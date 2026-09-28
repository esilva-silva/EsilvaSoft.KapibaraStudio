#Requires -Version 7.0
<#
.SYNOPSIS
  Spike reproduzível P7-CL0-01: comportamento do Claude Code instalado (auth status, stream-json,
  resume, --permission-prompt-tool, fontes de configuração, cancelamento no Windows).

.PARAMETER Scenario
  Nome de um cenário (ver $Scenarios abaixo) ou 'list'.

.PARAMETER AllowModelCalls
  Obrigatório para cenários que escrevem mensagem no stdin (consomem a cota da assinatura).
  Sem ele, somente cenários sem chamada ao modelo executam.

.NOTES
  - Nunca lê ~/.claude nem arquivos de credencial; nunca executa login/logout.
  - Variáveis de host com prefixo CLAUDE/ANTHROPIC são removidas somente do processo filho
    (o spike roda dentro de outra sessão Claude Code; essas variáveis não são do usuário final).
  - Valores de API Key usados são FALSOS ("sk-ant-invalid-spike") e só em cenários sem mensagem.
  - Transcripts são sanitizados (e-mail/org/ids de conta/perfil/UUIDs truncados).
#>
param(
    [Parameter(Mandatory)][string]$Scenario,
    [string]$ClaudePath,
    [string]$WorkRoot = (Join-Path ([IO.Path]::GetTempPath()) 'slop-cl0-spike'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'transcripts'),
    [string]$Model = 'haiku',
    [switch]$AllowModelCalls
)

. (Join-Path $PSScriptRoot 'ClaudeSpike.Common.ps1')

$Claude = Resolve-ClaudeExecutable $ClaudePath
New-Item -ItemType Directory -Force -Path $WorkRoot, $OutDir | Out-Null
$WorkRoot = (Get-Item -LiteralPath $WorkRoot).FullName
$CounterPath = Join-Path $WorkRoot 'model-calls.txt'
$FakeKey = 'sk-ant-invalid-spike'

# Registra e-mail/org da conta só em memória, para o sanitizador.
[void](Get-AuthStatus -Claude $Claude -WorkDir $WorkRoot)

function New-WorkDir([string]$name) {
    $d = Join-Path $WorkRoot $name
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d | Out-Null
    return $d
}

function Add-ModelCall([string]$label) {
    if (-not $AllowModelCalls) { throw "Cenário '$label' chama o modelo; use -AllowModelCalls." }
    Add-Content -LiteralPath $CounterPath -Value ("{0}`t{1}" -f [DateTimeOffset]::Now.ToString('s'), $label)
}

function Get-TurnArgs([int]$MaxTurns = 2, [string[]]$Extra = @()) {
    return @('-p', '--output-format', 'stream-json', '--input-format', 'stream-json', '--verbose',
        '--include-partial-messages', '--model', $Model, '--max-turns', "$MaxTurns") + $Extra
}

function Start-Claude([string[]]$Argv, [string]$Dir, [hashtable]$SetEnv = @{}, [switch]$Job) {
    $envMap = @{ SLOP_SPIKE_CANARY = '1' } + $SetEnv
    return [SlopSpike.SpikeProcess]::new($Claude, $Argv, $Dir, $envMap, [string[]]$script:HostEnvPrefixes, [bool]$Job)
}

function New-McpConfig([string]$Dir, [string]$Policy, [string]$LogPath) {
    $server = Join-Path $PSScriptRoot 'PermissionMcpServer.ps1'
    $pwsh = (Get-Process -Id $PID).Path
    $cfg = @{ mcpServers = @{ slopperm = @{ type = 'stdio'; command = $pwsh
                args = @('-NoProfile', '-NonInteractive', '-File', $server, '-LogPath', $LogPath, '-Policy', $Policy) } } }
    # Fica FORA do cwd do turno (T-M01), no WorkRoot do spike.
    $path = Join-Path $WorkRoot ("mcp-" + [IO.Path]::GetFileName($Dir) + ".json")
    Set-Content -LiteralPath $path -Value ($cfg | ConvertTo-Json -Depth 10) -Encoding utf8NoBOM
    return $path
}

function Save-McpLog([string]$LogPath, [string]$Name) {
    if (Test-Path -LiteralPath $LogPath) {
        $txt = (Get-Content -LiteralPath $LogPath -Raw)
        Set-Content -LiteralPath (Join-Path $OutDir "$Name.mcp.jsonl") -Value (ConvertTo-SanitizedText $txt $WorkRoot) -Encoding utf8NoBOM -NoNewline
    }
}

function Summarize([SlopSpike.SpikeProcess]$p) {
    $events = @(Get-StdoutJson $p)
    $types = $events | ForEach-Object { if ($_['type'] -eq 'system' -or $_['type'] -eq 'stream_event') { "$($_['type'])/$($_['subtype'])$($_['event']?['type'])" } else { $_['type'] } }
    $grouped = $types | Group-Object | ForEach-Object { "$($_.Name)x$($_.Count)" }
    Write-Host ("  eventos: " + ($grouped -join ', '))
    $result = $events | Where-Object { $_['type'] -eq 'result' } | Select-Object -Last 1
    if ($result) {
        Write-Host ("  result: subtype={0} is_error={1} turns={2} cost={3} denials={4}" -f $result['subtype'], $result['is_error'], $result['num_turns'], $result['total_cost_usd'], (@($result['permission_denials']).Count))
        Write-Host ("  texto: " + (ConvertTo-SanitizedText ([string]$result['result']) $WorkRoot))
    }
}

function Invoke-Turn {
    param([string]$Name, [string[]]$Argv, [string]$Dir, [string[]]$Messages = @(), [hashtable]$SetEnv = @{},
        [int]$TimeoutMs = 120000, [scriptblock]$During, [switch]$Job, [switch]$KeepStdinOpen)
    foreach ($m in $Messages) { Add-ModelCall $Name }
    $p = Start-Claude -Argv $Argv -Dir $Dir -SetEnv $SetEnv -Job:$Job
    try {
        foreach ($m in $Messages) { $p.WriteLine((New-UserMessage $m)) }
        if ($During) { & $During $p }
        elseif (-not $KeepStdinOpen) {
            [void]$p.WaitForStdout({ param($t) $t -like '*"type":"result"*' }, $TimeoutMs)
            $p.CloseStdin()
        }
        [void]$p.WaitForExit(30000)
        Save-Transcript $p (Join-Path $OutDir "$Name.jsonl") $WorkRoot @{ scenario = $Name; argv = $Argv; setEnvNames = @($SetEnv.Keys); claudeVersion = $script:Version }
        Write-Host "[$Name] exit=$(if ($p.HasExited) { $p.ExitCode } else { 'running' })"
        Summarize $p
        return , @(Get-StdoutJson $p)
    }
    finally { $p.Dispose() }
}

$script:Version = (& $Claude --version).Trim()

$Scenarios = [ordered]@{

    # ---------- Sem chamada ao modelo ----------
    'auth' = {
        $dir = New-WorkDir 'auth'
        $cases = [ordered]@{
            'clean-env'                 = @{ env = @{}; extra = @() }
            'inherit-host-env'          = @{ env = @{}; extra = @(); inherit = $true }
            'fake-ANTHROPIC_API_KEY'    = @{ env = @{ ANTHROPIC_API_KEY = $FakeKey }; extra = @() }
            'fake-ANTHROPIC_AUTH_TOKEN' = @{ env = @{ ANTHROPIC_AUTH_TOKEN = $FakeKey }; extra = @() }
            'fake-CLAUDE_CODE_OAUTH_TOKEN' = @{ env = @{ CLAUDE_CODE_OAUTH_TOKEN = $FakeKey }; extra = @() }
            'settings-apiKeyHelper'     = @{ env = @{}; extra = @('--settings', ('{"apiKeyHelper":"echo ' + $FakeKey + '"}')) }
            'settings-env-api-key'      = @{ env = @{}; extra = @('--settings', ('{"env":{"ANTHROPIC_API_KEY":"' + $FakeKey + '"}}')) }
            'setting-sources-empty'     = @{ env = @{}; extra = @('--setting-sources', '') }
            'fake-key-plus-sources-empty' = @{ env = @{ ANTHROPIC_API_KEY = $FakeKey }; extra = @('--setting-sources', '') }
        }
        $rows = foreach ($k in $cases.Keys) {
            $c = $cases[$k]
            $r = Get-AuthStatus -Claude $Claude -WorkDir $dir -SetEnv $c.env -InheritHostEnv:([bool]$c['inherit']) -ExtraArgs $c.extra
            [ordered]@{ case = $k; exit = $r.ExitCode; argvPrefix = $c.extra; fields = (Select-AuthStatusFields $r.Json)
                stderr = (ConvertTo-SanitizedText $r.Stderr $WorkRoot); stdoutIfNotJson = $(if ($r.Json) { $null } else { ConvertTo-SanitizedText $r.Stdout $WorkRoot }) }
        }
        # apiKeyHelper vindo de settings de PROJETO sintético no cwd (não toca ~/.claude).
        $proj = New-WorkDir 'auth-project'
        New-Item -ItemType Directory -Path (Join-Path $proj '.claude') | Out-Null
        Set-Content -LiteralPath (Join-Path $proj '.claude\settings.json') -Encoding utf8NoBOM -Value ('{"apiKeyHelper":"echo ' + $FakeKey + '"}')
        $rows = @($rows)
        foreach ($c in @(@{ n = 'project-apiKeyHelper-default'; x = @() }, @{ n = 'project-apiKeyHelper-sources-user'; x = @('--setting-sources', 'user') },
                @{ n = 'project-apiKeyHelper-sources-empty'; x = @('--setting-sources', '') }, @{ n = 'project-apiKeyHelper-sources-project'; x = @('--setting-sources', 'project') },
                @{ n = 'setting-sources-invalid'; x = @('--setting-sources', 'bogus') }, @{ n = 'setting-sources-managed'; x = @('--setting-sources', 'managed') })) {
            $r = Get-AuthStatus -Claude $Claude -WorkDir $proj -ExtraArgs $c.x
            $rows += [ordered]@{ case = $c.n; exit = $r.ExitCode; argvPrefix = $c.x; fields = (Select-AuthStatusFields $r.Json)
                stderr = (ConvertTo-SanitizedText $r.Stderr $WorkRoot); stdoutIfNotJson = $(if ($r.Json) { $null } else { ConvertTo-SanitizedText $r.Stdout $WorkRoot }) }
        }
        $txt = Get-AuthStatus -Claude $Claude -WorkDir $dir -TailArgs @('--text')
        $rows += [ordered]@{ case = 'text-output-shape'; exit = $txt.ExitCode; lineCount = @($txt.Stdout -split "`n").Count
            labels = @($txt.Stdout -split "`n" | ForEach-Object { ($_ -split ':')[0].Trim() } | Where-Object { $_ }) }
        $rows | ConvertTo-Json -Depth 10 | ForEach-Object { ConvertTo-SanitizedText $_ $WorkRoot } |
            Set-Content -LiteralPath (Join-Path $OutDir 'auth-status.json') -Encoding utf8NoBOM
        $rows | ForEach-Object { Write-Host ("{0,-30} exit={1} {2}" -f $_["case"], $_["exit"], (($_["fields"] | ConvertTo-Json -Compress -Depth 5))) }
    }

    'init-only' = {
        # Sem mensagem no stdin: observa se system/init chega antes do primeiro stdin (H-21) e seus campos.
        $variants = [ordered]@{
            'init-default'            = @()
            'init-permission-default' = @('--permission-mode', 'default')
            'init-sources-empty'      = @('--setting-sources', '')
            'init-tools-read'         = @('--tools', 'Read,Glob')
            'init-disallowed'         = @('--disallowedTools', 'Agent,Task,WebFetch,WebSearch,Skill')
            'init-no-persistence'     = @('--no-session-persistence')
            'init-restricted'         = @('--restricted')
            'init-safe-mode'          = @('--safe-mode')
        }
        foreach ($name in $variants.Keys) {
            $dir = New-WorkDir $name
            $argv = Get-TurnArgs -Extra $variants[$name]
            [void](Invoke-Turn -Name $name -Argv $argv -Dir $dir -During {
                    param($p)
                    $seen = $p.WaitForStdout({ param($t) $t -like '*"subtype":"init"*' }, 20000)
                    $p.Mark("init before stdin: $seen")
                    $p.CloseStdin()
                })
        }
    }

    'init-mcp' = {
        # --mcp-config + --strict-mcp-config + --permission-prompt-tool sem mensagem: init, MCP e H-05/H-11.
        foreach ($v in @(@{ n = 'init-mcp-visible'; x = @() }, @{ n = 'init-mcp-hidden'; x = @('--disallowedTools', 'mcp__slopperm__approve') })) {
            $dir = New-WorkDir $v.n
            $log = Join-Path $WorkRoot "$($v.n).mcp.log"; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
            $cfg = New-McpConfig $dir 'allow' $log
            $argv = Get-TurnArgs -Extra (@('--mcp-config', $cfg, '--strict-mcp-config', '--permission-prompt-tool', 'mcp__slopperm__approve') + $v.x)
            [void](Invoke-Turn -Name $v.n -Argv $argv -Dir $dir -During {
                    param($p)
                    $seen = $p.WaitForStdout({ param($t) $t -like '*"subtype":"init"*' }, 40000)
                    $p.Mark("init before stdin: $seen")
                    $p.CloseStdin()
                })
            Save-McpLog $log $v.n
        }
    }

    # ---------- Com chamada ao modelo (contadas) ----------
    'basic' = {
        $dir = New-WorkDir 'basic'
        $sid = [guid]::NewGuid().ToString()
        $argv = Get-TurnArgs -MaxTurns 1 -Extra @('--session-id', $sid, '--setting-sources', '')
        [void](Invoke-Turn -Name 'basic-turn1' -Argv $argv -Dir $dir -Messages @('Memorize a palavra ABACAXI-7. Responda apenas: ok'))
        $argv2 = Get-TurnArgs -MaxTurns 1 -Extra @('--resume', $sid, '--setting-sources', '')
        [void](Invoke-Turn -Name 'basic-turn2-resume' -Argv $argv2 -Dir $dir -Messages @('Qual palavra eu pedi para memorizar? Responda so a palavra.'))
    }

    'no-persistence' = {
        $dir = New-WorkDir 'nopersist'
        $sid = [guid]::NewGuid().ToString()
        [void](Invoke-Turn -Name 'nopersist-turn1' -Argv (Get-TurnArgs -MaxTurns 1 -Extra @('--session-id', $sid, '--no-session-persistence', '--setting-sources', '')) -Dir $dir -Messages @('Responda apenas: ok'))
        # Resume sem mensagem: verifica se a sessão existe (sem custo se falhar antes do modelo).
        [void](Invoke-Turn -Name 'nopersist-resume-probe' -Argv (Get-TurnArgs -MaxTurns 1 -Extra @('--resume', $sid, '--setting-sources', '')) -Dir $dir -During {
                param($p); [void]$p.WaitForStdout({ param($t) $t -like '*"subtype":"init"*' -or $t -like '*"type":"result"*' }, 20000); Start-Sleep -Seconds 2; $p.CloseStdin() })
    }
}

function Invoke-PermissionTurn {
    param([string]$Name, [string]$Policy, [string]$Prompt, [string[]]$Extra = @(), [int]$MaxTurns = 4,
        [scriptblock]$Prepare, [scriptblock]$During, [switch]$Job, [int]$TimeoutMs = 180000, [hashtable]$SetEnv = @{})
    $dir = New-WorkDir $Name
    if ($Prepare) { & $Prepare $dir }
    $log = Join-Path $WorkRoot "$Name.mcp.log"; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    $cfg = New-McpConfig $dir $Policy $log
    $argv = Get-TurnArgs -MaxTurns $MaxTurns -Extra (@('--mcp-config', $cfg, '--strict-mcp-config', '--permission-prompt-tool', 'mcp__slopperm__approve', '--setting-sources', '') + $Extra)
    $r = Invoke-Turn -Name $Name -Argv $argv -Dir $dir -Messages @($Prompt) -During $During -Job:$Job -TimeoutMs $TimeoutMs -SetEnv $SetEnv
    Save-McpLog $log $Name
    Write-Host "  cwd final: $((Get-ChildItem -LiteralPath $dir -Force | ForEach-Object Name) -join ', ')"
    return $r
}

$prepareFiles = { param($d) Set-Content -LiteralPath (Join-Path $d 'a.txt') -Value 'conteudo sintetico CANARIO-A1' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $d 'secret.txt') -Value 'conteudo sintetico CANARIO-S9' -Encoding utf8NoBOM }

$Scenarios['perm-tools'] = {
    # Quais ferramentas pedem permissão no modo default (sem fontes de configuração do usuário).
    [void](Invoke-PermissionTurn -Name 'perm-tools' -Policy 'deny-webfetch' -MaxTurns 12 -Prepare $prepareFiles -Prompt (
            'Teste de ferramentas. Execute exatamente, uma ferramenta por vez, nesta ordem: ' +
            '1) Read em a.txt; 2) Glob com padrao *.txt; 3) Grep por CANARIO no diretorio atual; ' +
            '4) Write criando b.txt com o texto ok; 5) Edit em b.txt trocando ok por ok2; ' +
            '6) Bash com o comando: echo spike-bash; 7) Bash com o comando: mkdir sub; ' +
            '8) WebFetch em http://127.0.0.1:9/ (pode falhar); 9) TodoWrite com um item "x". ' +
            'Nao explique; ao final responda FIM.'))
}

$Scenarios['perm-allow-bash'] = {
    [void](Invoke-PermissionTurn -Name 'perm-allow-bash' -Policy 'allow' -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Depois responda FIM.')
}
$Scenarios['perm-deny-bash'] = {
    [void](Invoke-PermissionTurn -Name 'perm-deny-bash' -Policy 'deny' -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Se negado, nao tente de novo; responda NEGADO.')
}
$Scenarios['perm-rewrite'] = {
    [void](Invoke-PermissionTurn -Name 'perm-rewrite' -Policy 'rewrite' -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Depois repita literalmente a saida do comando e responda FIM.')
}
$Scenarios['perm-allow-bare'] = {
    [void](Invoke-PermissionTurn -Name 'perm-allow-bare' -Policy 'allow-bare' -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Depois responda FIM.')
}
$Scenarios['perm-persist'] = {
    # Resposta com updatedPermissions (destination session): observa se algo é gravado no cwd (.claude/).
    [void](Invoke-PermissionTurn -Name 'perm-persist' -Policy 'persist' -Prompt 'Use a ferramenta Bash duas vezes, uma de cada vez: mkdir p1 ; depois mkdir p2 . Depois responda FIM.')
}
$Scenarios['perm-settings'] = {
    # --settings do Slop: ask para Read, deny para secret.txt e disableAllHooks.
    $settings = '{"disableAllHooks":true,"permissions":{"ask":["Read","Glob","Grep"],"deny":["Read(./secret.txt)","Bash(mkdir proibido*)"]}}'
    [void](Invoke-PermissionTurn -Name 'perm-settings' -Policy 'allow' -MaxTurns 8 -Prepare $prepareFiles -Extra @('--settings', $settings) -Prompt (
            'Uma ferramenta por vez: 1) Read em a.txt; 2) Read em secret.txt; 3) Bash: mkdir proibido1 ; 4) Glob *.txt. Nao repita ferramentas negadas. Responda FIM.'))
}
$Scenarios['perm-tool-direct'] = {
    # O modelo consegue chamar a tool de permissão diretamente? (H-05) Com e sem --disallowedTools.
    [void](Invoke-PermissionTurn -Name 'perm-tool-direct' -Policy 'allow' -Prompt 'Liste os nomes exatos de todas as ferramentas que voce tem disponiveis que comecem com mcp__. Se houver mcp__slopperm__approve, chame-a com {"tool_name":"Bash","input":{"command":"echo x"}}. Responda FIM.')
    [void](Invoke-PermissionTurn -Name 'perm-tool-hidden' -Policy 'allow' -Extra @('--disallowedTools', 'mcp__slopperm__approve') -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Depois liste os nomes das ferramentas mcp__ que voce tem. Responda FIM.')
}
$Scenarios['perm-timeout'] = {
    # Host demora a responder: a CLI espera, expira ou nega? Timeout curto via MCP_TOOL_TIMEOUT=8000 ms.
    [void](Invoke-PermissionTurn -Name 'perm-timeout-short' -Policy 'delay:25' -SetEnv @{ MCP_TOOL_TIMEOUT = '8000' } -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Se falhar, nao tente de novo; responda FALHOU ou FIM.')
}
$Scenarios['perm-timeout-default'] = {
    [void](Invoke-PermissionTurn -Name 'perm-timeout-default' -Policy 'delay:75' -TimeoutMs 200000 -Prompt 'Use a ferramenta Bash para executar exatamente: mkdir criado-pelo-bash . Se falhar, nao tente de novo; responda FALHOU ou FIM.')
}

function Get-PingProcesses { @(Get-CimInstance Win32_Process -Filter "Name='PING.EXE'" | Where-Object { $_.CommandLine -like '*-n 45 127.0.0.1*' }) }

function Wait-Ping([int]$ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { if (@(Get-PingProcesses).Count -gt 0) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }

$pingPrompt = 'Use a ferramenta Bash (sem run_in_background) para executar exatamente: ping -n 45 127.0.0.1 . Aguarde terminar e responda FIM.'

$Scenarios['cancel-kill-single'] = {
    [void](Invoke-PermissionTurn -Name 'cancel-kill-single' -Policy 'allow' -Prompt $pingPrompt -During {
            param($p)
            $started = Wait-Ping 90000; $p.Mark("ping started: $started")
            Start-Sleep -Seconds 2
            $p.KillSingle(); Start-Sleep -Seconds 3
            $p.Mark("ping alive after single kill: $(@(Get-PingProcesses).Count)")
            Get-PingProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        })
}
$Scenarios['cancel-job'] = {
    [void](Invoke-PermissionTurn -Name 'cancel-job' -Policy 'allow' -Job -Prompt $pingPrompt -During {
            param($p)
            $started = Wait-Ping 90000; $p.Mark("ping started: $started")
            Start-Sleep -Seconds 2
            $p.CloseJob(); Start-Sleep -Seconds 3
            $p.Mark("claude exited: $($p.HasExited); ping alive after job close: $(@(Get-PingProcesses).Count)")
            Get-PingProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        })
}
$Scenarios['cancel-interrupt'] = {
    # control_request interrupt pelo stdin (protocolo stream-json usado pelos SDKs).
    [void](Invoke-PermissionTurn -Name 'cancel-interrupt' -Policy 'allow' -Prompt $pingPrompt -During {
            param($p)
            $started = Wait-Ping 90000; $p.Mark("ping started: $started")
            Start-Sleep -Seconds 2
            $p.WriteLine('{"type":"control_request","request_id":"spike-int-1","request":{"subtype":"interrupt"}}')
            [void]$p.WaitForStdout({ param($t) $t -like '*"type":"result"*' }, 20000)
            Start-Sleep -Seconds 2
            $p.Mark("after interrupt: claude exited=$($p.HasExited); ping alive=$(@(Get-PingProcesses).Count)")
            $p.CloseStdin(); [void]$p.WaitForExit(15000)
            $p.Mark("after stdin close: claude exited=$($p.HasExited); ping alive=$(@(Get-PingProcesses).Count)")
            Get-PingProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        })
}
$Scenarios['cancel-stdin-close'] = {
    [void](Invoke-PermissionTurn -Name 'cancel-stdin-close' -Policy 'allow' -Prompt $pingPrompt -During {
            param($p)
            $started = Wait-Ping 90000; $p.Mark("ping started: $started")
            Start-Sleep -Seconds 2
            $p.CloseStdin(); $exited = $p.WaitForExit(20000)
            $p.Mark("after stdin close: claude exited=$exited; ping alive=$(@(Get-PingProcesses).Count)")
            Get-PingProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        })
}
$Scenarios['cancel-kill-tree'] = {
    [void](Invoke-PermissionTurn -Name 'cancel-kill-tree' -Policy 'allow' -Prompt $pingPrompt -During {
            param($p)
            $started = Wait-Ping 90000; $p.Mark("ping started: $started")
            Start-Sleep -Seconds 2
            $p.KillTree(); Start-Sleep -Seconds 3
            $p.Mark("ping alive after tree kill: $(@(Get-PingProcesses).Count)")
            Get-PingProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
        })
}
$prepareProject = { param($d)
    New-Item -ItemType Directory -Path (Join-Path $d '.claude') | Out-Null
    Set-Content -LiteralPath (Join-Path $d '.claude\settings.json') -Encoding utf8NoBOM -Value (
        '{"permissions":{"allow":["Bash(mkdir allowed*)"]},"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo hook-ran > hook-ran.txt"}]}]}}')
    Set-Content -LiteralPath (Join-Path $d '.mcp.json') -Encoding utf8NoBOM -Value (
        '{"mcpServers":{"projectdummy":{"type":"stdio","command":"' + ((Get-Process -Id $PID).Path.Replace('\', '\\')) + '","args":["-NoProfile","-Command","exit 0"]}}}')
}
$projectPrompt = 'Uma ferramenta por vez: 1) Bash: mkdir allowed1 ; 2) Bash: echo "$0 $BASH_VERSION" ; 3) Bash: alias | head -3 . Responda FIM.'
$Scenarios['project-config'] = {
    # Fontes padrão (user, project, local) + sem --strict-mcp-config: hooks, allow e .mcp.json de projeto em -p.
    $dir = New-WorkDir 'project-config'; & $prepareProject $dir
    $log = Join-Path $WorkRoot 'project-config.mcp.log'; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    $cfg = New-McpConfig $dir 'allow' $log
    [void](Invoke-Turn -Name 'project-config' -Dir $dir -Messages @($projectPrompt) -Argv (Get-TurnArgs -MaxTurns 5 -Extra @('--mcp-config', $cfg, '--permission-prompt-tool', 'mcp__slopperm__approve', '--include-hook-events')))
    Save-McpLog $log 'project-config'
    Write-Host "  cwd final: $((Get-ChildItem -LiteralPath $dir -Force | ForEach-Object Name) -join ', ')"
}
$Scenarios['project-config-isolated'] = {
    # Mesmo projeto com --setting-sources user + --strict-mcp-config + --settings disableAllHooks.
    $dir = New-WorkDir 'project-config-isolated'; & $prepareProject $dir
    $log = Join-Path $WorkRoot 'project-config-isolated.mcp.log'; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    $cfg = New-McpConfig $dir 'allow' $log
    [void](Invoke-Turn -Name 'project-config-isolated' -Dir $dir -Messages @($projectPrompt) -Argv (Get-TurnArgs -MaxTurns 5 -Extra @('--mcp-config', $cfg, '--strict-mcp-config', '--permission-prompt-tool', 'mcp__slopperm__approve', '--setting-sources', 'user', '--settings', '{"disableAllHooks":true}', '--include-hook-events')))
    Save-McpLog $log 'project-config-isolated'
    Write-Host "  cwd final: $((Get-ChildItem -LiteralPath $dir -Force | ForEach-Object Name) -join ', ')"
}
$Scenarios['tools-restrict'] = {
    [void](Invoke-PermissionTurn -Name 'tools-restrict' -Policy 'allow' -MaxTurns 3 -Extra @('--tools', 'Bash,Read,Edit,Write,Glob,Grep,WebFetch', '--disallowedTools', 'Agent,Task,Skill') -Prompt 'Liste os nomes exatos das ferramentas que voce tem. Depois tente delegar a um subagente (ferramenta Agent ou Task) a tarefa trivial de responder ok; se nao existir, diga SEM-AGENT. Responda FIM.')
}
$Scenarios['restricted'] = {
    [void](Invoke-PermissionTurn -Name 'restricted' -Policy 'allow' -MaxTurns 3 -Extra @('--restricted') -Prompt 'Liste os nomes exatos das ferramentas que voce tem e responda FIM. Nao use ferramentas.')
}
$Scenarios['shell-probe'] = {
    # Nomes (nunca valores) de variáveis CLAUDE*/ANTHROPIC*/SLOP_SPIKE* visíveis a um comando Bash aprovado.
    [void](Invoke-PermissionTurn -Name 'shell-probe' -Policy 'allow' -Prompt 'Use a ferramenta Bash para executar exatamente: env | cut -d= -f1 | grep -E "^(CLAUDE|ANTHROPIC|SLOP_SPIKE)" | sort . Repita a saida e responda FIM.')
}

if ($Scenario -eq 'list') { $Scenarios.Keys; return }
if (-not $Scenarios.Contains($Scenario)) { throw "Cenário desconhecido: $Scenario" }
Write-Host "claude: $(ConvertTo-SanitizedText $Claude $WorkRoot) ($script:Version)"
& $Scenarios[$Scenario]
if (Test-Path -LiteralPath $CounterPath) { Write-Host ("chamadas ao modelo acumuladas: " + @(Get-Content -LiteralPath $CounterPath).Count) }
