[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')][string]$Rid
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$extension = if ($Rid.StartsWith('win-')) { '.exe' } else { '' }
$required = @(
    "EsilvaSoft.KapibaraStudio.Desktop$extension",
    "THIRD-PARTY-NOTICES.md"
)
foreach ($relative in $required) {
    $file = Join-Path $packageRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Arquivo obrigatório do release ausente ou vazio: $relative"
    }
}
$notices = Get-Content -LiteralPath (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.md') -Raw
foreach ($requiredNotice in @('GitHub.Copilot.SDK', 'Copyright GitHub, Inc.', 'MIT License')) {
    if (-not $notices.Contains($requiredNotice, [StringComparison]::Ordinal)) {
        throw "Aviso de terceiros do Copilot SDK ausente/incompleto: $requiredNotice"
    }
}
if (Get-ChildItem -LiteralPath $packageRoot -Directory -Filter 'mcp' -ErrorAction SilentlyContinue) {
    throw 'O proxy MCP do Claude Code não deve estar presente no pacote Release.'
}
# Release artifacts must not carry the separately licensed Copilot CLI/runtime.
foreach ($forbidden in @("runtimes/$Rid/native/copilot.exe", "runtimes/$Rid/native/copilot", "runtimes/$Rid/native/copilot-runtime.exe",
    "runtimes/$Rid/native/copilot-runtime", "runtimes/$Rid/native/runtime.node", "runtimes/$Rid/native/libcopilot_runtime.so",
    "runtimes/$Rid/native/copilot_runtime.dll", "runtimes/$Rid/copilot-cli")) {
    if (Test-Path -LiteralPath (Join-Path $packageRoot $forbidden)) {
        throw "O release não deve redistribuir a CLI/runtime Copilot: $forbidden"
    }
}
Write-Host "Release $Rid verificado: CLI oficial do usuário, avisos MIT do SDK e ausência de binários Copilot empacotados."
