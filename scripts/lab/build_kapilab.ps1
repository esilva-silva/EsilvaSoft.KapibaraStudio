[CmdletBinding()]
param(
    [ValidateSet("Cpu", "WinML", "Cuda")]
    [string]$Backend = $(if ($IsWindows) { "WinML" } else { "Cpu" }),
    [ValidateSet("win-x64")]
    [string]$Rid = "win-x64",
    [switch]$Offline,
    [switch]$Test,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$project = Join-Path $repoRoot "tools/KapiLab/KapiLab.csproj"
$testProject = Join-Path $repoRoot "tools/KapiLab.Tests/KapiLab.Tests.csproj"
$backendProperty = "-p:SlopOnnxBackend=$Backend"
$restoreProperties = @("-m:1", $backendProperty)
if ($Offline) { $restoreProperties += "-p:NuGetAudit=false" }

dotnet restore $testProject --locked-mode @restoreProperties
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $testProject --no-restore $backendProperty -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Test) {
    $resultsDirectory = Join-Path $repoRoot "artifacts/f7b/test-results/$Backend"
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    dotnet test $testProject --no-build --no-restore $backendProperty `
        --logger "trx;LogFileName=kapilab-$Backend.trx" --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Publish) {
    if ($Backend -eq "Cuda") {
        throw "A publicação KapiLab Cuda ainda não está habilitada para aceite local."
    }
    if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw "A publicação win-x64 só pode ser gerada neste momento em host Windows x64."
    }

    $publishDirectory = Join-Path $repoRoot "artifacts/f7b/publish/$Backend/$Rid"
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    $publishArguments = @(
        "publish", $project, "--configuration", "Release",
        "--self-contained", "false", "--no-restore", "--output", $publishDirectory,
        $backendProperty, "-m:1"
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $executable = Join-Path $publishDirectory "EsilvaSoft.KapibaraStudio.KapiLab.exe"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "O apphost publicado não foi encontrado: $executable"
    }

    $environmentOutput = @(& $executable env 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível consultar o ambiente do binário publicado." }
    $environment = $environmentOutput -join "`n" | ConvertFrom-Json
    if ($environment.backend -ne $Backend -or $environment.architecture -ne "X64") {
        throw "O binário publicado diverge do backend ou arquitetura solicitados."
    }

    $commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível identificar o commit do checkout." }
    $dirty = [bool]((& git -C $repoRoot status --porcelain).Trim())
    $relativeExecutable = [IO.Path]::GetRelativePath((Join-Path $repoRoot "tools"), $executable).Replace('\', '/')
    $relativeFiles = [string[]](Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/')
    })
    [Array]::Sort($relativeFiles, [StringComparer]::Ordinal)
    $manifest = [Text.StringBuilder]::new()
    foreach ($relativeFile in $relativeFiles) {
        $filePath = Join-Path $publishDirectory $relativeFile
        $fileHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$manifest.Append($relativeFile).Append([char]0).Append($fileHash).Append("`n")
    }
    $contentHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifest.ToString()))).ToLowerInvariant()
    $entry = [ordered]@{
        backend = $Backend
        rid = $Rid
        executable = $relativeExecutable
        sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
        contentSha256 = $contentHash
        genAiVersion = [string]$environment.genAiVersion
        runtime = [string]$environment.runtime
        ideCommit = $commit
        ideDirty = $dirty
    }

    $lockPath = Join-Path $repoRoot "tools/kapilab.lock.json"
    $entries = @()
    if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
        $existingLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
        if ($existingLock.schema -ne "kapilab-distribution-lock-v1" -or $existingLock.entries -isnot [array]) {
            throw "Lockfile KapiLab existente tem schema desconhecido e não será sobrescrito."
        }
        $entries = @($existingLock.entries | Where-Object { $_.backend -ne $Backend -or $_.rid -ne $Rid })
    }
    $entries += $entry
    $lock = [ordered]@{ schema = "kapilab-distribution-lock-v1"; entries = $entries }
    $temporaryLock = "$lockPath.$([guid]::NewGuid().ToString('N')).tmp"
    $lock | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temporaryLock -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryLock -Destination $lockPath -Force
    Write-Host "Publicado $Backend/$Rid; lock atualizado em tools/kapilab.lock.json"
}
