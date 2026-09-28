[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'dependency-inventory.csv')
)

$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $PSScriptRoot '.cache/packages'
$lockFiles = @(
    (Join-Path $PSScriptRoot 'Client/packages.lock.json'),
    (Join-Path $PSScriptRoot 'Tests/packages.lock.json')
)

if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "Cache NuGet ausente: $packageRoot. Faça restore locked antes de gerar o inventário."
}

$packages = @{}
foreach ($lockPath in $lockFiles) {
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Lockfile ausente: $lockPath"
    }

    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $target = $lock.dependencies.'net10.0'
    foreach ($property in $target.PSObject.Properties) {
        $entry = $property.Value
        if ($entry.type -notin @('Direct', 'Transitive')) { continue }

        $key = "$($property.Name.ToLowerInvariant())/$($entry.resolved)"
        if (-not $packages.ContainsKey($key)) {
            $packages[$key] = [ordered]@{
                Id = $property.Name
                Version = $entry.resolved
                ReferenceKinds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                ContentHash = $entry.contentHash
            }
        }
        [void] $packages[$key].ReferenceKinds.Add($entry.type)
        if ($entry.contentHash -and $packages[$key].ContentHash -and $entry.contentHash -ne $packages[$key].ContentHash) {
            throw "Lockfiles disagree on package content hash: $key"
        }
    }
}

$inventory = foreach ($package in ($packages.Values | Sort-Object Id, Version)) {
    $packageDirectory = Join-Path (Join-Path $packageRoot $package.Id.ToLowerInvariant()) $package.Version
    $nupkg = Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.nupkg' -ErrorAction Stop | Select-Object -First 1
    if ($null -eq $nupkg) { throw "Nupkg ausente no cache para $($package.Id) $($package.Version)" }

    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.nuspec' -ErrorAction Stop | Select-Object -First 1
    if ($null -eq $nuspec) { throw "Nuspec ausente no cache para $($package.Id) $($package.Version)" }
    [xml] $nuspecXml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $license = $nuspecXml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $licenseValue = if ($null -eq $license) {
        'UNSPECIFIED'
    } elseif ($license.type -eq 'expression') {
        $license.InnerText.Trim()
    } elseif ($license.type -eq 'file') {
        "file:$($license.InnerText.Trim())"
    } else {
        'UNSPECIFIED'
    }
    $repository = $nuspecXml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='repository']")

    [pscustomobject]@{
        Id = $package.Id
        Version = $package.Version
        ReferenceKind = (@($package.ReferenceKinds | Sort-Object) -join '+')
        License = $licenseValue
        NuGetContentHash = $package.ContentHash
        PackageSha256 = (Get-FileHash -LiteralPath $nupkg.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Repository = if ($null -eq $repository) { '' } else { $repository.url }
        RepositoryCommit = if ($null -eq $repository) { '' } else { $repository.commit }
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputParent = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    throw "Diretório de saída inexistente: $outputParent"
}
$inventory | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding utf8
Write-Output "Pacotes inventariados: $($inventory.Count)"
Write-Output "Arquivo: $resolvedOutput"
