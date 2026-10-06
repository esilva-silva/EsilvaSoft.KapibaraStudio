<# Fixtures sintéticas do validador SPDX; não executam app/CLI nem substituem inventário ou revisão legal. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh27-sbom-fixtures'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$total = 0
$failures = [Collections.Generic.List[string]]::new()

function Test-ExpectedSbom([scriptblock]$Validate, [bool]$ShouldAccept, [string]$Label) {
    $script:total++
    $failure = $null
    try { & $Validate }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) {
        $failures.Add("$Label rejeitou SBOM SPDX válido: $($failure.Message)")
    }
    elseif (-not $ShouldAccept -and $null -eq $failure) {
        $failures.Add("$Label aceitou SBOM inválido ou ausente")
    }
    elseif (-not $ShouldAccept -and
        -not $failure.Message.Contains('SBOM', [StringComparison]::Ordinal) -and
        -not $failure.Message.Contains('manifest.spdx.json', [StringComparison]::Ordinal)) {
        $failures.Add("$Label falhou por motivo diferente do SBOM: $($failure.Message)")
    }
}

function New-SbomFixtureArchive([string]$Source, [string]$Archive) {
    $stream = [IO.File]::Create($Archive)
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::Fastest)
    $writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Pax, $true)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $name = [IO.Path]::GetRelativePath($Source, $file.FullName).Replace('\', '/')
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $name)
            $entry.Mode = [IO.UnixFileMode]$(if ($name -eq 'EsilvaSoft.KapibaraStudio.Desktop') { 493 } else { 420 })
            $content = $file.OpenRead()
            try { $entry.DataStream = $content; $writer.WriteEntry($entry) }
            finally { $content.Dispose() }
        }
    }
    finally { $writer.Dispose(); $gzip.Dispose(); $stream.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    $states = [ordered]@{
        valid = '{"spdxVersion":"SPDX-2.2","packages":[{"name":"synthetic"}]}'
        missing = $null
        empty = ''
        malformed = '{not-json'
        wrongVersion = '{"spdxVersion":"SPDX-3.0","packages":[{"name":"synthetic"}]}'
        emptyPackages = '{"spdxVersion":"SPDX-2.2","packages":[]}'
        packagesObject = '{"spdxVersion":"SPDX-2.2","packages":{"name":"synthetic"}}'
        packagesString = '{"spdxVersion":"SPDX-2.2","packages":"synthetic"}'
        packageString = '{"spdxVersion":"SPDX-2.2","packages":["synthetic"]}'
        packageMissingName = '{"spdxVersion":"SPDX-2.2","packages":[{}]}'
    }
    $notice = Get-Content -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Raw
    foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')) {
        foreach ($state in $states.Keys) {
            $payload = Join-Path $fixtureRoot "$rid-$state"
            New-Item -ItemType Directory -Path $payload | Out-Null
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload
            [IO.File]::WriteAllText((Join-Path $payload 'THIRD-PARTY-NOTICES.md'), $notice)
            if ($rid.StartsWith('win-')) {
                [IO.File]::WriteAllText((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop.exe'), 'synthetic app')
            }
            else {
                $header = [byte[]]::new(64)
                $header[0] = 127; $header[1] = 69; $header[2] = 76; $header[3] = 70
                $header[4] = 2; $header[5] = 1; $header[6] = 1
                $header[18] = if ($rid -eq 'linux-x64') { 62 } else { 183 }
                [IO.File]::WriteAllBytes((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop'), $header)
            }

            if ($null -ne $states[$state]) {
                $manifestDirectory = Join-Path $payload '_manifest/spdx_2.2'
                New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
                [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'), $states[$state])
            }
            $shouldAccept = $state -eq 'valid'
            Test-ExpectedSbom {
                & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $payload -Rid $rid
            } $shouldAccept "$rid/$state/package"

            if ($rid.StartsWith('linux-')) {
                Test-ExpectedSbom { Assert-KapibaraLinuxPayload $payload $rid } $shouldAccept "$rid/$state/payload"
                $archive = Join-Path $fixtureRoot "$rid-$state.tar.gz"
                New-SbomFixtureArchive $payload $archive
                Test-ExpectedSbom { Assert-KapibaraLinuxArchive $archive $rid } $shouldAccept "$rid/$state/archive"
            }
        }
    }

    if ($failures.Count -ne 0) { throw "$($failures.Count)/$total fixtures falharam: $($failures -join '; ')" }
    Write-Host "$total/$total fixtures passaram: SBOM ausente/vazio/malformado/versão ou lista inválidas recusados nos quatro RIDs, payload e TAR Linux."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Limpeza de fixtures SBOM recusada fora do diretório reservado.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
