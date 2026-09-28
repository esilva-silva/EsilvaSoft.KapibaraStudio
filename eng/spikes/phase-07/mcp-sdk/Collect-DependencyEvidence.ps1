[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$packageRoot = Join-Path $PSScriptRoot '.cache/packages'
$serverLock = Get-Content (Join-Path $PSScriptRoot 'Server/packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$testLock = Get-Content (Join-Path $PSScriptRoot 'Tests/packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$serverPackages = $serverLock.dependencies['net10.0']
$packages = $testLock.dependencies['net10.0']
$entries = foreach ($id in ($packages.Keys | Sort-Object)) {
    $entry = $packages[$id]
    if ($entry.type -eq 'Project') { continue }
    $version = $entry.resolved
    $directory = Join-Path $packageRoot ($id.ToLowerInvariant() + '/' + $version)
    [xml]$nuspec = Get-Content (Join-Path $directory ($id.ToLowerInvariant() + '.nuspec')) -Raw
    $metadata = $nuspec.package.metadata
    $archive = Join-Path $directory ($id.ToLowerInvariant() + '.' + $version + '.nupkg')
    $packageMetadata = Get-Content (Join-Path $directory '.nupkg.metadata') -Raw | ConvertFrom-Json
    if ($packageMetadata.contentHash -ne $entry.contentHash) { throw "Hash de conteúdo NuGet divergente: $id $version" }
    $notices = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
        $_.Name -match '(?i)(license|notice|copying)' -and $_.Extension -ne '.dll'
    } | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($directory, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $nativeAssets = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
        if ($_.Extension -in '.so', '.dylib') { return $true }
        if ($_.Extension -notin '.dll', '.exe') { return $false }
        $stream = [IO.File]::OpenRead($_.FullName)
        $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try { return -not $reader.HasMetadata }
        finally { $reader.Dispose(); $stream.Dispose() }
    } | ForEach-Object { [IO.Path]::GetRelativePath($directory, $_.FullName).Replace('\', '/') })
    [ordered]@{
        id = $id; version = $version
        scope = $(if ($serverPackages.ContainsKey($id)) { 'spike-server' } else { 'test-only' })
        licenseType = [string]$metadata.license.type
        license = [string]$metadata.license.'#text'
        licenseUrl = [string]$metadata.licenseUrl
        source = "https://www.nuget.org/packages/$id/$version"
        repository = [string]$metadata.repository.url
        commit = [string]$metadata.repository.commit
        nugetContentHash = $entry.contentHash
        archiveSha512 = (Get-Content ($archive + '.sha512') -Raw).Trim()
        sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        notices = $notices
        nativeAssets = $nativeAssets
    }
}
$report = [ordered]@{
    schemaVersion = 1
    targetFramework = 'net10.0'
    source = 'NuGet packages restored with the spike lockfiles; nuspec, nupkg, embedded notices'
    distributionApproval = 'pending: isolated research only; not a product SBOM or legal clearance'
    packages = @($entries)
}
$report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $PSScriptRoot 'dependency-evidence.json') -Encoding utf8
Write-Output "Inventário gerado: $($entries.Count) pacotes; hashes conferidos com os locks."
