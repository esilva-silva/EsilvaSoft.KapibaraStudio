[CmdletBinding()]
param(
    [string] $CodexPath,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
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

function Remove-ValidatedFile {
    param([string] $TargetPath, [string] $ExpectedParent, [string] $ExpectedLeaf)

    $targetFull = [IO.Path]::GetFullPath($TargetPath)
    $parentFull = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $actualParent = [IO.Path]::GetFullPath((Split-Path -Parent $targetFull))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($actualParent, $parentFull, $comparison) -or
        [IO.Path]::GetFileName($targetFull) -cne $ExpectedLeaf) {
        throw "Recusa de limpeza: arquivo fora do pai/nome esperado: $targetFull"
    }
    if (-not (Test-Path -LiteralPath $targetFull -PathType Leaf)) { return }
    $fileItem = Get-Item -LiteralPath $targetFull -Force
    if (($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Recusa de limpeza de reparse point: $targetFull"
    }
    Remove-Item -LiteralPath $targetFull -Force
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

function Invoke-CodexWithCleanEnvironment {
    param(
        [string] $Executable,
        [string[]] $Arguments,
        [string] $IsolatedCodexHome
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    # Do not inherit API keys, auth tokens, user Codex config, or unrelated environment.
    $startInfo.Environment.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'PATH', 'TEMP', 'TMP')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $startInfo.Environment[$name] = $value
        }
    }
    $startInfo.Environment['CODEX_HOME'] = $IsolatedCodexHome
    $startInfo.Environment['HOME'] = $IsolatedCodexHome
    if ($IsWindows) {
        $startInfo.Environment['USERPROFILE'] = $IsolatedCodexHome
        $startInfo.Environment['APPDATA'] = Join-Path $IsolatedCodexHome 'AppData/Roaming'
        $startInfo.Environment['LOCALAPPDATA'] = Join-Path $IsolatedCodexHome 'AppData/Local'
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Não foi possível iniciar codex.exe.'
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            # Do not copy stderr into the persistent evidence; it may contain local paths or diagnostics.
            throw "codex.exe terminou com código $($process.ExitCode) no comando solicitado."
        }
        return $stdout
    }
    finally {
        $process.Dispose()
    }
}

$codex = Resolve-CodexExecutable -RequestedPath $CodexPath
if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    throw 'OutputDirectory precisa ser um caminho absoluto.'
}
$outputResolved = Resolve-Path -LiteralPath $OutputDirectory -ErrorAction Stop
if ($outputResolved.Provider.Name -ne 'FileSystem' -or
    -not (Test-Path -LiteralPath $outputResolved.Path -PathType Container)) {
    throw 'OutputDirectory precisa ser um diretório local existente.'
}
$outputRoot = [IO.Path]::GetFullPath($outputResolved.Path)
$outputItem = Get-Item -LiteralPath $outputRoot -ErrorAction Stop
if (($IsWindows -and $outputRoot.StartsWith('\\')) -or
    ($outputItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'OutputDirectory precisa estar em um diretório local sem redirecionamento por link.'
}
if ($IsWindows) {
    $driveName = [IO.Path]::GetPathRoot($outputRoot).TrimEnd('\').TrimEnd(':')
    $drive = Get-PSDrive -Name $driveName -PSProvider FileSystem -ErrorAction SilentlyContinue
    if ($null -eq $drive -or -not [string]::IsNullOrWhiteSpace($drive.DisplayRoot)) {
        throw 'OutputDirectory precisa estar em um volume local existente, não em drive mapeado.'
    }
}
Assert-NoReparseAncestors -Path $outputRoot

$binaryHash = (Get-FileHash -LiteralPath $codex -Algorithm SHA256).Hash.ToLowerInvariant()
$scratchRoot = Join-Path ([IO.Path]::GetTempPath()) ('slop-phase07-codex-' + [guid]::NewGuid().ToString('N'))
$isolatedHome = Join-Path $scratchRoot 'codex-home'
$schemaDirectory = Join-Path $outputRoot ('codex-app-server-schema-' + $binaryHash.Substring(0, 12))
$manifestPath = Join-Path $outputRoot 'codex-app-server-schema-manifest.json'

if ((Test-Path -LiteralPath $schemaDirectory) -or (Test-Path -LiteralPath $manifestPath)) {
    throw "A saída já existe; escolha um diretório dedicado e vazio: $outputRoot"
}

$succeeded = $false
try {
    [void] (New-Item -ItemType Directory -Path $scratchRoot)
    Set-PrivateDirectoryPermissions -Path $scratchRoot
    [void] (New-Item -ItemType Directory -Path $isolatedHome)
    Set-PrivateDirectoryPermissions -Path $isolatedHome
    if ($IsWindows) {
        [void] (New-Item -ItemType Directory -Path (Join-Path $isolatedHome 'AppData/Roaming'))
        [void] (New-Item -ItemType Directory -Path (Join-Path $isolatedHome 'AppData/Local'))
    }
    $versionOutput = (Invoke-CodexWithCleanEnvironment -Executable $codex -Arguments @('--version') -IsolatedCodexHome $isolatedHome).Trim()
    if ([string]::IsNullOrWhiteSpace($versionOutput)) {
        throw 'codex.exe não retornou uma versão.'
    }

    [void] (New-Item -ItemType Directory -Path $schemaDirectory)
    [void] (Invoke-CodexWithCleanEnvironment -Executable $codex -Arguments @('app-server', 'generate-json-schema', '--out', $schemaDirectory) -IsolatedCodexHome $isolatedHome)

    $hashAfter = (Get-FileHash -LiteralPath $codex -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hashAfter -ne $binaryHash) {
        throw 'O hash de codex.exe mudou durante a coleta; evidência rejeitada.'
    }

    $schemaFiles = @(Get-ChildItem -LiteralPath $schemaDirectory -File -Recurse | Sort-Object FullName)
    if ($schemaFiles.Count -eq 0) {
        throw 'O comando concluiu sem gerar arquivos de schema; evidência rejeitada.'
    }

    $manifest = [ordered]@{
        collectedAtUtc = [DateTime]::UtcNow.ToString('O')
        executable = [IO.Path]::GetFileName($codex)
        executableSha256 = $binaryHash
        version = $versionOutput
        schemaCommand = 'app-server generate-json-schema'
        schemaFiles = @($schemaFiles | ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($schemaDirectory, $_.FullName)
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
        scope = 'availability-and-version-specific-schema-only'
        credentialsCaptured = $false
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    $verifiedManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($verifiedManifest.executableSha256 -ne $binaryHash -or
        $verifiedManifest.version -ne $versionOutput -or
        $verifiedManifest.credentialsCaptured -ne $false -or
        $verifiedManifest.schemaFiles.Count -ne $schemaFiles.Count) {
        throw 'Verificação do manifesto falhou; evidência rejeitada.'
    }
    $succeeded = $true

    Write-Output "Executável: $codex"
    Write-Output "Versão: $versionOutput"
    Write-Output "SHA-256: $binaryHash"
    Write-Output "Schema: $schemaDirectory"
    Write-Output "Manifesto: $manifestPath"
}
finally {
    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-ValidatedDirectory -TargetPath $scratchRoot -ExpectedParent ([IO.Path]::GetTempPath()) -ExpectedLeaf ([IO.Path]::GetFileName($scratchRoot))
    }
    if (-not $succeeded -and (Test-Path -LiteralPath $schemaDirectory)) {
        Remove-ValidatedDirectory -TargetPath $schemaDirectory -ExpectedParent $outputRoot -ExpectedLeaf ([IO.Path]::GetFileName($schemaDirectory))
    }
    if (-not $succeeded -and (Test-Path -LiteralPath $manifestPath)) {
        Remove-ValidatedFile -TargetPath $manifestPath -ExpectedParent $outputRoot -ExpectedLeaf ([IO.Path]::GetFileName($manifestPath))
    }
}
