#Requires -Version 7.0
# Funções comuns do spike P7-CL0-01. Carregado por dot-source em Invoke-ClaudeCodeSpike.ps1.
# Regras: nunca abrir ~/.claude nem arquivos de credencial; argv estruturado, sem shell; saída sanitizada.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ('SlopSpike.SpikeProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'SpikeProcess.cs')
}

function Resolve-ClaudeExecutable([string]$ClaudePath) {
    if ($ClaudePath) { $candidate = $ClaudePath }
    else {
        $cmd = Get-Command claude -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $cmd) {
            $winget = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
            $cmd = Get-ChildItem -LiteralPath $winget -Filter 'claude.exe' -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like '*Anthropic.ClaudeCode*' } | Select-Object -First 1
            $candidate = $cmd.FullName
        }
        else { $candidate = $cmd.Source }
    }
    if (-not $candidate -or -not [IO.Path]::IsPathFullyQualified($candidate)) { throw 'claude: caminho absoluto não encontrado' }
    $ext = [IO.Path]::GetExtension($candidate).ToLowerInvariant()
    if ($IsWindows -and $ext -ne '.exe') { throw "claude: somente executável nativo (.exe) é aceito; recebido '$ext'" }
    return (Get-Item -LiteralPath $candidate).FullName
}

# Variáveis herdadas do host que executa o agente de desenvolvimento (Claude Desktop/Claude Code) não
# representam o ambiente do usuário final. O spike as remove SOMENTE do processo filho, por prefixo.
$script:HostEnvPrefixes = @('CLAUDE', 'ANTHROPIC')

$script:Secrets = [System.Collections.Generic.List[string]]::new()

function Register-SanitizerSecret([string]$value) {
    if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Length -ge 3) { $script:Secrets.Add($value) }
}

function ConvertTo-SanitizedText([string]$text, [string]$WorkRoot) {
    if ($null -eq $text) { return $text }
    foreach ($s in $script:Secrets) { $text = $text.Replace($s, '<REDACTED>') }
    $text = [regex]::Replace($text, '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', '<EMAIL>')
    if ($WorkRoot) {
        # Mais longo primeiro: JSON dentro de string JSON (\\\\), JSON (\\), nativo e com barras.
        foreach ($variant in @($WorkRoot.Replace('\', '\\\\'), $WorkRoot.Replace('\', '\\'), $WorkRoot, $WorkRoot.Replace('\', '/'))) {
            $text = [regex]::Replace($text, [regex]::Escape($variant), '<WORK>', 'IgnoreCase')
        }
    }
    $profileDir = [Environment]::GetFolderPath('UserProfile')
    foreach ($variant in @($profileDir.Replace('\', '\\\\'), $profileDir.Replace('\', '\\'), $profileDir, $profileDir.Replace('\', '/'),
            ('/' + $profileDir.Substring(0, 1).ToLowerInvariant() + $profileDir.Substring(2).Replace('\', '/')))) {
        $text = [regex]::Replace($text, [regex]::Escape($variant), '<HOME>', 'IgnoreCase')
    }
    $text = [regex]::Replace($text, [regex]::Escape($env:USERNAME), '<USER>', 'IgnoreCase')
    $text = [regex]::Replace($text, '(?i)\b([0-9a-f]{8})-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b', '$1-…')
    $text = [regex]::Replace($text, '("signature":\s*")([A-Za-z0-9+/=]{8})[A-Za-z0-9+/=]*"', '$1$2…"')
    $text = [regex]::Replace($text, '(sk-ant-[A-Za-z0-9_-]{2})[A-Za-z0-9_-]+', '$1…')
    return $text
}

function Get-AuthStatus([string]$Claude, [string]$WorkDir, [hashtable]$SetEnv, [switch]$InheritHostEnv, [string[]]$ExtraArgs, [string[]]$TailArgs) {
    $argv = @()
    if ($ExtraArgs) { $argv += $ExtraArgs }
    $argv += @('auth', 'status')
    if ($TailArgs) { $argv += $TailArgs }
    [string[]]$remove = if ($InheritHostEnv) { $null } else { $script:HostEnvPrefixes }
    $p = [SlopSpike.SpikeProcess]::new($Claude, [string[]]$argv, $WorkDir, $SetEnv, $remove, $false)
    try {
        $p.CloseStdin()
        [void]$p.WaitForExit(30000)
        $out = ($p.Snapshot() | Where-Object Stream -eq 'out' | ForEach-Object Text) -join "`n"
        $err = ($p.Snapshot() | Where-Object Stream -eq 'err' | ForEach-Object Text) -join "`n"
        $json = $null
        try { $json = $out | ConvertFrom-Json -AsHashtable } catch { }
        if ($json) { foreach ($k in 'email', 'orgId', 'orgName', 'accountUuid', 'organizationUuid') { if ($json.ContainsKey($k)) { Register-SanitizerSecret ([string]$json[$k]) } } }
        return [pscustomobject]@{ Args = $argv; ExitCode = $p.ExitCode; Json = $json; Stdout = $out; Stderr = $err }
    }
    finally { $p.Dispose() }
}

# Allowlist de campos de auth status (T-P06): somente estes vão para o transcript.
function Select-AuthStatusFields($json) {
    if (-not $json) { return $null }
    $o = [ordered]@{}
    foreach ($k in 'loggedIn', 'authMethod', 'apiProvider', 'apiKeySource', 'subscriptionType', 'analyticsDisabled') {
        if ($json.ContainsKey($k)) { $o[$k] = $json[$k] }
    }
    $o['fieldNames'] = @($json.Keys | Sort-Object)
    return $o
}

function Save-Transcript([SlopSpike.SpikeProcess]$p, [string]$Path, [string]$WorkRoot, [object]$Header) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add((ConvertTo-SanitizedText ((@{ header = $Header } | ConvertTo-Json -Compress -Depth 20)) $WorkRoot))
    foreach ($l in $p.Snapshot()) {
        $rec = [ordered]@{ ms = [math]::Round($l.Ms); s = $l.Stream }
        $parsed = $null
        if ($l.Stream -in 'out', 'stdin') { try { $parsed = $l.Text | ConvertFrom-Json -AsHashtable -Depth 100 } catch { } }
        if ($parsed) { $rec['json'] = $parsed } else { $rec['text'] = $l.Text }
        $lines.Add((ConvertTo-SanitizedText ($rec | ConvertTo-Json -Compress -Depth 100) $WorkRoot))
    }
    Set-Content -LiteralPath $Path -Value $lines -Encoding utf8NoBOM
}

function Get-StdoutJson([SlopSpike.SpikeProcess]$p) {
    foreach ($l in $p.Snapshot()) {
        if ($l.Stream -ne 'out') { continue }
        try { $l.Text | ConvertFrom-Json -AsHashtable -Depth 100 } catch { }
    }
}

function New-UserMessage([string]$text) {
    return (@{ type = 'user'; message = @{ role = 'user'; content = @(@{ type = 'text'; text = $text }) } } | ConvertTo-Json -Compress -Depth 10)
}
