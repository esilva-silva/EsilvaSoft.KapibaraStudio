[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Spike disponível somente no Windows; nenhum teste foi executado.' }

if (-not ('SlopStudio.Spikes.WindowsCredentials.CredentialManagerProbe' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CredentialManagerProbe.cs')
}

$results = @(
    foreach ($scenario in @('Success', 'FailureAfterWrite', 'FailureAfterRead')) {
        [SlopStudio.Spikes.WindowsCredentials.CredentialManagerProbe]::Run($scenario)
    }
)
$passed = $true
foreach ($result in $results) {
    $expectedStatus = if ($result.Scenario -eq 'Success') { 'Success' } else { 'InjectedFailure' }
    $requiresRead = $result.Scenario -ne 'FailureAfterWrite'
    if ($result.Status -ne $expectedStatus -or -not $result.Written -or -not $result.Deleted -or
        -not $result.ConfirmedAbsent -or $result.CleanupStatus -ne 'Success' -or
        $result.AbsenceNativeError -ne 1168 -or
        ($requiresRead -and (-not $result.ReadBackMatches -or -not $result.MetadataMatches))) {
        $passed = $false
    }
}

$report = [ordered]@{
    schemaVersion = 1
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    osVersion = [Environment]::OSVersion.Version.ToString()
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    credentialType = 'CRED_TYPE_GENERIC'
    persistence = 'CRED_PERSIST_LOCAL_MACHINE: current token user, same computer'
    scopeEvidence = 'Native API under current token; cross-user isolation not exercised'
    syntheticOnly = $true
    passed = $passed
    results = $results
}
$json = $report | ConvertTo-Json -Depth 6
$json | Set-Content (Join-Path $PSScriptRoot 'evidence.json') -Encoding utf8
Write-Output $json
if (-not $passed) { throw 'Spike reprovado. Consulte somente códigos saneados em evidence.json; nenhuma falha é tratada como aprovação.' }
