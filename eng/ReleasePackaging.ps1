# Funções compartilhadas pelo empacotamento local e pelo workflow de release.
function Assert-KapibaraLinuxPayload([string]$SourceDir, [string]$Rid) {
    if ($Rid -notin @('linux-x64', 'linux-arm64')) { throw "RID Linux inválido: $Rid" }
    $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
    $required = @(
        'EsilvaSoft.KapibaraStudio.Desktop',
        'mcp/EsilvaSoft.KapibaraStudio.McpServer',
        "runtimes/$Rid/native/copilot",
        "runtimes/$Rid/native/copilot-runtime",
        "runtimes/$Rid/copilot-cli/copilot",
        "runtimes/$Rid/native/runtime.node",
        "runtimes/$Rid/native/libcopilot_runtime.so",
        "runtimes/$Rid/native/ripgrep/bin/$Rid/rg",
        "runtimes/$Rid/native/tgrep/bin/$Rid/tgrep"
    )
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
    if (-not (Test-Path -LiteralPath (Join-Path $SourceDir "runtimes/$Rid/native/LICENSE.md") -PathType Leaf)) {
        throw 'Licença original do runtime Copilot ausente no payload Linux.'
    }
}

function Test-KapibaraLinuxExecutable([string]$Name, [string]$Rid) {
    if ($Name -in @('EsilvaSoft.KapibaraStudio.Desktop', 'mcp/EsilvaSoft.KapibaraStudio.McpServer', 'createdump',
        "runtimes/$Rid/native/copilot", "runtimes/$Rid/native/copilot-runtime", "runtimes/$Rid/copilot-cli/copilot")) { return $true }
    # O archive oficial inclui helpers das duas arquiteturas; manter seus modos de execução originais.
    foreach ($helperRid in @('linux-x64', 'linux-arm64')) {
        if ($Name -in @("runtimes/$Rid/native/ripgrep/bin/$helperRid/rg", "runtimes/$Rid/native/tgrep/bin/$helperRid/tgrep")) {
            return $true
        }
    }
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
    $requiredModes = @{
        'EsilvaSoft.KapibaraStudio.Desktop' = 493 # 0755
        'mcp/EsilvaSoft.KapibaraStudio.McpServer' = 493
        "runtimes/$Rid/native/copilot" = 493
        "runtimes/$Rid/native/copilot-runtime" = 493
        "runtimes/$Rid/copilot-cli/copilot" = 493
        "runtimes/$Rid/native/runtime.node" = 420 # 0644
        "runtimes/$Rid/native/libcopilot_runtime.so" = 420
        "runtimes/$Rid/native/ripgrep/bin/$Rid/rg" = 493
        "runtimes/$Rid/native/tgrep/bin/$Rid/tgrep" = 493
        "runtimes/$Rid/native/LICENSE.md" = 420
    }
    $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
    $elfEntries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $requiredModes.Keys) {
        if ($name -ne "runtimes/$Rid/native/LICENSE.md") { [void]$elfEntries.Add($name) }
    }
    $inputStream = [IO.File]::OpenRead($Archive)
    $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [Formats.Tar.TarReader]::new($gzip, $false)
    try {
        while ($null -ne ($entry = $reader.GetNextEntry())) {
            if ((Test-KapibaraLinuxExecutable $entry.Name $Rid) -and [int]$entry.Mode -ne 493) {
                throw "Permissão de execução inválida no pacote Linux: $($entry.Name)"
            }
            if (-not $requiredModes.ContainsKey($entry.Name)) { continue }
            if (-not $found.Add($entry.Name)) { throw "Entrada duplicada no pacote Linux: $($entry.Name)" }
            if ([int]$entry.Mode -ne $requiredModes[$entry.Name] -or $entry.Length -lt 20) {
                throw "Permissão ou conteúdo inválido no pacote Linux: $($entry.Name)"
            }
            if ($elfEntries.Contains($entry.Name)) {
                $header = [byte[]]::new(20)
                $count = $entry.DataStream.Read($header, 0, $header.Length)
                if ($count -ne 20 -or $header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or
                    $header[4] -ne 2 -or $header[5] -ne 1 -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                    throw "ELF no pacote não corresponde à arquitetura $Rid`: $($entry.Name)"
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
