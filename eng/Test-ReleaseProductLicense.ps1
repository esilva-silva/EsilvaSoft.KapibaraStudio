<# Fixtures estruturais: não publica nem executa aplicativo/CLI e não aprova licenças de terceiros. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh27-product-license'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$results = [Collections.Generic.List[bool]]::new()
$failures = [Collections.Generic.List[string]]::new()

function Test-ExpectedLicenseResult([scriptblock]$Validate, [bool]$ShouldAccept, [string]$Label) {
    $failure = $null
    try { & $Validate }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) {
        $failures.Add("$Label rejeitou o controle válido: $($failure.Message)")
        return $false
    }
    if (-not $ShouldAccept -and $null -eq $failure) {
        $failures.Add("$Label aceitou LICENSE ausente/vazio")
        return $false
    }
    if (-not $ShouldAccept -and -not $failure.Message.Contains('LICENSE', [StringComparison]::Ordinal)) {
        $failures.Add("$Label falhou por motivo diferente da licença do produto: $($failure.Message)")
        return $false
    }
    return $true
}

function New-SyntheticLinuxArchive([string]$Source, [string]$Archive) {
    $stream = [IO.File]::Create($Archive)
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::Fastest)
    $writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Pax, $true)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $name = [IO.Path]::GetRelativePath($Source, $file.FullName).Replace('\', '/')
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $name)
            $entry.Mode = [IO.UnixFileMode]$(if ($name -eq 'EsilvaSoft.KapibaraStudio.Desktop') { 493 } else { 420 })
            $content = $file.OpenRead()
            try {
                $entry.DataStream = $content
                $writer.WriteEntry($entry)
            }
            finally { $content.Dispose() }
        }
    }
    finally {
        $writer.Dispose()
        $gzip.Dispose()
        $stream.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')) {
        foreach ($licenseState in @('present', 'missing', 'empty', 'altered')) {
            $payload = Join-Path $fixtureRoot "$rid-$licenseState"
            New-Item -ItemType Directory -Path $payload | Out-Null
            $manifestDirectory = Join-Path $payload '_manifest/spdx_2.2'
            New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'), '{"spdxVersion":"SPDX-2.2","packages":[{"name":"synthetic"}]}')
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $payload
            if ($licenseState -eq 'present') {
                Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload
            }
            elseif ($licenseState -eq 'empty') {
                [IO.File]::WriteAllBytes((Join-Path $payload 'LICENSE'), [byte[]]::new(0))
            }
            elseif ($licenseState -eq 'altered') {
                [IO.File]::WriteAllText((Join-Path $payload 'LICENSE'), 'A nonempty license file that is not the product MIT license.')
            }
            if ($rid.StartsWith('win-')) {
                # Nonempty placeholder: this validator checks packaging, not executable loading.
                [IO.File]::WriteAllText((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop.exe'), 'synthetic app')
            }
            else {
                $header = [byte[]]::new(64)
                $header[0] = 127; $header[1] = 69; $header[2] = 76; $header[3] = 70
                $header[4] = 2; $header[5] = 1; $header[6] = 1
                $header[18] = if ($rid -eq 'linux-x64') { 62 } else { 183 }
                [IO.File]::WriteAllBytes((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop'), $header)
            }
            $shouldAccept = $licenseState -eq 'present'
            $results.Add((Test-ExpectedLicenseResult {
                & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $payload -Rid $rid
            } $shouldAccept "$rid/$licenseState/package"))
            if ($rid.StartsWith('linux-')) {
                $results.Add((Test-ExpectedLicenseResult {
                    Assert-KapibaraLinuxPayload $payload $rid
                } $shouldAccept "$rid/$licenseState/payload"))
                $archive = Join-Path $fixtureRoot "$rid-$licenseState.tar.gz"
                New-SyntheticLinuxArchive $payload $archive
                $results.Add((Test-ExpectedLicenseResult {
                    Assert-KapibaraLinuxArchive $archive $rid
                } $shouldAccept "$rid/$licenseState/archive"))
            }
        }
    }
    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/EsilvaSoft.KapibaraStudio.Desktop/EsilvaSoft.KapibaraStudio.Desktop.csproj') -Raw
    $content = $project.SelectNodes("//Content[@Include='../../LICENSE']")
    $copiesLicense = $content.Count -eq 1 -and $content[0].GetAttribute('Link') -eq 'LICENSE' -and
        $content[0].GetAttribute('CopyToPublishDirectory') -eq 'Always' -and
        $content[0].GetAttribute('ExcludeFromSingleFile') -eq 'true'
    $results.Add($copiesLicense)
    if (-not $copiesLicense) { $failures.Add('Desktop não declara LICENSE externo no publish.') }
    $failed = @($results | Where-Object { -not $_ }).Count
    if ($failed -ne 0) { throw "$failed/$($results.Count) fixtures falharam: $($failures -join '; ')" }
    Write-Host "$($results.Count)/$($results.Count) fixtures aprovadas: conteúdo MIT presente, ausente, vazio ou alterado nos quatro RIDs/payload/TAR Linux e copy no projeto."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Limpeza de fixtures recusada fora do diretório reservado.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
