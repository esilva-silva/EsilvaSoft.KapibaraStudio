#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CodexPath,
    [Parameter(Mandatory = $true)] [string] $SchemaManifestPath,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory,
    [ValidateRange(1, 60)] [int] $TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'

function Resolve-CodexExecutable {
    param([string] $RequestedPath)

    if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        $names = if ($IsWindows) { @('codex.exe', 'codex') } else { @('codex', 'codex.exe') }
        $command = $null
        foreach ($name in $names) {
            $command = Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($null -ne $command) { break }
        }
        if ($null -eq $command) {
            throw 'O executável Codex não foi encontrado no PATH. Informe -CodexPath com o caminho local.'
        }
        $RequestedPath = $command.Source
    }

    $resolved = Resolve-Path -LiteralPath $RequestedPath -ErrorAction Stop
    if ($resolved.Provider.Name -ne 'FileSystem' -or
        -not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
        throw 'CodexPath precisa apontar para um arquivo executável local existente.'
    }

    $resolvedPath = [IO.Path]::GetFullPath($resolved.Path)
    if ([IO.Path]::GetFileName($resolvedPath) -notin @('codex', 'codex.exe')) {
        throw 'CodexPath deve apontar para codex (Linux/macOS) ou codex.exe (Windows).'
    }
    return $resolvedPath
}

function Remove-ValidatedDirectory {
    param(
        [string] $TargetPath,
        [string] $ExpectedParent,
        [string] $ExpectedLeaf
    )

    $targetFull = [IO.Path]::GetFullPath($TargetPath)
    $parentFull = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $actualParent = [IO.Path]::GetFullPath((Split-Path -Parent $targetFull))
    $parentPrefix = $parentFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($actualParent, $parentFull, $comparison) -or
        [IO.Path]::GetFileName($targetFull) -cne $ExpectedLeaf -or
        -not $targetFull.StartsWith($parentPrefix, $comparison)) {
        throw "Recusa de limpeza: caminho não corresponde ao diretório esperado: $targetFull"
    }

    if (-not (Test-Path -LiteralPath $targetFull -PathType Container)) { return }
    $targetItem = Get-Item -LiteralPath $targetFull -Force
    if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Recusa de limpeza de reparse point: $targetFull"
    }
    $nestedReparsePoint = Get-ChildItem -LiteralPath $targetFull -Force -Recurse -ErrorAction Stop |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
        Select-Object -First 1
    if ($null -ne $nestedReparsePoint) {
        throw "Recusa de limpeza de árvore com reparse point: $targetFull"
    }
    Remove-Item -LiteralPath $targetFull -Recurse -Force
}

function Assert-NoReparseAncestors {
    param([string] $Path)

    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Caminho contém link/reparse point e não foi aceito: $current"
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Set-PrivateDirectoryPermissions {
    param([string] $Path)

    if ($IsWindows) {
        $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        if ($null -eq $currentSid) { throw 'Não foi possível determinar o SID do usuário atual.' }
        $security = [Security.AccessControl.DirectorySecurity]::new()
        $security.SetAccessRuleProtection($true, $false)
        $security.SetOwner($currentSid)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $currentSid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void] $security.AddAccessRule($rule)
        Set-Acl -LiteralPath $Path -AclObject $security

        $verifiedAcl = Get-Acl -LiteralPath $Path
        $rules = @($verifiedAcl.Access)
        $ruleSid = if ($rules.Count -eq 1) {
            $rules[0].IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        }
        if (-not $verifiedAcl.AreAccessRulesProtected -or $rules.Count -ne 1 -or
            $ruleSid -ne $currentSid.Value -or
            $rules[0].IsInherited -or
            $rules[0].AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rules[0].FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl) {
            throw 'ACL do diretório temporário não ficou restrita ao usuário atual.'
        }
    }
    elseif ($IsLinux -or $IsMacOS) {
        $privateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
        [IO.File]::SetUnixFileMode($Path, $privateMode)
        if ([IO.File]::GetUnixFileMode($Path) -ne $privateMode) {
            throw 'Permissões POSIX do diretório temporário não ficaram em 0700.'
        }
    }
    else {
        throw 'Plataforma sem implementação de permissões privadas; coleta recusada.'
    }
}

if (-not [IO.Path]::IsPathFullyQualified($CodexPath) -or
    -not [IO.Path]::IsPathFullyQualified($SchemaManifestPath) -or
    -not [IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    throw 'Informe caminhos absolutos para executável, manifesto e saída.'
}
$codex = Resolve-CodexExecutable -RequestedPath $CodexPath
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
if (-not (Test-Path -LiteralPath $outputRoot -PathType Container)) {
    throw 'OutputDirectory precisa ser um diretório local existente.'
}
Assert-NoReparseAncestors -Path $outputRoot
if ($IsWindows) {
    $driveName = [IO.Path]::GetPathRoot($outputRoot).TrimEnd('\').TrimEnd(':')
    $drive = Get-PSDrive -Name $driveName -PSProvider FileSystem -ErrorAction SilentlyContinue
    if ($outputRoot.StartsWith('\\') -or $null -eq $drive -or
        -not [string]::IsNullOrWhiteSpace($drive.DisplayRoot)) {
        throw 'OutputDirectory precisa estar em volume local, sem UNC ou drive mapeado.'
    }
}
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedHandshake.cs')
$manifest = [SlopCodexHandshake]::ReadSchemaManifest($SchemaManifestPath)
$binaryHash = (Get-FileHash -LiteralPath $codex -Algorithm SHA256).Hash.ToLowerInvariant()
if ($binaryHash -cne $manifest.Sha256) {
    throw 'SHA-256 do executável diverge do manifesto; processo não iniciado.'
}
$evidencePath = Join-Path $outputRoot 'codex-app-server-handshake.json'
if (Test-Path -LiteralPath $evidencePath) { throw 'Evidência já existe; escolha outro diretório.' }
$scratchParent = [IO.Path]::GetTempPath()
Assert-NoReparseAncestors -Path $scratchParent
$scratchLeaf = 'slop-phase07-codex-handshake-' + [guid]::NewGuid().ToString('N')
$scratchRoot = Join-Path $scratchParent $scratchLeaf
$isolatedHome = Join-Path $scratchRoot 'codex-home'
$evidence = $null
try {
    [void] (New-Item -ItemType Directory -Path $scratchRoot)
    Set-PrivateDirectoryPermissions -Path $scratchRoot
    foreach ($relative in @('codex-home', 'work', 'temp', 'config', 'cache', 'data', 'AppData/Roaming', 'AppData/Local')) {
        [void] (New-Item -ItemType Directory -Path (Join-Path $scratchRoot $relative))
    }
    # Explicit memory-only auth configuration for this unauthenticated probe.
    # This does not test keyring availability or isolation and never invokes login/logout.
    'cli_auth_credentials_store = "ephemeral"' |
        Set-Content -LiteralPath (Join-Path $isolatedHome 'config.toml') -Encoding utf8NoBOM
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $codex
    $startInfo.WorkingDirectory = Join-Path $scratchRoot 'work'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('app-server', '--listen', 'stdio://')) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $startInfo.Environment[$name] = $value }
    }
    # Do not inherit user PATH, API keys, tokens, proxy variables, Codex flags, or config.
    $startInfo.Environment['PATH'] = [IO.Path]::GetDirectoryName($codex)
    $startInfo.Environment['CODEX_HOME'] = $isolatedHome
    $startInfo.Environment['HOME'] = $scratchRoot
    $startInfo.Environment['TEMP'] = Join-Path $scratchRoot 'temp'
    $startInfo.Environment['TMP'] = Join-Path $scratchRoot 'temp'
    $startInfo.Environment['TMPDIR'] = Join-Path $scratchRoot 'temp'
    $startInfo.Environment['XDG_CONFIG_HOME'] = Join-Path $scratchRoot 'config'
    $startInfo.Environment['XDG_CACHE_HOME'] = Join-Path $scratchRoot 'cache'
    $startInfo.Environment['XDG_DATA_HOME'] = Join-Path $scratchRoot 'data'
    if ($IsWindows) {
        $startInfo.Environment['USERPROFILE'] = $scratchRoot
        $startInfo.Environment['APPDATA'] = Join-Path $scratchRoot 'AppData/Roaming'
        $startInfo.Environment['LOCALAPPDATA'] = Join-Path $scratchRoot 'AppData/Local'
    }
    $startedAt = [DateTime]::UtcNow
    $probe = [SlopCodexHandshake]::RunAsync($startInfo, $isolatedHome, $TimeoutSeconds).GetAwaiter().GetResult()
    if ((Get-FileHash -LiteralPath $codex -Algorithm SHA256).Hash.ToLowerInvariant() -cne $binaryHash) {
        throw 'Executável mudou durante o probe; evidência recusada.'
    }
    $authFileCount = @(Get-ChildItem -LiteralPath $scratchRoot -File -Recurse -Force -Filter 'auth.json').Count
    if ($authFileCount -ne 0) { throw 'auth.json encontrado no scratch; evidência recusada.' }
    $evidence = [ordered]@{
        observedAtUtc = $startedAt.ToString('O')
        executable = [IO.Path]::GetFileName($codex)
        executableSha256 = $binaryHash
        versionFromMatchingSchemaManifest = $manifest.version
        operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
        command = 'app-server --listen stdio://'
        sentMethods = @('initialize', 'initialized')
        initializeResponseId = 1
        initializeSucceeded = $true
        initializedNotificationSent = $true
        initializedAcknowledgementExpected = $false
        codexHomeMatchesScratch = $probe.CodexHomeMatchesScratch
        platformFamily = $probe.PlatformFamily
        platformOs = $probe.PlatformOs
        responseBytesExcludingNewline = $probe.ResponseBytes
        trailingStdoutBytesDiscarded = $probe.TrailingStdoutBytes
        stderrBytesDiscarded = $probe.StderrBytes
        processExitCode = $probe.ExitCode
        shutdown = 'stdin-eof-normal-exit'
        timeoutSeconds = $TimeoutSeconds
        maximumResponseBytes = 16384
        maximumBytesPerDrain = 65536
        scratchAuthJsonFiles = $authFileCount
        rawSubprocessOutputPersisted = $false
        authConfigRequested = 'ephemeral'
        scope = 'unauthenticated-stdio-initialize-only'
        acceptanceCriteriaApproved = @()
    }
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-ValidatedDirectory -TargetPath $scratchRoot -ExpectedParent $scratchParent -ExpectedLeaf $scratchLeaf
    }
}
# No success file until the process and scratch have both been cleaned up.
$evidence['scratchRemoved'] = -not (Test-Path -LiteralPath $scratchRoot)
$json = $evidence | ConvertTo-Json -Depth 5
$stream = [IO.File]::Open($evidencePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream.Write($bytes, 0, $bytes.Length)
}
finally { $stream.Dispose() }
Write-Output "Handshake initialize recebido; initialized enviado; EOF e exit 0 confirmados. Evidência: $evidencePath"


