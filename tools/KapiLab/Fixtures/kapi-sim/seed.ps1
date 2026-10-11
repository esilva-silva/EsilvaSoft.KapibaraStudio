[CmdletBinding()]
param(
    [string]$Uri = 'mongodb://127.0.0.1:27017',
    [string]$Kapilab = 'tools/KapiLab/bin/Debug/net10.0/EsilvaSoft.KapibaraStudio.KapiLab.dll'
)
$ErrorActionPreference = 'Stop'
$fixtureRepo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$fixturePreviousUri = $env:KAPILAB_MONGO_URI
try {
    $env:KAPILAB_MONGO_URI = $Uri
    & dotnet (Join-Path $fixtureRepo $Kapilab) --workspace $fixtureRepo fixture seed --fixture 'tools/KapiLab/Fixtures/kapi-sim/cases/reference.json'
    if ($LASTEXITCODE -ne 0) { throw "Semeadura recusada ou falhou (código $LASTEXITCODE)." }
}
finally { $env:KAPILAB_MONGO_URI = $fixturePreviousUri }
