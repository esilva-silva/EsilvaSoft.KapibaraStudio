[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$ComponentDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')][string]$Rid,
    [Parameter(Mandatory)][string]$PackageName,
    [Parameter(Mandatory)][string]$PackageVersion,
    [string]$ToolPath = (Join-Path $PSScriptRoot '../.tools/sbom/sbom-tool')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$componentRoot = (Resolve-Path -LiteralPath $ComponentDirectory).Path
$tool = if ($IsWindows -and (Test-Path -LiteralPath "$ToolPath.exe")) { "$ToolPath.exe" } else { $ToolPath }
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Executável sbom-tool ausente: $tool" }

$namespacePart = [Uri]::EscapeDataString("$PackageName-$Rid-$([Guid]::NewGuid().ToString('N'))")
$common = @(
    '-b', $publishRoot,
    '-bc', $componentRoot,
    '-pn', $PackageName,
    '-pv', $PackageVersion,
    '-ps', 'EsilvaSoft',
    '-nsb', 'https://esilvasoft.com/kapibarastudio/sbom',
    '-nsu', $namespacePart,
    '-mi', 'SPDX:2.2',
    '-pm'
)

& $tool generate @common
if ($LASTEXITCODE -ne 0) { throw "sbom-tool generate falhou para $Rid (exit $LASTEXITCODE)." }

$manifest = Join-Path $publishRoot '_manifest/spdx_2.2/manifest.spdx.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or (Get-Item -LiteralPath $manifest).Length -eq 0) {
    throw "SBOM SPDX 2.2 ausente ou vazio para $Rid."
}
Assert-KapibaraReleaseSbomContent -Json (Get-Content -LiteralPath $manifest -Raw) `
    -PackageName $PackageName -PackageVersion $PackageVersion

# Keep the validation report outside the hashed build drop so validation cannot change its inventory.
$validationDirectory = Join-Path (Split-Path -Parent $publishRoot) '.cache/sbom-validation'
New-Item -ItemType Directory -Path $validationDirectory -Force | Out-Null
$validationReport = Join-Path $validationDirectory "$Rid.validation.json"
& $tool validate -b $publishRoot -o $validationReport -mi 'SPDX:2.2' -n -V Error
if ($LASTEXITCODE -ne 0) { throw "sbom-tool validate falhou para $Rid (exit $LASTEXITCODE)." }

Write-Host "SBOM SPDX 2.2 gerado e validado para $PackageName $PackageVersion ($Rid): $manifest"
