#requires -Version 7.4
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedHandshake.cs')

# Independent synthetic child processes. This test never starts Codex or reads credentials.
$syntheticHome = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'slop-synthetic-home-not-created'))
$validResponse = @{ id = 1; result = @{
    codexHome = $syntheticHome; platformFamily = 'windows'; platformOs = 'windows'; userAgent = 'synthetic'
} } | ConvertTo-Json -Depth 3 -Compress
$cases = @(
    @{ Name = 'resposta válida e EOF'; Accept = $true; Body = '[void][Console]::ReadLine(); [Console]::WriteLine($env:SLOP_SYNTHETIC_RESPONSE); [void][Console]::ReadLine(); while ($null -ne [Console]::ReadLine()) {}' },
    @{ Name = 'id não correlacionado'; Accept = $false; Body = '[void][Console]::ReadLine(); [Console]::WriteLine(''{"id":99,"result":{}}'')' },
    @{ Name = 'frame acima de 16 KiB'; Accept = $false; Body = '[void][Console]::ReadLine(); [Console]::WriteLine((''x'' * 16385))' },
    @{ Name = 'stderr acima de 64 KiB'; Accept = $false; Body = '[void][Console]::ReadLine(); [Console]::Error.Write((''x'' * 70000)); Start-Sleep -Seconds 30' },
    @{ Name = 'processo sem resposta e timeout'; Accept = $false; Body = '[void][Console]::ReadLine(); Start-Sleep -Seconds 30' }
)
foreach ($case in $cases) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Process -Id $PID).Path
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', $case.Body)) { [void]$info.ArgumentList.Add($argument) }
    $info.Environment.Clear()
    if ($IsWindows) { $info.Environment['SystemRoot'] = $env:SystemRoot }
    $info.Environment['SLOP_SYNTHETIC_RESPONSE'] = $validResponse
    $accepted = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $result = [SlopCodexHandshake]::RunAsync($info, $syntheticHome, 2).GetAwaiter().GetResult()
        $accepted = $result.ExitCode -eq 0 -and $result.CodexHomeMatchesScratch
    }
    catch {
        if ($_.Exception.Message -notlike '*Handshake recusado*') { throw }
    }
    if ($accepted -ne $case.Accept -or $watch.Elapsed.TotalSeconds -gt 10) {
        throw "Falha de comportamento/limite no cenário: $($case.Name)"
    }
    Write-Output "PASS: $($case.Name)"
}
