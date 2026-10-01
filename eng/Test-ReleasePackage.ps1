[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')][string]$Rid,
    [switch]$SkipRuntimeExecution
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$extension = if ($Rid.StartsWith('win-')) { '.exe' } else { '' }
$required = @(
    "EsilvaSoft.KapibaraStudio.Desktop$extension",
    "mcp/EsilvaSoft.KapibaraStudio.McpServer$extension",
    "runtimes/$Rid/native/copilot$extension",
    "runtimes/$Rid/native/runtime.node",
    "runtimes/$Rid/copilot-cli/copilot$extension"
)
foreach ($relative in $required) {
    $file = Join-Path $packageRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Arquivo obrigatório do release ausente ou vazio: $relative"
    }
}
# Cross-compiles are checked structurally; only execute binaries for the host RID.
$hostRid = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
if (-not $SkipRuntimeExecution -and $hostRid -eq $Rid) {
    $cli = Join-Path $packageRoot "runtimes/$Rid/copilot-cli/copilot$extension"
    foreach ($arguments in @(@('--version'), @('login', '--help'))) {
        $start = [Diagnostics.ProcessStartInfo]::new($cli)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
        # Help/version do not authenticate, request a token, create sessions or send prompts.
        $process = [Diagnostics.Process]::Start($start)
        try {
            $output = $process.StandardOutput.ReadToEndAsync()
            $errorOutput = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'A CLI oficial não respondeu em 30 segundos.' }
            if ($process.ExitCode -ne 0) { throw "A CLI oficial falhou na checagem de ajuda/versão (exit $($process.ExitCode))." }
            if ([string]::IsNullOrWhiteSpace($output.GetAwaiter().GetResult())) { throw 'A CLI oficial não retornou ajuda/versão.' }
            $null = $errorOutput.GetAwaiter().GetResult()
        }
        finally { $process.Dispose() }
    }
}
if ($SkipRuntimeExecution) {
    Write-Host "Release $Rid verificado estruturalmente; execução de binários ignorada por política."
} else {
    Write-Host "Release $Rid verificado: aplicativo, proxy MCP, runtime Copilot e CLI interativa."
}
