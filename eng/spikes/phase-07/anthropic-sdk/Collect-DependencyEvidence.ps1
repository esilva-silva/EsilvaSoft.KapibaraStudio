[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $PSScriptRoot '.cache/packages'
$lock = Get-Content (Join-Path $PSScriptRoot 'packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$assets = Get-Content (Join-Path $PSScriptRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$sdkVersion = $lock.dependencies['net10.0']['Anthropic'].resolved
$tag = "Anthropic-v$sdkVersion"
$release = Invoke-RestMethod "https://api.github.com/repos/anthropics/anthropic-sdk-csharp/releases/tags/$tag"
$tagRef = Invoke-RestMethod "https://api.github.com/repos/anthropics/anthropic-sdk-csharp/git/ref/tags/$tag"
if ($tagRef.object.type -ne 'commit') { throw 'Revisar tag anotada antes de gerar evidência.' }
$registration = Invoke-RestMethod "https://api.nuget.org/v3/registration5-gz-semver2/anthropic/$sdkVersion.json"
$versions = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/anthropic/index.json'
$stableVersions = @($versions.versions | Where-Object { $_ -notmatch '-' })
$licenses = Join-Path $PSScriptRoot 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$entries = foreach ($id in ($lock.dependencies['net10.0'].Keys | Sort-Object)) {
    $entry = $lock.dependencies['net10.0'][$id]
    $version = $entry.resolved
    $directory = Join-Path $packageRoot ($id.ToLowerInvariant() + '/' + $version)
    [xml]$nuspec = Get-Content (Join-Path $directory ($id.ToLowerInvariant() + '.nuspec')) -Raw
    $metadata = $nuspec.package.metadata
    $archive = Join-Path $directory ($id.ToLowerInvariant() + '.' + $version + '.nupkg')
    $packageMetadata = Get-Content (Join-Path $directory '.nupkg.metadata') -Raw | ConvertFrom-Json
    if ($packageMetadata.contentHash -ne $entry.contentHash) { throw "Hash NuGet divergente: $id" }
    $archiveHash = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($archive)))
    if ($archiveHash -ne (Get-Content ($archive + '.sha512') -Raw).Trim()) { throw "SHA512 do arquivo divergente: $id" }
    if ($id -eq 'Anthropic') {
        if ($metadata.repository.commit -ne $tagRef.object.sha) { throw 'Commit do pacote diferente da tag.' }
        $licenseUrl = "https://raw.githubusercontent.com/anthropics/anthropic-sdk-csharp/$tag/LICENSE"
    } else {
        $licenseUrl = "https://raw.githubusercontent.com/dotnet/extensions/$($metadata.repository.commit)/LICENSE"
    }
    $licenseFile = Join-Path $licenses "$id-$version-LICENSE.txt"
    Invoke-WebRequest $licenseUrl -OutFile $licenseFile
    $licenseText = Get-Content $licenseFile -Raw
    if ($licenseText -notmatch 'Permission is hereby granted, free of charge') { throw "Revisar licença de $id" }
    $sourceNotices = @()
    if ($id -eq 'Microsoft.Extensions.AI.Abstractions') {
        $noticeUrl = "https://raw.githubusercontent.com/dotnet/extensions/$($metadata.repository.commit)/THIRD-PARTY-NOTICES.TXT"
        $noticeFile = Join-Path $licenses "$id-$version-SOURCE-NOTICES.txt"
        Invoke-WebRequest $noticeUrl -OutFile $noticeFile
        $sourceNotices = @([ordered]@{
            source = $noticeUrl
            file = "licenses/$id-$version-SOURCE-NOTICES.txt"
            sha256 = (Get-FileHash $noticeFile -Algorithm SHA256).Hash.ToLowerInvariant()
            scope = 'Repository notices; not proof that every listed component is in this package'
        })
    }
    $selectedAssets = $assets.targets['net10.0']["$id/$version"]
    [ordered]@{
        id = $id; version = $version; scope = $entry.type
        authors = [string]$metadata.authors
        source = [string]$packageMetadata.source
        licenseType = [string]$metadata.license.type; license = [string]$metadata.license.'#text'
        repository = [string]$metadata.repository.url; commit = [string]$metadata.repository.commit
        nugetContentHash = $entry.contentHash; archiveSha512 = $archiveHash
        archiveSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        licenseSource = $licenseUrl
        licenseFile = "licenses/$id-$version-LICENSE.txt"
        licenseSha256 = (Get-FileHash $licenseFile -Algorithm SHA256).Hash.ToLowerInvariant()
        sourceNotices = $sourceNotices
        compileAssets = @($selectedAssets.compile.Keys | Sort-Object)
        nativeAssets = @(Get-ChildItem $directory -Recurse -File | Where-Object {
            $_.FullName -match '[/\\]native[/\\]' -or $_.Extension -in '.so', '.dylib'
        } | ForEach-Object { [IO.Path]::GetRelativePath($directory, $_.FullName).Replace('\', '/') })
    }
}
[ordered]@{
    schemaVersion = 1
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    targetFramework = 'net10.0'; selectedVersion = $sdkVersion; latestStableObserved = $stableVersions[-1]
    tag = $tag; tagCommit = $tagRef.object.sha
    releaseUrl = $release.html_url; releasePublishedAt = $release.published_at; prerelease = $release.prerelease
    nugetPublishedAt = $registration.published; nugetListed = $registration.listed
    scope = 'Isolated compile-only spike; not a product SBOM or provider certification'
    packages = @($entries)
} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $PSScriptRoot 'dependency-evidence.json') -Encoding utf8
Write-Output "Inventário gerado: $($entries.Count) pacotes; hash NuGet, arquivo e commit/tag conferidos."
