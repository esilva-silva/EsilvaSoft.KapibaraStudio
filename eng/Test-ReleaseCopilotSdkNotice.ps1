<# Fixtures somente sintéticas; não executa nem publica app/CLI e não aprova licenças. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh27-sdk-notice'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$failures = [Collections.Generic.List[string]]::new()
$total = 0
$notices = Get-Content -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Raw

function Test-ExpectedNotice([scriptblock]$Validate, [bool]$ShouldAccept, [string]$Label) {
    $script:total++
    $failure = $null
    try { & $Validate }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) { $failures.Add("$Label rejeitou aviso integral: $($failure.Message)") }
    elseif (-not $ShouldAccept -and $null -eq $failure) { $failures.Add("$Label aceitou aviso MIT truncado") }
    elseif (-not $ShouldAccept -and -not $failure.Message.Contains('MIT', [StringComparison]::Ordinal)) {
        $failures.Add("$Label falhou por motivo diferente de MIT: $($failure.Message)")
    }
}

function New-NoticeFixtureArchive([string]$Source, [string]$Archive) {
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
        full = $notices
        markers = "GitHub.Copilot.SDK`nCopyright GitHub, Inc.`nMIT License"
        'missing-grant' = [regex]::Replace($notices, '(?s)Permission is hereby granted,.*?following conditions:', '')
        'missing-condition' = [regex]::Replace($notices, '(?s)The above copyright notice.*?portions of the Software\.', '')
        'missing-disclaimer' = [regex]::Replace($notices, '(?s)THE SOFTWARE IS PROVIDED.*?SOFTWARE\.', '')
        reflow = $notices.Replace('free of charge,', "free   of`ncharge,").Replace("`r`n", "`n")
    }
    foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')) {
        foreach ($state in $states.Keys) {
            $payload = Join-Path $fixtureRoot "$rid-$state"
            New-Item -ItemType Directory -Path $payload | Out-Null
            $manifestDirectory = Join-Path $payload '_manifest/spdx_2.2'
            New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'), '{"spdxVersion":"SPDX-2.2","packages":[{"name":"synthetic"}]}')
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload
            [IO.File]::WriteAllText((Join-Path $payload 'THIRD-PARTY-NOTICES.md'), $states[$state])
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
            $accept = $state -in @('full', 'reflow')
            Test-ExpectedNotice {
                & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $payload -Rid $rid
            } $accept "$rid/$state/package"
            if ($rid.StartsWith('linux-')) {
                Test-ExpectedNotice { Assert-KapibaraLinuxPayload $payload $rid } $accept "$rid/$state/payload"
                $archive = Join-Path $fixtureRoot "$rid-$state.tar.gz"
                New-NoticeFixtureArchive $payload $archive
                Test-ExpectedNotice { Assert-KapibaraLinuxArchive $archive $rid } $accept "$rid/$state/archive"
            }
        }
    }
    if ($failures.Count -ne 0) { throw "$($failures.Count)/$total fixtures falharam: $($failures -join '; ')" }
    Write-Host "$total/$total fixtures aprovadas: MIT integral do SDK, truncamentos e whitespace nos quatro RIDs/payload/TAR."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Limpeza de fixtures recusada fora do diretório reservado.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
