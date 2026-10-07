[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')][string]$Rid
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$packageRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$extension = if ($Rid.StartsWith('win-')) { '.exe' } else { '' }
$required = @(
    "EsilvaSoft.KapibaraStudio.Desktop$extension",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md"
)
foreach ($relative in $required) {
    $file = Join-Path $packageRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Arquivo obrigatório do release ausente ou vazio: $relative"
    }
}
$productLicense = Join-Path $packageRoot 'LICENSE'
Assert-KapibaraProductLicense (Get-Content -LiteralPath $productLicense -Raw)
$notices = Get-Content -LiteralPath (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.md') -Raw
Assert-KapibaraCopilotSdkNotice $notices
Assert-KapibaraReleaseSbom $packageRoot
if (Get-ChildItem -LiteralPath $packageRoot -Directory -Filter 'mcp' -ErrorAction SilentlyContinue) {
    throw 'O proxy MCP do Claude Code não deve estar presente no pacote Release.'
}
# Release artifacts must not carry the separately licensed Copilot CLI/runtime at any payload path.
$forbiddenNames = @('copilot.exe', 'copilot', 'copilot-runtime.exe', 'copilot-runtime', 'runtime.node',
    'libcopilot_runtime.so', 'copilot_runtime.dll', 'copilot-cli')
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force) {
    if ($file.Name -in $forbiddenNames) {
        $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
        throw "O release não deve redistribuir a CLI/runtime Copilot: $relative"
    }
}
Write-Host "Release $Rid verificado: CLI oficial do usuário, avisos MIT do SDK e ausência de binários Copilot empacotados."
