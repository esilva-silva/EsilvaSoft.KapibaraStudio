# Funções compartilhadas pelo empacotamento local e pelo workflow de release.
function Assert-KapibaraLinuxPayload([string]$SourceDir, [string]$Rid) {
    if ($Rid -notin @('linux-x64', 'linux-arm64')) { throw "RID Linux inválido: $Rid" }
    $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
    $required = @('EsilvaSoft.KapibaraStudio.Desktop')
    foreach ($relative in $required) {
        $path = Join-Path $SourceDir $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Payload Linux ausente: $relative" }
        $stream = [IO.File]::OpenRead($path)
        try {
            $header = [byte[]]::new(20)
            $count = $stream.Read($header, 0, $header.Length)
            if ($count -ne 20 -or $header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or
                $header[4] -ne 2 -or $header[5] -ne 1 -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                throw "Payload não é ELF 64-bit da arquitetura $Rid`: $relative"
            }
        }
        finally { $stream.Dispose() }
    }
    foreach ($relative in @("runtimes/$Rid/native/copilot", "runtimes/$Rid/native/copilot-runtime",
        "runtimes/$Rid/native/runtime.node", "runtimes/$Rid/native/libcopilot_runtime.so", "runtimes/$Rid/copilot-cli")) {
        if (Test-Path -LiteralPath (Join-Path $SourceDir $relative)) {
            throw "A CLI/runtime Copilot não deve ser redistribuída no payload Linux: $relative"
        }
    }
    $notices = Join-Path $SourceDir 'THIRD-PARTY-NOTICES.md'
    if (-not (Test-Path -LiteralPath $notices -PathType Leaf) -or (Get-Item -LiteralPath $notices).Length -eq 0) {
        throw 'Avisos de terceiros, incluindo a licença MIT do Copilot SDK, ausentes no payload Linux.'
    }
    $noticeText = Get-Content -LiteralPath $notices -Raw
    foreach ($requiredNotice in @('GitHub.Copilot.SDK', 'Copyright GitHub, Inc.', 'MIT License')) {
        if (-not $noticeText.Contains($requiredNotice, [StringComparison]::Ordinal)) {
            throw "Aviso MIT do Copilot SDK incompleto no payload Linux: $requiredNotice"
        }
    }
}

function Test-KapibaraLinuxExecutable([string]$Name, [string]$Rid) {
    if ($Name -in @('EsilvaSoft.KapibaraStudio.Desktop', 'createdump')) { return $true }
    return $false
}

# TarWriter define permissões Unix mesmo quando a publicação foi feita no Windows.
function New-KapibaraLinuxPackage([string]$SourceDir, [string]$Destination, [string]$Rid) {
    $SourceDir = [IO.Path]::GetFullPath($SourceDir)
    Assert-KapibaraLinuxPayload $SourceDir $Rid
    $fileMode = [IO.UnixFileMode][Convert]::ToInt32('644', 8)
    $execMode = [IO.UnixFileMode][Convert]::ToInt32('755', 8)
    $output = [IO.File]::Create($Destination)
    $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::SmallestSize)
    $writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Pax, $false)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force | Sort-Object FullName) {
            $name = [IO.Path]::GetRelativePath($SourceDir, $file.FullName).Replace('\', '/')
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $name)
            $isExecutable = Test-KapibaraLinuxExecutable $name $Rid
            $entry.Mode = if ($isExecutable) { $execMode } else { $fileMode }
            $entry.ModificationTime = [DateTimeOffset]$file.LastWriteTimeUtc
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
        $output.Dispose()
    }
    Assert-KapibaraLinuxArchive $Destination $Rid
}

function Assert-KapibaraLinuxArchive([string]$Archive, [string]$Rid) {
    if ($Rid -notin @('linux-x64', 'linux-arm64')) { throw "RID Linux inválido: $Rid" }
    $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
    $requiredModes = @{
        'EsilvaSoft.KapibaraStudio.Desktop' = 493 # 0755
        'THIRD-PARTY-NOTICES.md' = 420 # 0644
    }
    $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputStream = [IO.File]::OpenRead($Archive)
    $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [Formats.Tar.TarReader]::new($gzip, $false)
    try {
        while ($null -ne ($entry = $reader.GetNextEntry())) {
            if ($entry.Name.StartsWith('mcp/', [StringComparison]::Ordinal)) {
                throw "Integração Claude Code não deve estar presente no pacote Release: $($entry.Name)"
            }
            if ($entry.Name.StartsWith("runtimes/$Rid/native/copilot", [StringComparison]::Ordinal) -or
                $entry.Name -eq "runtimes/$Rid/native/runtime.node" -or $entry.Name.StartsWith("runtimes/$Rid/copilot-cli", [StringComparison]::Ordinal)) {
                throw "A CLI/runtime Copilot não deve ser redistribuída no pacote Release: $($entry.Name)"
            }
            if ((Test-KapibaraLinuxExecutable $entry.Name $Rid) -and [int]$entry.Mode -ne 493) {
                throw "Permissão de execução inválida no pacote Linux: $($entry.Name)"
            }
            if (-not $requiredModes.ContainsKey($entry.Name)) { continue }
            if (-not $found.Add($entry.Name)) { throw "Entrada duplicada no pacote Linux: $($entry.Name)" }
            if ([int]$entry.Mode -ne $requiredModes[$entry.Name] -or $entry.Length -lt 1) {
                throw "Permissão ou conteúdo inválido no pacote Linux: $($entry.Name)"
            }
            if ($entry.Name -eq 'EsilvaSoft.KapibaraStudio.Desktop') {
                $header = [byte[]]::new(20)
                $count = $entry.DataStream.Read($header, 0, $header.Length)
                if ($count -ne 20 -or $header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or
                    $header[4] -ne 2 -or $header[5] -ne 1 -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                    throw "Desktop ELF no pacote não corresponde à arquitetura $Rid."
                }
            }
            if ($entry.Name -eq 'THIRD-PARTY-NOTICES.md') {
                $readerText = [IO.StreamReader]::new($entry.DataStream, [Text.Encoding]::UTF8, $true, 1024, $true)
                try { $noticeText = $readerText.ReadToEnd() }
                finally { $readerText.Dispose() }
                foreach ($requiredNotice in @('GitHub.Copilot.SDK', 'Copyright GitHub, Inc.', 'MIT License')) {
                    if (-not $noticeText.Contains($requiredNotice, [StringComparison]::Ordinal)) {
                        throw "Aviso MIT do Copilot SDK incompleto no TAR Linux: $requiredNotice"
                    }
                }
            }
        }
        foreach ($name in $requiredModes.Keys) {
            if (-not $found.Contains($name)) { throw "Entrada ausente no pacote Linux: $name" }
        }
    }
    finally {
        $reader.Dispose()
        $gzip.Dispose()
        $inputStream.Dispose()
    }
}
