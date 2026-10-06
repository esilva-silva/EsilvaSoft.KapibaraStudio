<# Synthetic ZIP fixtures; does not build, publish or execute the desktop or Copilot CLI. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh30-windows-zip-fixtures'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$total = 0
$failures = [Collections.Generic.List[string]]::new()

function Test-ExpectedZip([scriptblock]$Validate, [bool]$ShouldAccept, [string]$Label, [string]$ExpectedMessage) {
    $script:total++
    $failure = $null
    try { & $Validate | Out-Null }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) { $failures.Add("$Label rejeitou ZIP válido: $($failure.Message)") }
    elseif (-not $ShouldAccept -and $null -eq $failure) { $failures.Add("$Label aceitou ZIP inválido") }
    elseif (-not $ShouldAccept -and $ExpectedMessage -and -not $failure.Message.Contains($ExpectedMessage, [StringComparison]::Ordinal)) {
        $failures.Add("$Label falhou por outro motivo: $($failure.Message)")
    }
}

function Write-ZipFixtureEntry([IO.Compression.ZipArchive]$Zip, [string]$Name, [byte[]]$Bytes) {
    $entry = $Zip.CreateEntry($Name, [IO.Compression.CompressionLevel]::SmallestSize)
    $stream = $entry.Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) }
    finally { $stream.Dispose() }
}

function New-ZipFixture([string]$Source, [string]$Archive, [string]$State) {
    $zip = [IO.Compression.ZipFile]::Open($Archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File -Force) {
            $name = [IO.Path]::GetRelativePath($Source, $file.FullName).Replace('\', '/')
            if (($State -eq 'missingLicense' -and $name -eq 'LICENSE') -or
                ($State -eq 'missingNotice' -and $name -eq 'THIRD-PARTY-NOTICES.md') -or
                ($State -eq 'missingSbom' -and $name -eq '_manifest/spdx_2.2/manifest.spdx.json')) { continue }
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            if ($State -eq 'tampered' -and $name -eq 'EsilvaSoft.KapibaraStudio.Desktop.exe') {
                $bytes = [Text.Encoding]::UTF8.GetBytes('tampered! app') # Same length as synthetic app.
            }
            if ($State -eq 'tamperedNotice' -and $name -eq 'THIRD-PARTY-NOTICES.md') { $bytes[0] = $bytes[0] -bxor 1 }
            Write-ZipFixtureEntry $zip $name $bytes
        }
        $extra = switch ($State) {
            'duplicateExact' { 'LICENSE' }
            'duplicateCase' { 'license' }
            'traversal' { '../escaped.txt' }
            'absolute' { '/escaped.txt' }
            'backslash' { '..\escaped.txt' }
            'ads' { 'LICENSE:stream' }
            'alias' { './LICENSE' }
            'embeddedCli' { 'bin/COPILOT.EXE' }
            'unexpected' { 'extra.txt' }
            default { $null }
        }
        if ($null -ne $extra) { Write-ZipFixtureEntry $zip $extra ([Text.Encoding]::UTF8.GetBytes('synthetic extra')) }
    }
    finally { $zip.Dispose() }
    if ($State -eq 'truncated') {
        $bytes = [IO.File]::ReadAllBytes($Archive)
        [IO.File]::WriteAllBytes($Archive, $bytes[0..([int]($bytes.Length / 2))])
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    foreach ($rid in @('win-x64', 'win-arm64')) {
        $payload = Join-Path $fixtureRoot "$rid-payload"
        $manifestDirectory = Join-Path $payload '_manifest/spdx_2.2'
        New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $payload
        [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'),
            '{"spdxVersion":"SPDX-2.2","packages":[{"name":"synthetic"}]}')
        [IO.File]::WriteAllText((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop.exe'), 'synthetic app')
        $resourceDirectory = Join-Path $payload 'resources'
        New-Item -ItemType Directory -Path $resourceDirectory | Out-Null
        $hiddenPath = Join-Path $resourceDirectory 'hidden-resource.json'
        [IO.File]::WriteAllText($hiddenPath, '{"synthetic":"resource"}')
        [IO.File]::SetAttributes($hiddenPath, [IO.FileAttributes]::Hidden)
        & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $payload -Rid $rid
        $archive = Join-Path $fixtureRoot "$rid-hidden.zip"
        $packageOutput = @(New-KapibaraWindowsPackage $payload $archive $rid 6>&1)
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $total++
            if ($null -eq $zip.GetEntry('resources/hidden-resource.json')) { $failures.Add("$rid ZIP omitiu recurso Hidden aprovado no payload.") }
        }
        finally { $zip.Dispose() }
        $total++
        $reported = ($packageOutput | ForEach-Object { $_.ToString() } | Where-Object { $_ -match 'SHA256=([A-F0-9]{64})' } | Select-Object -Last 1)
        if (-not $reported -or $reported -notmatch "SHA256=$((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)") {
            $failures.Add("$rid checksum reportado não corresponde ao ZIP final.")
        }
        $states = [ordered]@{
            complete = $null
            missingLicense = 'ausente'
            missingNotice = 'ausente'
            missingSbom = 'ausente'
            duplicateExact = 'duplicada'
            duplicateCase = 'duplicada'
            traversal = 'Caminho inválido'
            absolute = 'Caminho inválido'
            backslash = 'Caminho inválido'
            ads = 'Caminho inválido'
            alias = 'Caminho inválido'
            embeddedCli = 'Copilot'
            unexpected = 'inesperado'
            tampered = 'SHA-256 divergente'
            tamperedNotice = 'SHA-256 divergente'
            truncated = $null
        }
        foreach ($state in $states.Keys) {
            $candidate = Join-Path $fixtureRoot "$rid-$state.zip"
            New-ZipFixture $payload $candidate $state
            Test-ExpectedZip { Assert-KapibaraWindowsArchive $candidate $payload $rid } ($state -eq 'complete') "$rid/$state" $states[$state]
        }
    }
    if ($failures.Count -ne 0) { throw "$($failures.Count)/$total fixtures falharam: $($failures -join '; ')" }
    Write-Host "$total/$total fixtures passaram: ZIP Hidden/checksum, traversal/aliases, duplicatas, ausências, CLI e bytes divergentes nos dois RIDs Windows."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpeza de fixtures recusada fora do diretório reservado.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
