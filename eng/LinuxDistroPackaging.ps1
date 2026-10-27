# Empacotamento Linux por distribuição (.deb, .rpm e fonte AUR) a partir dos tarballs do release.
# Uso: . ./eng/LinuxDistroPackaging.ps1 e depois New-KapibaraDistroPackages / New-KapibaraAurPackage.
# Os pacotes nunca tocam o $HOME do usuário nem têm scripts de manutenção (postinst/postrm).
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')

$script:KapibaraPackageName = 'kapibarastudio'
$script:KapibaraAurPackageName = 'kapibarastudio-bin'
$script:KapibaraProjectUrl = 'https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio'
$script:KapibaraPackageDescription = 'Desktop IDE for MongoDB'
$script:KapibaraIconSizes = @(16, 24, 32, 48, 64, 128, 256, 512)
$script:KapibaraPackagingDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../packaging/linux'))
$script:KapibaraBrandingDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/EsilvaSoft.KapibaraStudio.Desktop/Assets/Branding'))

# Dependências do Arch (as de deb e rpm ficam em packaging/linux/nfpm.yaml). Sem nenhuma fonte o Avalonia aborta
# na inicialização ("Default font family name can't be null"); ttf-font é o virtual usado pelos pacotes do Arch.
$script:KapibaraArchDepends = @('glibc', 'gcc-libs', 'icu', 'openssl', 'zlib', 'fontconfig', 'ttf-font', 'libx11', 'libice',
    'libsm', 'libxrandr', 'libxi', 'libxcursor', 'hicolor-icon-theme')
$script:KapibaraArchOptDepends = @('gnome-keyring: Secret Service')

# Arquitetura de cada formato por RID.
$script:KapibaraDistroArchitectures = @{
    'linux-x64'   = @{ deb = 'amd64'; rpm = 'x86_64'; pacman = 'x86_64' }
    'linux-arm64' = @{ deb = 'arm64'; rpm = 'aarch64'; pacman = 'aarch64' }
}

function ConvertTo-KapibaraDistroVersion {
    # SemVer X.Y.Z[-sufixo] -> versão do formato. deb/rpm: X.Y.Z~sufixo (o til ordena o pré-release antes do final);
    # pacman: X.Y.Zsufixo (pkgver não aceita hífen; vercmp também ordena letras antes da versão final).
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][ValidateSet('deb', 'rpm', 'pacman')][string]$Format
    )
    $match = [regex]::Match($Version, '^(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<pre>[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?$')
    if (-not $match.Success) { throw "Versão SemVer inválida (esperado X.Y.Z ou X.Y.Z-sufixo, sem metadados de build): $Version" }
    $core = $match.Groups['core'].Value
    $pre = $match.Groups['pre'].Value
    if ($pre.Length -eq 0) { return $core }
    if ($Format -eq 'pacman') { return $core + $pre.Replace('-', '') }
    # Hífen não é permitido na versão de deb sem revisão nem na de rpm.
    return $core + '~' + $pre.Replace('-', '.')
}

function Get-KapibaraDistroArchitecture([string]$Rid, [string]$Format) {
    if (-not $script:KapibaraDistroArchitectures.ContainsKey($Rid)) { throw "RID Linux inválido: $Rid" }
    return $script:KapibaraDistroArchitectures[$Rid][$Format]
}

function Expand-KapibaraLinuxArchive([string]$Archive, [string]$Destination) {
    # Extrai preservando o modo Unix; recusa caminhos fora do destino e tipos além de arquivo/diretório.
    $Destination = [IO.Path]::GetFullPath($Destination)
    $prefix = $Destination.TrimEnd('/') + '/'
    $inputStream = [IO.File]::OpenRead($Archive)
    $gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    $reader = [Formats.Tar.TarReader]::new($gzip, $false)
    try {
        while ($null -ne ($entry = $reader.GetNextEntry())) {
            $relative = $entry.Name.Replace('\', '/')
            while ($relative.StartsWith('./', [StringComparison]::Ordinal)) { $relative = $relative.Substring(2) }
            $relative = $relative.TrimEnd('/')
            if ($relative.Length -eq 0 -or $relative -eq '.') { continue }
            if ($relative.StartsWith('/', [StringComparison]::Ordinal) -or ($relative.Split('/') -contains '..')) {
                throw "Caminho inseguro no tarball Linux: $($entry.Name)"
            }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
            if (-not $target.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "Caminho fora do destino no tarball Linux: $($entry.Name)" }
            switch ($entry.EntryType.ToString()) {
                'Directory' { [IO.Directory]::CreateDirectory($target) | Out-Null }
                { $_ -in @('RegularFile', 'V7RegularFile') } {
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                    $entry.ExtractToFile($target, $true)
                    # Garante o modo do tarball mesmo se a extração aplicar umask.
                    [IO.File]::SetUnixFileMode($target, $entry.Mode)
                }
                { $_ -in @('PaxGlobalExtendedAttributes', 'ExtendedAttributes') } { }
                default { throw "Tipo de entrada não suportado no tarball Linux ($_): $($entry.Name)" }
            }
        }
    }
    finally {
        $reader.Dispose()
        $gzip.Dispose()
        $inputStream.Dispose()
    }
}

function New-KapibaraDebianCopyright([string]$LicensePath) {
    # DEP-5 simples; o corpo da licença vem do LICENSE do próprio tarball, sem reescrever o texto.
    $lines = @(Get-Content -LiteralPath $LicensePath)
    $start = 0
    while ($start -lt $lines.Count -and ($lines[$start].Trim().Length -eq 0 -or $lines[$start] -eq 'MIT License' -or
            $lines[$start].StartsWith('Copyright', [StringComparison]::Ordinal))) { $start++ }
    $body = foreach ($line in $lines[$start..($lines.Count - 1)]) { if ($line.Trim().Length -eq 0) { ' .' } else { ' ' + $line.TrimEnd() } }
    $header = @(
        'Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/'
        'Upstream-Name: EsilvaSoft.KapibaraStudio'
        "Source: $script:KapibaraProjectUrl"
        'Comment: Os componentes de terceiros embutidos no executável e suas licenças estão listados em'
        ' /usr/lib/kapibarastudio/THIRD-PARTY-NOTICES.md.'
        ''
        'Files: *'
        'Copyright: 2026 EsilvaSoft'
        'License: MIT'
        ''
        'License: MIT'
    )
    return (($header + $body) -join "`n") + "`n"
}

function Invoke-KapibaraNfpm([string]$Packager, [string]$Config, [string]$Target, [hashtable]$Environment) {
    if (-not (Get-Command nfpm -ErrorAction SilentlyContinue)) { throw 'nfpm não encontrado no PATH (esperado 2.48.0).' }
    $saved = @{}
    foreach ($name in $Environment.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Environment[$name])
    }
    try {
        & nfpm package --config $Config --packager $Packager --target $Target
        if ($LASTEXITCODE -ne 0) { throw "nfpm falhou ao gerar o pacote $Packager (código $LASTEXITCODE)." }
    }
    finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    if (-not (Test-Path -LiteralPath $Target -PathType Leaf) -or (Get-Item -LiteralPath $Target).Length -eq 0) {
        throw "nfpm não produziu o pacote esperado: $Target"
    }
}

function New-KapibaraDistroPackages {
    param(
        [Parameter(Mandatory)][string]$Archive,
        [Parameter(Mandatory)][ValidateSet('linux-x64', 'linux-arm64')][string]$Rid,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    if (-not $IsLinux) { throw 'New-KapibaraDistroPackages só funciona em Linux (pwsh); o nfpm e os modos Unix dependem dele.' }
    $Archive = [IO.Path]::GetFullPath($Archive)
    if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw "Tarball Linux inexistente: $Archive" }
    Assert-KapibaraLinuxArchive $Archive $Rid

    $debVersion = ConvertTo-KapibaraDistroVersion $Version 'deb'
    $rpmVersion = ConvertTo-KapibaraDistroVersion $Version 'rpm'
    $debArch = Get-KapibaraDistroArchitecture $Rid 'deb'
    $rpmArch = Get-KapibaraDistroArchitecture $Rid 'rpm'
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    # File names keep the SemVer text: GitHub renames release assets containing "~". The tilde stays in the
    # package Version field, where it orders a pre-release below the final version. Same names as
    # LinuxPackageUpdateGuidance.GetCommand (Core), which tells the user which file to install.
    $debPath = Join-Path $OutputDirectory "${script:KapibaraPackageName}_${Version}_${debArch}.deb"
    $rpmPath = Join-Path $OutputDirectory "${script:KapibaraPackageName}-${Version}-1.${rpmArch}.rpm"

    $staging = Join-Path ([IO.Path]::GetTempPath()) ("kapibara-distro-" + [guid]::NewGuid().ToString('N'))
    try {
        $payload = Join-Path $staging 'payload'
        $assets = Join-Path $staging 'assets'
        $markers = Join-Path $staging 'markers'
        foreach ($dir in @($payload, $assets, $markers)) { [IO.Directory]::CreateDirectory($dir) | Out-Null }
        Expand-KapibaraLinuxArchive $Archive $payload
        if (Test-Path -LiteralPath (Join-Path $payload 'package-manager')) {
            throw 'O tarball do release não deve conter o arquivo package-manager; ele é gerado por formato.'
        }

        Copy-Item -LiteralPath (Join-Path $script:KapibaraPackagingDirectory 'kapibarastudio.desktop') -Destination $assets
        foreach ($size in $script:KapibaraIconSizes) {
            Copy-Item -LiteralPath (Join-Path $script:KapibaraBrandingDirectory "kapibara-icon-$size.png") -Destination (Join-Path $assets "icon-$size.png")
        }
        [IO.File]::WriteAllText((Join-Path $assets 'copyright'), (New-KapibaraDebianCopyright (Join-Path $payload 'LICENSE')), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $markers 'package-manager.apt'), "apt`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $markers 'package-manager.dnf'), "dnf`n", [Text.UTF8Encoding]::new($false))

        $config = Join-Path $script:KapibaraPackagingDirectory 'nfpm.yaml'
        Invoke-KapibaraNfpm 'deb' $config $debPath @{ KAPIBARA_ARCH = $debArch; KAPIBARA_VERSION = $debVersion; KAPIBARA_STAGING = $staging }
        Invoke-KapibaraNfpm 'rpm' $config $rpmPath @{ KAPIBARA_ARCH = $rpmArch; KAPIBARA_VERSION = $rpmVersion; KAPIBARA_STAGING = $staging }
    }
    finally {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }

    Assert-KapibaraDistroPackage -Package $debPath -Format deb -Rid $Rid -Version $Version
    Assert-KapibaraDistroPackage -Package $rpmPath -Format rpm -Rid $Rid -Version $Version
    return @($debPath, $rpmPath)
}

function Invoke-KapibaraNativeText([string]$Command, [string[]]$Arguments) {
    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Falha ao executar $Command $($Arguments -join ' ') (código $LASTEXITCODE)." }
    return @($output)
}

function Assert-KapibaraDistroPackage {
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][ValidateSet('deb', 'rpm')][string]$Format,
        [Parameter(Mandatory)][ValidateSet('linux-x64', 'linux-arm64')][string]$Rid,
        [Parameter(Mandatory)][string]$Version
    )
    $Package = [IO.Path]::GetFullPath($Package)
    if (-not (Test-Path -LiteralPath $Package -PathType Leaf) -or (Get-Item -LiteralPath $Package).Length -eq 0) {
        throw "Pacote $Format ausente ou vazio: $Package"
    }
    $expectedVersion = ConvertTo-KapibaraDistroVersion $Version $Format
    $expectedArch = Get-KapibaraDistroArchitecture $Rid $Format
    $marker = if ($Format -eq 'deb') { 'apt' } else { 'dnf' }
    $exeRelative = 'usr/lib/kapibarastudio/EsilvaSoft.KapibaraStudio.Desktop'
    $listPattern = '^(?<mode>[-dl][rwxsStT-]{9})\s.*?\s(?<path>\.?/\S.*?)(?: -> (?<target>\S+))?$'

    if ($Format -eq 'deb') {
        if (-not (Get-Command dpkg-deb -ErrorAction SilentlyContinue)) {
            Write-KapibaraSkippedCheck 'dpkg-deb ausente: metadados e conteúdo do .deb não foram conferidos.'
            return
        }
        $fields = Invoke-KapibaraNativeText 'dpkg-deb' @('-f', $Package, 'Package', 'Version', 'Architecture')
        if ("$($fields[0])" -cne "Package: $script:KapibaraPackageName" -or "$($fields[1])" -cne "Version: $expectedVersion" -or
            "$($fields[2])" -cne "Architecture: $expectedArch") {
            throw "Metadados do .deb divergentes (esperado $script:KapibaraPackageName $expectedVersion $expectedArch): $($fields -join '; ')"
        }
        $listing = @(& bash -c 'dpkg-deb --fsys-tarfile "$1" | tar -tvf -' _ $Package)
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao listar o conteúdo do .deb.' }
        $markerText = (& bash -c 'dpkg-deb --fsys-tarfile "$1" | tar -xOf - ./usr/lib/kapibarastudio/package-manager' _ $Package) -join ''
        if ($LASTEXITCODE -ne 0) { throw 'Marcador package-manager ausente no .deb.' }
    }
    else {
        if (-not (Get-Command rpm -ErrorAction SilentlyContinue)) {
            Write-KapibaraSkippedCheck 'rpm ausente: metadados e conteúdo do .rpm não foram conferidos.'
            return
        }
        # A private empty database avoids lock errors on hosts without an rpm database (Debian/Ubuntu runners).
        $rpmDb = Join-Path ([IO.Path]::GetTempPath()) "kapibara-rpmdb-$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Force $rpmDb | Out-Null
        $fields = (Invoke-KapibaraNativeText 'rpm' @('--dbpath', $rpmDb, '-qp', '--qf', '%{NAME}|%{VERSION}|%{RELEASE}|%{ARCH}', $Package)) -join ''
        if ($fields -cne "$script:KapibaraPackageName|$expectedVersion|1|$expectedArch") {
            throw "Metadados do .rpm divergentes (esperado $script:KapibaraPackageName|$expectedVersion|1|$expectedArch): $fields"
        }
        $listing = Invoke-KapibaraNativeText 'rpm' @('--dbpath', $rpmDb, '-qlvp', $Package)
        Remove-Item -LiteralPath $rpmDb -Recurse -Force -ErrorAction Ignore
        if (Get-Command rpm2cpio -ErrorAction SilentlyContinue) {
            if (Get-Command cpio -ErrorAction SilentlyContinue) {
                $markerText = (& bash -c 'rpm2cpio "$1" | cpio -i --to-stdout --quiet ./usr/lib/kapibarastudio/package-manager' _ $Package) -join ''
                if ($LASTEXITCODE -ne 0) { throw 'Marcador package-manager ausente no .rpm.' }
            }
        }
        if ($null -eq $markerText) { Write-KapibaraSkippedCheck 'rpm2cpio/cpio ausentes: conteúdo do marcador do .rpm não foi conferido.' }
    }

    $entries = @{}
    foreach ($line in $listing) {
        $match = [regex]::Match([string]$line, $listPattern)
        if ($match.Success) { $entries[$match.Groups['path'].Value.TrimStart('.').TrimStart('/')] = $match }
    }
    if (-not $entries.ContainsKey($exeRelative) -or $entries[$exeRelative].Groups['mode'].Value -cne '-rwxr-xr-x') {
        throw "Executável ausente ou sem modo 0755 no pacote ${Format}: /$exeRelative"
    }
    if (-not $entries.ContainsKey('usr/bin/kapibarastudio') -or
        $entries['usr/bin/kapibarastudio'].Groups['mode'].Value[0] -ne 'l' -or
        $entries['usr/bin/kapibarastudio'].Groups['target'].Value -cne "/$exeRelative") {
        throw "Link /usr/bin/kapibarastudio ausente ou com destino incorreto no pacote $Format."
    }
    foreach ($path in @('usr/lib/kapibarastudio/package-manager', 'usr/lib/kapibarastudio/LICENSE',
            'usr/lib/kapibarastudio/THIRD-PARTY-NOTICES.md', 'usr/share/applications/kapibarastudio.desktop')) {
        if (-not $entries.ContainsKey($path)) { throw "Arquivo ausente no pacote ${Format}: /$path" }
    }
    foreach ($size in $script:KapibaraIconSizes) {
        $icon = "usr/share/icons/hicolor/${size}x${size}/apps/kapibarastudio.png"
        if (-not $entries.ContainsKey($icon)) { throw "Ícone ausente no pacote ${Format}: /$icon" }
    }
    $licensePath = if ($Format -eq 'deb') { 'usr/share/doc/kapibarastudio/copyright' } else { 'usr/share/licenses/kapibarastudio/LICENSE' }
    if (-not $entries.ContainsKey($licensePath)) { throw "Licença ausente no pacote ${Format}: /$licensePath" }
    if ($null -ne $markerText -and $markerText.Trim() -cne $marker) {
        throw "Conteúdo do marcador package-manager incorreto no pacote ${Format}: esperado '$marker', obtido '$markerText'."
    }
}

# Conferência opcional em máquina de desenvolvimento; no CI a ferramenta ausente é erro, para a verificação não sumir em silêncio.
function Write-KapibaraSkippedCheck([string]$Message) {
    if ($env:CI -eq 'true') { throw $Message }
    Write-Warning $Message
}

function Get-KapibaraFileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-KapibaraSrcInfo([hashtable]$Values) {
    # Mesmo conteúdo de `makepkg --printsrcinfo` (conferido com makepkg 7.1 no Arch): tab de indentação, LF, chaves
    # na ordem do makepkg (arch, license, depends, optdepends, provides, conflicts, noextract, options, source e sums
    # genéricos, depois source/sums de cada arquitetura).
    $lines = [Collections.Generic.List[string]]::new()
    $add = { param([string]$Key, $Items) foreach ($item in @($Items)) { $lines.Add("`t$Key = $item") } }
    $lines.Add("pkgbase = $script:KapibaraAurPackageName")
    & $add 'pkgdesc' $script:KapibaraPackageDescription
    & $add 'pkgver' $Values.PkgVer
    & $add 'pkgrel' '1'
    & $add 'url' $script:KapibaraProjectUrl
    & $add 'arch' @('x86_64', 'aarch64')
    & $add 'license' 'MIT'
    & $add 'depends' $script:KapibaraArchDepends
    & $add 'optdepends' $script:KapibaraArchOptDepends
    & $add 'provides' $script:KapibaraPackageName
    & $add 'conflicts' $script:KapibaraPackageName
    & $add 'noextract' @($Values.X64Name, $Values.Arm64Name)
    & $add 'options' @('!strip', '!debug')
    & $add 'source' $Values.LocalNames
    & $add 'sha256sums' $Values.LocalHashes
    & $add 'source_x86_64' "$($Values.X64Name)::$($Values.X64Url)"
    & $add 'sha256sums_x86_64' $Values.X64Hash
    & $add 'source_aarch64' "$($Values.Arm64Name)::$($Values.Arm64Url)"
    & $add 'sha256sums_aarch64' $Values.Arm64Hash
    $lines.Add('')
    $lines.Add("pkgname = $script:KapibaraAurPackageName")
    return ($lines -join "`n") + "`n"
}

function New-KapibaraAurPackage {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$X64Archive,
        [Parameter(Mandatory)][string]$Arm64Archive,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    $pkgver = ConvertTo-KapibaraDistroVersion $Version 'pacman'
    $x64Name = "EsilvaSoft.KapibaraStudio-$Version-linux-x64.tar.gz"
    $arm64Name = "EsilvaSoft.KapibaraStudio-$Version-linux-arm64.tar.gz"
    $X64Archive = [IO.Path]::GetFullPath($X64Archive)
    $Arm64Archive = [IO.Path]::GetFullPath($Arm64Archive)
    if ([IO.Path]::GetFileName($X64Archive) -cne $x64Name) { throw "Nome do tarball x64 deve ser exatamente $x64Name." }
    if ([IO.Path]::GetFileName($Arm64Archive) -cne $arm64Name) { throw "Nome do tarball arm64 deve ser exatamente $arm64Name." }
    foreach ($file in @($X64Archive, $Arm64Archive)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Tarball Linux ausente ou vazio: $file"
        }
    }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $destination = Join-Path $OutputDirectory "$script:KapibaraAurPackageName-$Version-aur.tar.gz"

    $work = Join-Path ([IO.Path]::GetTempPath()) ("kapibara-aur-" + [guid]::NewGuid().ToString('N'))
    try {
        $dir = Join-Path $work $script:KapibaraAurPackageName
        [IO.Directory]::CreateDirectory($dir) | Out-Null

        # Fontes locais: .desktop e ícones renomeados como o PKGBUILD espera.
        $localNames = [Collections.Generic.List[string]]::new()
        Copy-Item -LiteralPath (Join-Path $script:KapibaraPackagingDirectory 'kapibarastudio.desktop') -Destination (Join-Path $dir 'kapibarastudio.desktop')
        $localNames.Add('kapibarastudio.desktop')
        foreach ($size in $script:KapibaraIconSizes) {
            Copy-Item -LiteralPath (Join-Path $script:KapibaraBrandingDirectory "kapibara-icon-$size.png") -Destination (Join-Path $dir "kapibarastudio-$size.png")
            $localNames.Add("kapibarastudio-$size.png")
        }
        $localHashes = @($localNames | ForEach-Object { Get-KapibaraFileSha256 (Join-Path $dir $_) })

        $baseUrl = "$script:KapibaraProjectUrl/releases/download/v$Version"
        $values = @{
            PkgVer = $pkgver; LocalNames = @($localNames); LocalHashes = $localHashes
            X64Name = $x64Name; X64Url = "$baseUrl/$x64Name"; X64Hash = Get-KapibaraFileSha256 $X64Archive
            Arm64Name = $arm64Name; Arm64Url = "$baseUrl/$arm64Name"; Arm64Hash = Get-KapibaraFileSha256 $Arm64Archive
        }
        $quote = { param($items) (@($items) | ForEach-Object { "'$_'" }) -join ' ' }
        $template = (Get-Content -LiteralPath (Join-Path $script:KapibaraPackagingDirectory 'aur/PKGBUILD.in') -Raw).Replace("`r`n", "`n")
        $replacements = [ordered]@{
            '@SEMVER@'        = "'$Version'"
            '@PKGVER@'        = $pkgver
            '@PKGDESC@'       = "'$script:KapibaraPackageDescription'"
            '@URL@'           = "'$script:KapibaraProjectUrl'"
            '@DEPENDS@'       = & $quote $script:KapibaraArchDepends
            '@OPTDEPENDS@'    = & $quote $script:KapibaraArchOptDepends
            '@SOURCE_LOCAL@'  = & $quote $localNames
            '@SHA256_LOCAL@'  = & $quote $localHashes
            '@SHA256_X64@'    = $values.X64Hash
            '@SHA256_ARM64@'  = $values.Arm64Hash
        }
        $pkgbuild = $template
        foreach ($key in $replacements.Keys) { $pkgbuild = $pkgbuild.Replace($key, $replacements[$key]) }
        if ($pkgbuild -match '@[A-Z0-9_]+@') { throw "Marcador não substituído no PKGBUILD: $($Matches[0])" }
        $utf8 = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText((Join-Path $dir 'PKGBUILD'), $pkgbuild, $utf8)
        [IO.File]::WriteAllText((Join-Path $dir '.SRCINFO'), (New-KapibaraSrcInfo $values), $utf8)

        # Tarball determinístico: ordem fixa, mtime fixo, modos 0755/0644.
        $output = [IO.File]::Create($destination)
        $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::SmallestSize)
        $writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Pax, $false)
        try {
            $mtime = [DateTimeOffset]::UnixEpoch
            $dirEntry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::Directory, "$script:KapibaraAurPackageName/")
            $dirEntry.Mode = [IO.UnixFileMode][Convert]::ToInt32('755', 8)
            $dirEntry.ModificationTime = $mtime
            $writer.WriteEntry($dirEntry)
            foreach ($file in Get-ChildItem -LiteralPath $dir -File -Force | Sort-Object { $_.Name } -CaseSensitive) {
                $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, "$script:KapibaraAurPackageName/$($file.Name)")
                $entry.Mode = [IO.UnixFileMode][Convert]::ToInt32('644', 8)
                $entry.ModificationTime = $mtime
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
    }
    finally {
        if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    }
    return $destination
}
