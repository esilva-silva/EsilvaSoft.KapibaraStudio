#requires -Version 7.4
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedHandshake.cs')
$testParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$testLeaf = 'slop-manifest-test-' + [guid]::NewGuid().ToString('N')
$testRoot = Join-Path $testParent $testLeaf
[void](New-Item -ItemType Directory -Path $testRoot)
$linkPaths = @()
try {
    $valid = Join-Path $testRoot 'valid.json'
    $validJson = @{ scope = 'availability-and-version-specific-schema-only'; executableSha256 = ('a' * 64); version = 'codex-cli 0.1.0-test' } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($valid, $validJson)
    $oversize = Join-Path $testRoot 'oversize.json'
    $stream = [IO.File]::Create($oversize)
    try { $stream.SetLength(1MB + 1) } finally { $stream.Dispose() }
    $invalid = Join-Path $testRoot 'invalid.json'
    [IO.File]::WriteAllText($invalid, 'SYNTHETIC_SECRET_MUST_NOT_APPEAR')
    $deep = Join-Path $testRoot 'deep.json'
    [IO.File]::WriteAllText($deep, ('[' * 9) + '0' + (']' * 9))
    $duplicate = Join-Path $testRoot 'duplicate.json'
    [IO.File]::WriteAllText($duplicate, $validJson.Replace('{', '{"scope":"other",'))
    $fileLink = Join-Path $testRoot 'leaf-link.json'
    $leafLinkAvailable = $false
    try {
        [void][IO.File]::CreateSymbolicLink($fileLink, $valid)
        $linkPaths += $fileLink
        $leafLinkAvailable = $true
    }
    catch {
        if (-not $IsWindows -or $_.Exception.InnerException.HResult -ne -2147023582) { throw }
        Write-Output 'SKIP: symlink no arquivo (Windows sem privilégio de criação; não homologado).'
    }
    $directoryLink = Join-Path $testRoot 'ancestor-link'
    if ($IsWindows) {
        [void](New-Item -ItemType Junction -Path $directoryLink -Target $testRoot)
    }
    else { [void][IO.Directory]::CreateSymbolicLink($directoryLink, $testRoot) }
    $linkPaths += $directoryLink
    $cases = @(
        @{ Name = 'manifesto válido'; Path = $valid; Accept = $true },
        @{ Name = 'arquivo acima de 1 MiB'; Path = $oversize; Accept = $false },
        @{ Name = 'JSON inválido sem eco do conteúdo'; Path = $invalid; Accept = $false },
        @{ Name = 'JSON acima da profundidade 8'; Path = $deep; Accept = $false },
        @{ Name = 'propriedade duplicada'; Path = $duplicate; Accept = $false },
        @{ Name = 'reparse point ancestral'; Path = (Join-Path $directoryLink 'valid.json'); Accept = $false }
    )
    if ($leafLinkAvailable) { $cases += @{ Name = 'symlink no arquivo'; Path = $fileLink; Accept = $false } }
    foreach ($case in $cases) {
        $accepted = $false
        try {
            $result = [SlopCodexHandshake]::ReadSchemaManifest($case.Path)
            $accepted = $result.Version -eq 'codex-cli 0.1.0-test' -and $result.Sha256 -eq ('a' * 64)
        }
        catch {
            if ($_.Exception.Message -notlike '*Manifesto recusado*' -or
                $_.Exception.Message -like '*SYNTHETIC_SECRET*') { throw }
        }
        if ($accepted -ne $case.Accept) { throw "Resultado incorreto: $($case.Name)" }
        Write-Output "PASS: $($case.Name)"
    }
}
finally {
    # Delete only the test-created link objects, never recurse through a junction.
    foreach ($link in $linkPaths) { Remove-Item -LiteralPath $link -Force }
    $full = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($full) -ne $testParent -or [IO.Path]::GetFileName($full) -cne $testLeaf) {
        throw 'Limpeza recusada: caminho de teste inesperado.'
    }
    if (@(Get-ChildItem -LiteralPath $full -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
        throw 'Limpeza recusada: reparse point restante.'
    }
    Remove-Item -LiteralPath $full -Recurse -Force
}
