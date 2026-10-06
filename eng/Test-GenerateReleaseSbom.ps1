<# Local command-contract fixtures. The double does not generate or validate a real release SBOM. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureParent = Join-Path $repositoryRoot '.cache/gh27-sbom-command-fixtures'
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$total = 0
$failures = [Collections.Generic.List[string]]::new()

function Test-ExpectedGeneration([scriptblock]$Invoke, [bool]$ShouldAccept, [string]$Label) {
    $script:total++
    $failure = $null
    try { & $Invoke | Out-Null }
    catch { $failure = $_.Exception }
    if ($ShouldAccept -and $null -ne $failure) { $failures.Add("$Label rejeitou controle: $($failure.Message)") }
    elseif (-not $ShouldAccept -and $null -eq $failure) { $failures.Add("$Label aceitou erro de geração/validação/identidade") }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    $fakeTool = Join-Path $fixtureRoot 'fake-sbom-tool.ps1'
    $fakeContent = @'
param()
[string[]]$ToolArguments = $args
$state = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'state.json') -Raw | ConvertFrom-Json
$action = $ToolArguments[0]
@{ action = $action; arguments = $ToolArguments } | ConvertTo-Json -Compress -Depth 5 |
    Add-Content -LiteralPath $state.log
$global:LASTEXITCODE = 0
function ArgumentValue([string]$Name) {
    $index = [Array]::IndexOf($ToolArguments, $Name)
    if ($index -lt 0 -or $index + 1 -ge $ToolArguments.Length) { return $null }
    return $ToolArguments[$index + 1]
}
if ($action -eq 'generate') {
    if ($state.mode -eq 'generateFailure' -or
        ($state.mode -eq 'strictGenerate' -and $ToolArguments -contains '-n')) {
        $global:LASTEXITCODE = 23
        return
    }
    $drop = ArgumentValue '-b'
    $manifestDirectory = Join-Path $drop '_manifest/spdx_2.2'
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    $name = ArgumentValue '-pn'
    $version = ArgumentValue '-pv'
    if ($state.mode -eq 'wrongVersion') { $version = '9.9.9-wrong' }
    if ($state.mode -eq 'wrongRootName') { $name = 'wrong-root' }
    $packages = @(@{ name = $name; versionInfo = $version })
    if ($state.mode -eq 'duplicateRoot') { $packages += @{ name = $name; versionInfo = $version } }
    @{ spdxVersion = 'SPDX-2.2'; packages = $packages } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $manifestDirectory 'manifest.spdx.json')
}
elseif ($action -eq 'validate') {
    $report = ArgumentValue '-o'
    if ($state.mode -eq 'validateFailure' -or
        ($state.mode -eq 'strictValidate' -and
            ([string]::IsNullOrWhiteSpace($report) -or (ArgumentValue '-mi') -ne 'SPDX:2.2'))) {
        $global:LASTEXITCODE = 31
        return
    }
    if ($null -ne $report) {
        $drop = [IO.Path]::GetFullPath((ArgumentValue '-b')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ([IO.Path]::GetFullPath($report).StartsWith($drop, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Validation report must not modify the inventoried build drop.'
        }
        if ($ToolArguments -notcontains '-n') { throw 'Validate must reject a manifest without detected packages.' }
        [IO.File]::WriteAllText($report, '{"fixture":"command contract only"}')
    }
}
else { throw "Unexpected fake tool action: $action" }
'@
    [IO.File]::WriteAllText($fakeTool, $fakeContent)
    $components = Join-Path $fixtureRoot 'components'
    New-Item -ItemType Directory -Path $components | Out-Null
    foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')) {
        $package = "EsilvaSoft.KapibaraStudio-0.0.0-fixture-$rid"
        foreach ($mode in @('strictGenerate', 'strictValidate', 'valid', 'wrongVersion', 'wrongRootName', 'duplicateRoot', 'generateFailure', 'validateFailure')) {
            $drop = Join-Path $fixtureRoot "$rid-$mode"
            $log = Join-Path $fixtureRoot "$rid-$mode.jsonl"
            New-Item -ItemType Directory -Path $drop | Out-Null
            @{ mode = $mode; log = $log } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'state.json')
            $accept = $mode -in @('strictGenerate', 'strictValidate', 'valid')
            Test-ExpectedGeneration {
                & (Join-Path $PSScriptRoot 'Generate-ReleaseSbom.ps1') -PublishDirectory $drop -ComponentDirectory $components `
                    -Rid $rid -PackageName $package -PackageVersion '0.0.0-fixture' -ToolPath $fakeTool
            } $accept "$rid/$mode"
            if ($mode -eq 'valid') {
                $first = Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object action -eq generate
                $secondDrop = Join-Path $fixtureRoot "$rid-repeat"
                New-Item -ItemType Directory -Path $secondDrop | Out-Null
                & (Join-Path $PSScriptRoot 'Generate-ReleaseSbom.ps1') -PublishDirectory $secondDrop -ComponentDirectory $components `
                    -Rid $rid -PackageName $package -PackageVersion '0.0.0-fixture' -ToolPath $fakeTool | Out-Null
                $second = Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object action -eq generate | Select-Object -Last 1
                $total++
                $firstUnique = $first.arguments[[Array]::IndexOf($first.arguments, '-nsu') + 1]
                $secondUnique = $second.arguments[[Array]::IndexOf($second.arguments, '-nsu') + 1]
                if ($firstUnique -eq $secondUnique) { $failures.Add("$rid namespace reused for distinct generations") }
            }
        }
    }
    $total++
    $workflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release.yml') -Raw
    $generateAt = $workflow.IndexOf('./eng/Generate-ReleaseSbom.ps1', [StringComparison]::Ordinal)
    $cleanupAt = $workflow.IndexOf('Remove-Item publish/*.pdb', [StringComparison]::Ordinal)
    if ($cleanupAt -lt 0 -or $generateAt -lt 0 -or $cleanupAt -gt $generateAt) {
        $failures.Add('Workflow changes the inventoried drop after SBOM generation/validation.')
    }
    if ($failures.Count -ne 0) { throw "$($failures.Count)/$total fixtures falharam: $($failures -join '; ')" }
    Write-Host "$total/$total fixtures passaram: argumentos, códigos de saída, identidade, namespace e ordem do workflow; tool sintética."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $allowed = [IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpeza de fixtures recusada fora do diretório reservado.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
