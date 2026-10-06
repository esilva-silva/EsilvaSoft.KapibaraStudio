<# Synthetic ELF header fixtures only; does not publish or execute the application or Copilot CLI. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh30-linux-elf-fixtures'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$total = 0
$failures = [Collections.Generic.List[string]]::new()

function Test-ExpectedElf([scriptblock]$Validate, [bool]$ShouldAccept, [string]$Label) {
    $script:total++
    $failure = $null
    try { & $Validate }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) { $failures.Add("$Label rejeitou cabeçalho completo: $($failure.Message)") }
    elseif (-not $ShouldAccept -and $null -eq $failure) { $failures.Add("$Label aceitou cabeçalho ELF inválido") }
    elseif (-not $ShouldAccept -and -not $failure.Message.Contains('ELF', [StringComparison]::Ordinal)) {
        $failures.Add("$Label falhou por motivo diferente do ELF: $($failure.Message)")
    }
}

function New-ElfFixtureArchive([string]$Source, [string]$Archive) {
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
    foreach ($rid in @('linux-x64', 'linux-arm64')) {
        foreach ($state in @('complete', 'truncated20', 'truncated63', 'wrongMachine')) {
            $payload = Join-Path $fixtureRoot "$rid-$state"
            $manifestDirectory = Join-Path $payload '_manifest/spdx_2.2'
            New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $payload
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $payload
            [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.spdx.json'),
                '{"spdxVersion":"SPDX-2.2","packages":[{"name":"synthetic"}]}')
            $header = [byte[]]::new(64)
            $header[0] = 127; $header[1] = 69; $header[2] = 76; $header[3] = 70
            $header[4] = 2; $header[5] = 1; $header[6] = 1
            $header[16] = 3; $header[20] = 1; $header[52] = 64
            $header[18] = if ($rid -eq 'linux-x64') { 62 } else { 183 }
            if ($state -eq 'wrongMachine') { $header[18] = if ($rid -eq 'linux-x64') { 183 } else { 62 } }
            if ($state -eq 'truncated20') { $header = $header[0..19] }
            if ($state -eq 'truncated63') { $header = $header[0..62] }
            [IO.File]::WriteAllBytes((Join-Path $payload 'EsilvaSoft.KapibaraStudio.Desktop'), $header)
            $accept = $state -eq 'complete'
            Test-ExpectedElf { Assert-KapibaraLinuxPayload $payload $rid } $accept "$rid/$state/payload"
            $archive = Join-Path $fixtureRoot "$rid-$state.tar.gz"
            New-ElfFixtureArchive $payload $archive
            Test-ExpectedElf { Assert-KapibaraLinuxArchive $archive $rid } $accept "$rid/$state/archive"
        }
    }
    if ($failures.Count -ne 0) { throw "$($failures.Count)/$total fixtures falharam: $($failures -join '; ')" }
    Write-Host "$total/$total fixtures passaram: cabeçalho ELF64 completo, truncamentos 20/63 e arquitetura nos dois RIDs Linux/payload/TAR."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpeza de fixtures recusada fora do diretório reservado.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
