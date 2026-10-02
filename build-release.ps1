<#
.SYNOPSIS
    Compila localmente os pacotes de release (espelha .github/workflows/release.yml).

.EXAMPLE
    ./build-release.ps1                      # versão da última tag git (ou 0.0.0-local)
    ./build-release.ps1 1.2.3                # versão explícita
    ./build-release.ps1 1.2.3 -SkipTests -Rids win-x64,linux-x64
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string[]]$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64'),

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Solution = 'EsilvaSoft.KapibaraStudio.slnx'
$DesktopProject = 'src/EsilvaSoft.KapibaraStudio.Desktop/EsilvaSoft.KapibaraStudio.Desktop.csproj'
$ExecutableName = 'EsilvaSoft.KapibaraStudio.Desktop'

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Falhou: $Title (exit $LASTEXITCODE)" }
}

. (Join-Path $PSScriptRoot 'eng/ReleasePackaging.ps1')
Push-Location $PSScriptRoot
try {
    if (-not $Version) {
        $tag = git describe --tags --abbrev=0 2>$null
        $Version = if ($LASTEXITCODE -eq 0 -and $tag) { $tag.Trim().TrimStart('v') } else { '0.0.0-local' }
    }
    $Version = $Version.TrimStart('v')
    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$') {
        throw "Versão '$Version' inválida. Use MAJOR.MINOR.PATCH ou MAJOR.MINOR.PATCH-sufixo."
    }

    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    $publishRoot = Join-Path $artifacts 'publish'
    $distDir = Join-Path $artifacts "release/$Version"

    function Remove-ReleaseDirectory([string]$Path) {
        $resolved = [IO.Path]::GetFullPath($Path)
        $allowedRoot = [IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Limpeza recusada fora de artifacts: $resolved"
        }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }

    Write-Host "EsilvaSoft.KapibaraStudio $Version -> $($Rids -join ', ')" -ForegroundColor Green

    Remove-ReleaseDirectory $distDir
    New-Item -ItemType Directory -Force $distDir | Out-Null

    Invoke-Step 'restore' { dotnet restore $Solution --locked-mode }
    Invoke-Step 'build' { dotnet build $Solution --no-restore -c Release "-p:Version=$Version" }
    if (-not $SkipTests) {
        if ($IsLinux) {
            Invoke-Step 'unit tests (Linux)' {
                dotnet test 'tests/EsilvaSoft.KapibaraStudio.UnitTests/EsilvaSoft.KapibaraStudio.UnitTests.csproj' --no-build --no-restore -c Release
            }
            Invoke-Step 'agent adapter unit tests (Linux)' {
                dotnet test 'tests/EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests/EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.csproj' --no-build --no-restore -c Release
            }
        }
        else {
            Invoke-Step 'test' { dotnet test $Solution --no-build --no-restore -c Release }
        }
    }

    foreach ($rid in $Rids) {
        $publishDir = Join-Path $publishRoot $rid
        $packageName = "EsilvaSoft.KapibaraStudio-$Version-$rid"
        Remove-ReleaseDirectory $publishDir

        # Cada família usa seu backend ONNX e lock file (WinML no Windows, CPU no Linux), como no release.yml.
        # O restore sem -r já inclui os RIDs do Desktop; um restore implícito com -r propagaria o RID
        # aos projetos referenciados e quebraria os lock files.
        $backend = if ($rid.StartsWith('win-')) { 'WinML' } else { 'Cpu' }
        Invoke-Step "restore $rid ($backend)" { dotnet restore $DesktopProject --locked-mode "-p:SlopOnnxBackend=$backend" }
        Invoke-Step "publish $rid" {
            dotnet publish $DesktopProject `
                --no-restore `
                -c Release `
                -r $rid `
                --self-contained true `
                "-p:SlopOnnxBackend=$backend" `
                -p:PublishSingleFile=true `
                -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:DebugType=none `
                -p:PublishDocumentationFiles=false `
                -p:PublishReferencesDocumentationFiles=false `
                "-p:Version=$Version" `
                -o $publishDir
        }

        Remove-Item (Join-Path $publishDir '*.pdb'), (Join-Path $publishDir '*.xml'), (Join-Path $publishDir '*.lib') -ErrorAction Ignore

        & (Join-Path $PSScriptRoot 'eng/Test-ReleasePackage.ps1') -PublishDirectory $publishDir -Rid $rid

        Write-Host "==> package $rid" -ForegroundColor Cyan
        if ($rid.StartsWith('win-')) {
            Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath (Join-Path $distDir "$packageName.zip") -Force
        }
        else {
            if (-not (Test-Path (Join-Path $publishDir $ExecutableName))) {
                throw "Executável '$ExecutableName' não encontrado em $publishDir"
            }
            New-KapibaraLinuxPackage $publishDir (Join-Path $distDir "$packageName.tar.gz") $rid
        }
    }

    # Devolve o workspace ao backend padrão desta máquina para builds normais sem restore.
    Invoke-Step 'restore (backend padrão)' { dotnet restore $Solution --locked-mode }

    Write-Host "`n==> checksums" -ForegroundColor Cyan
    $packages = Get-ChildItem $distDir -File | Where-Object { $_.Name -like '*.zip' -or $_.Name -like '*.tar.gz' } | Sort-Object Name
    $packages |
        ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" } |
        Set-Content (Join-Path $distDir 'SHA256SUMS.txt') -Encoding utf8NoBOM

    Write-Host "`nPacotes gerados em $distDir" -ForegroundColor Green
    Get-ChildItem $distDir -File | ForEach-Object { '  {0,-60} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
}
finally {
    Pop-Location
}
