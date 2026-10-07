# Funções compartilhadas pelo empacotamento local e pelo workflow de release.
function Assert-KapibaraCopilotSdkNotice([string]$NoticeText) {
    # Structural preservation of the SDK notice already versioned in the repository; not legal clearance.
    $expected = @'
MIT License

Copyright GitHub, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@
    # The preserved text must also identify the centrally pinned SDK version. This is an identity check,
    # not a claim that the package's transitive notices or the installed CLI license are complete.
    [xml]$packageVersions = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Packages.props') -Raw
    $sdkPins = @($packageVersions.SelectNodes('//PackageVersion[@Include="GitHub.Copilot.SDK"]'))
    if ($sdkPins.Count -ne 1 -or [string]::IsNullOrWhiteSpace($sdkPins[0].GetAttribute('Version'))) {
        throw 'Pin único de versão do GitHub Copilot SDK ausente para conferir o aviso MIT.'
    }
    $sdkVersion = $sdkPins[0].GetAttribute('Version')
    $sdkBlock = [regex]::Match($NoticeText, '(?ms)^### GitHub Copilot SDK (?<version>[^\s]+)[^\r\n]*\r?\n.*?^```text\r?\n(?<license>.*?)^```')
    $actual = [regex]::Replace($sdkBlock.Groups['license'].Value, '\s+', ' ').Trim()
    $canonical = [regex]::Replace($expected, '\s+', ' ').Trim()
    if (-not $NoticeText.Contains('GitHub.Copilot.SDK', [StringComparison]::Ordinal) -or
        -not $sdkBlock.Success -or $sdkBlock.Groups['version'].Value -cne $sdkVersion -or
        -not $actual.Equals($canonical, [StringComparison]::Ordinal)) {
        throw 'Aviso MIT integral do GitHub Copilot SDK ausente, alterado ou com versão divergente do pin em THIRD-PARTY-NOTICES.md.'
    }
}

function Assert-KapibaraProductLicense([string]$LicenseText) {
    # Presence alone is insufficient: reject a nonempty replacement that changes the product's declared MIT license.
    $expected = @'
MIT License

Copyright (c) 2026 EsilvaSoft

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@
    $actual = [regex]::Replace($LicenseText, '\s+', ' ').Trim()
    $canonical = [regex]::Replace($expected, '\s+', ' ').Trim()
    if (-not $actual.Equals($canonical, [StringComparison]::Ordinal)) {
        throw 'LICENSE MIT do produto ausente ou alterada no payload do release.'
    }
}

function Assert-KapibaraReleaseSbomContent([string]$Json, [string]$PackageName, [string]$PackageVersion) {
    # A small structural/identity guard; full SPDX schema and file hashes are validated by sbom-tool.
    try { $sbom = $Json | ConvertFrom-Json -AsHashtable -NoEnumerate }
    catch { throw 'JSON do SBOM SPDX inválido no payload do release.' }
    if ($sbom -isnot [Collections.IDictionary] -or $sbom['spdxVersion'] -cne 'SPDX-2.2' -or
        $sbom['packages'] -isnot [array] -or $sbom['packages'].Count -lt 1) {
        throw 'SBOM SPDX 2.2 inválido ou sem pacotes no payload do release.'
    }
    foreach ($package in $sbom['packages']) {
        if ($package -isnot [Collections.IDictionary] -or $package['name'] -isnot [string] -or
            [string]::IsNullOrWhiteSpace($package['name'])) {
            throw 'Entrada de pacote inválida no SBOM SPDX 2.2 do release.'
        }
    }
    if (-not [string]::IsNullOrEmpty($PackageName)) {
        $rootPackages = @($sbom['packages'] | Where-Object { $_['name'] -ceq $PackageName })
        if ($rootPackages.Count -ne 1 -or $rootPackages[0]['versionInfo'] -cne $PackageVersion) {
            throw "SBOM sem pacote raiz único '$PackageName' na versão '$PackageVersion'."
        }
    }
}

function Assert-KapibaraReleaseSbom([string]$Root) {
    $sbomPath = Join-Path $Root '_manifest/spdx_2.2/manifest.spdx.json'
    if (-not (Test-Path -LiteralPath $sbomPath -PathType Leaf) -or (Get-Item -LiteralPath $sbomPath).Length -eq 0) {
        throw 'SBOM SPDX 2.2 ausente ou vazio no payload do release.'
    }
    Assert-KapibaraReleaseSbomContent (Get-Content -LiteralPath $sbomPath -Raw)
}

function Assert-KapibaraWindowsZipEntryName([string]$Name) {
    $path = $Name.TrimEnd('/')
    $segments = $path.Split('/')
    if ([string]::IsNullOrEmpty($path) -or $Name.StartsWith('/') -or $Name.Contains('\') -or
        $Name.IndexOfAny([char[]]'<>:"|?*') -ge 0 -or ($Name.ToCharArray() | Where-Object { [char]::IsControl($_) }) -or
        ($segments | Where-Object { $_ -in @('', '.', '..') -or $_.EndsWith('.') -or $_.EndsWith(' ') -or
            $_ -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' })) {
        throw "Caminho inválido no ZIP Windows: $Name"
    }
}

function Get-KapibaraWindowsPayloadFiles([string]$SourceDir) {
    $root = Get-Item -LiteralPath $SourceDir -Force
    if ($root.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Raiz com link/reparse point não permitida no ZIP Windows.' }
    $items = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Force)
    if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
        throw 'Links/reparse points não permitidos no payload do ZIP Windows.'
    }
    return $items | Where-Object { -not $_.PSIsContainer }
}

function Assert-KapibaraWindowsArchive([string]$Archive, [string]$SourceDir, [string]$Rid) {
    if ($Rid -notin @('win-x64', 'win-arm64')) { throw "RID Windows inválido: $Rid" }
    $SourceDir = [IO.Path]::GetFullPath($SourceDir)
    & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $SourceDir -Rid $Rid
    $expected = [Collections.Generic.Dictionary[string, IO.FileInfo]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-KapibaraWindowsPayloadFiles $SourceDir) {
        $name = [IO.Path]::GetRelativePath($SourceDir, $file.FullName).Replace('\', '/')
        Assert-KapibaraWindowsZipEntryName $name
        if (-not $expected.TryAdd($name, $file)) { throw "Nome duplicado no payload Windows: $name" }
    }
    $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            Assert-KapibaraWindowsZipEntryName $name
            if (-not $found.Add($name.TrimEnd('/'))) { throw "Entrada duplicada no ZIP Windows: $name" }
            if (($entry.ExternalAttributes -shr 16 -band 61440) -eq 40960) { throw "Link não permitido no ZIP Windows: $name" }
            $leaf = $name.TrimEnd('/').Split('/')[-1]
            if ($leaf -in @('copilot.exe', 'copilot', 'copilot-runtime.exe', 'copilot-runtime', 'runtime.node',
                    'libcopilot_runtime.so', 'copilot_runtime.dll', 'copilot-cli')) {
                throw "CLI/runtime Copilot não deve ser redistribuída no ZIP Windows: $name"
            }
            if ($name -eq 'mcp/' -or $name.StartsWith('mcp/', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Proxy MCP não permitido no ZIP Windows: $name"
            }
            if ($name.EndsWith('/')) {
                if (-not (Test-Path -LiteralPath (Join-Path $SourceDir $name) -PathType Container)) {
                    throw "Diretório inesperado no ZIP Windows: $name"
                }
                continue
            }
            if (-not $expected.ContainsKey($name)) { throw "Arquivo inesperado no ZIP Windows: $name" }
            $sourceFile = $expected[$name]
            if ($entry.Length -ne $sourceFile.Length) { throw "Tamanho divergente no ZIP Windows: $name" }
            $stream = $entry.Open()
            try { $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $expectedHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
            if ($actualHash -cne $expectedHash) { throw "SHA-256 divergente no ZIP Windows: $name" }
        }
        foreach ($name in $expected.Keys) {
            if (-not $found.Contains($name)) { throw "Arquivo ausente no ZIP Windows: $name" }
        }
    }
    finally { $zip.Dispose() }
}

function New-KapibaraWindowsPackage([string]$SourceDir, [string]$Destination, [string]$Rid) {
    if ($Rid -notin @('win-x64', 'win-arm64')) { throw "RID Windows inválido: $Rid" }
    $SourceDir = [IO.Path]::GetFullPath($SourceDir)
    $Destination = [IO.Path]::GetFullPath($Destination)
    $sourcePrefix = $SourceDir.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($Destination.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Destino ZIP deve ficar fora do payload de origem.'
    }
    & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PublishDirectory $SourceDir -Rid $Rid
    Get-KapibaraWindowsPayloadFiles $SourceDir | Out-Null
    # The .NET API preserves Hidden files; Compress-Archive silently excludes them.
    [IO.Compression.ZipFile]::CreateFromDirectory($SourceDir, $Destination, [IO.Compression.CompressionLevel]::SmallestSize, $false)
    Assert-KapibaraWindowsArchive $Destination $SourceDir $Rid
    Write-Host "ZIP $Rid conferido contra o payload; SHA256=$((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash)"
}

function Read-KapibaraLinuxElfHeader([IO.Stream]$Stream) {
    # Both Linux RIDs require ELF64's complete 64-byte header. Archive streams may return partial reads.
    $header = [byte[]]::new(64)
    $count = 0
    while ($count -lt $header.Length) {
        $read = $Stream.Read($header, $count, $header.Length - $count)
        if ($read -eq 0) { break }
        $count += $read
    }
    if ($count -ne $header.Length) { throw 'Cabeçalho ELF64 truncado no payload/pacote Linux.' }
    return ,$header
}

function Assert-KapibaraLinuxPayload([string]$SourceDir, [string]$Rid) {
    if ($Rid -notin @('linux-x64', 'linux-arm64')) { throw "RID Linux inválido: $Rid" }
    $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
    $required = @('EsilvaSoft.KapibaraStudio.Desktop')
    foreach ($relative in $required) {
        $path = Join-Path $SourceDir $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Payload Linux ausente: $relative" }
        $stream = [IO.File]::OpenRead($path)
        try {
            $header = Read-KapibaraLinuxElfHeader $stream
            if ($header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or
                $header[4] -ne 2 -or $header[5] -ne 1 -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                throw "Payload não é ELF 64-bit da arquitetura $Rid`: $relative"
            }
        }
        finally { $stream.Dispose() }
    }
    $forbiddenNames = @('copilot.exe', 'copilot', 'copilot-runtime.exe', 'copilot-runtime', 'runtime.node',
        'libcopilot_runtime.so', 'copilot_runtime.dll', 'copilot-cli')
    foreach ($file in Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force) {
        if ($file.Name -in $forbiddenNames) {
            $relative = [IO.Path]::GetRelativePath($SourceDir, $file.FullName).Replace('\', '/')
            throw "A CLI/runtime Copilot não deve ser redistribuída no payload Linux: $relative"
        }
    }
    $productLicense = Join-Path $SourceDir 'LICENSE'
    if (-not (Test-Path -LiteralPath $productLicense -PathType Leaf) -or (Get-Item -LiteralPath $productLicense).Length -eq 0) {
        throw 'LICENSE do produto ausente ou vazio no payload Linux.'
    }
    Assert-KapibaraProductLicense (Get-Content -LiteralPath $productLicense -Raw)
    $notices = Join-Path $SourceDir 'THIRD-PARTY-NOTICES.md'
    if (-not (Test-Path -LiteralPath $notices -PathType Leaf) -or (Get-Item -LiteralPath $notices).Length -eq 0) {
        throw 'Avisos de terceiros, incluindo a licença MIT do Copilot SDK, ausentes no payload Linux.'
    }
    $noticeText = Get-Content -LiteralPath $notices -Raw
    Assert-KapibaraCopilotSdkNotice $noticeText
    Assert-KapibaraReleaseSbom $SourceDir
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
        'LICENSE' = 420 # 0644
        'THIRD-PARTY-NOTICES.md' = 420 # 0644
        '_manifest/spdx_2.2/manifest.spdx.json' = 420 # 0644
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
            $forbiddenNames = @('copilot.exe', 'copilot', 'copilot-runtime.exe', 'copilot-runtime', 'runtime.node',
                'libcopilot_runtime.so', 'copilot_runtime.dll', 'copilot-cli')
            $entryType = $entry.EntryType.ToString()
            if ($entryType -in @('RegularFile', 'V7RegularFile', 'ContiguousFile', 'HardLink', 'SymbolicLink')) {
                $entryName = $entry.Name.Replace('\', '/').TrimEnd('/').Split('/')[-1]
                $linkName = ([string]$entry.LinkName).Replace('\', '/').TrimEnd('/').Split('/')[-1]
                if ($entryName -in $forbiddenNames -or
                    ($entryType -in @('HardLink', 'SymbolicLink') -and $linkName -in $forbiddenNames)) {
                    throw "A CLI/runtime Copilot não deve ser redistribuída no pacote Release: $($entry.Name)"
                }
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
                $header = Read-KapibaraLinuxElfHeader $entry.DataStream
                if ($header[0] -ne 127 -or $header[1] -ne 69 -or $header[2] -ne 76 -or $header[3] -ne 70 -or
                    $header[4] -ne 2 -or $header[5] -ne 1 -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                    throw "Desktop ELF no pacote não corresponde à arquitetura $Rid."
                }
            }
            if ($entry.Name -eq 'THIRD-PARTY-NOTICES.md') {
                $readerText = [IO.StreamReader]::new($entry.DataStream, [Text.Encoding]::UTF8, $true, 1024, $true)
                try { $noticeText = $readerText.ReadToEnd() }
                finally { $readerText.Dispose() }
                Assert-KapibaraCopilotSdkNotice $noticeText
            }
            if ($entry.Name -eq 'LICENSE') {
                $readerText = [IO.StreamReader]::new($entry.DataStream, [Text.Encoding]::UTF8, $true, 1024, $true)
                try { $licenseText = $readerText.ReadToEnd() }
                finally { $readerText.Dispose() }
                Assert-KapibaraProductLicense $licenseText
            }
            if ($entry.Name -eq '_manifest/spdx_2.2/manifest.spdx.json') {
                $readerText = [IO.StreamReader]::new($entry.DataStream, [Text.Encoding]::UTF8, $true, 1024, $true)
                try { $sbomText = $readerText.ReadToEnd() }
                finally { $readerText.Dispose() }
                Assert-KapibaraReleaseSbomContent $sbomText
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
