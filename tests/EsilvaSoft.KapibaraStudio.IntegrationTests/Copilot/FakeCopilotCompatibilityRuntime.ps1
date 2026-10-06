# Isolated protocol fixture: never reads account files, calls a network service, or executes tools.
$ErrorActionPreference = 'Stop'
$scenario = 'Compatible'
$logPath = $null
for ($index = 0; $index -lt $args.Length - 1; $index++) {
    if ($args[$index] -eq '-Scenario') { $scenario = [string]$args[$index + 1] }
    if ($args[$index] -eq '-LogPath') { $logPath = [string]$args[$index + 1] }
}
$inputStream = [Console]::OpenStandardInput()
$outputStream = [Console]::OpenStandardOutput()
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Read-Frame {
    $header = [System.Collections.Generic.List[byte]]::new()
    while ($true) {
        $b = $inputStream.ReadByte()
        if ($b -lt 0) { return $null }
        $header.Add([byte]$b)
        $n = $header.Count
        if ($n -ge 4 -and $header[$n - 4] -eq 13 -and $header[$n - 3] -eq 10 -and $header[$n - 2] -eq 13 -and $header[$n - 1] -eq 10) { break }
    }
    if ([System.Text.Encoding]::ASCII.GetString($header.ToArray()) -notmatch '(?im)^Content-Length:\s*(\d+)') { throw 'Invalid frame.' }
    $body = [byte[]]::new([int]$Matches[1])
    $offset = 0
    while ($offset -lt $body.Length) {
        $read = $inputStream.Read($body, $offset, $body.Length - $offset)
        if ($read -le 0) { throw 'Unexpected EOF.' }
        $offset += $read
    }
    return $utf8.GetString($body) | ConvertFrom-Json
}

function Write-Frame($value) {
    $body = $utf8.GetBytes((ConvertTo-Json -InputObject $value -Depth 20 -Compress))
    $header = $utf8.GetBytes("Content-Length: $($body.Length)`r`n`r`n")
    $outputStream.Write($header, 0, $header.Length)
    $outputStream.Write($body, 0, $body.Length)
    $outputStream.Flush()
}

while ($null -ne ($request = Read-Frame)) {
    $method = [string]$request.method
    if ($logPath) { Add-Content -LiteralPath $logPath -Value $method -Encoding UTF8 }
    if ($null -eq $request.id) { continue }
    if (($scenario -eq 'ConnectFailure' -and $method -eq 'connect') -or
        ($scenario -eq 'MissingAuth' -and $method -eq 'auth.getStatus') -or
        ($scenario -eq 'MissingModels' -and $method -eq 'models.list') -or
        ($scenario -eq 'MissingRestrictions' -and $method -eq 'session.options.update')) {
        $code = $(if ($scenario -eq 'ConnectFailure') { -32000 } else { -32601 })
        Write-Frame @{ jsonrpc = '2.0'; id = $request.id; error = @{ code = $code; message = 'synthetic runtime failure secret-should-not-escape' } }
        continue
    }
    $result = switch ($method) {
        'connect' {
            $version = $(if ($scenario -eq 'OldProtocol') { 2 } elseif ($scenario -eq 'FutureProtocol') { 999 } else { 3 })
            $handshake = @{ ok = $true; protocolVersion = $version; version = 'synthetic-1.0'; taskKinds = @('agent', 'client', 'shell') }
            if ($scenario -eq 'MissingProtocol') { $handshake.Remove('protocolVersion') }
            $handshake
            break
        }
        'auth.getStatus' { @{ isAuthenticated = $true; authType = 'user' }; break }
        'models.list' { @{ models = @(@{ id = 'fake-model'; name = 'Fake model' }) }; break }
        'session.create' { @{ sessionId = [string]$request.params.sessionId; capabilities = @{} }; break }
        'session.options.update' { @{ success = $true }; break }
        'session.send' {
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = [string]$request.params.sessionId; event = @{ type = 'session.idle'; data = @{ mode = 'interactive' } } } }
            @{ messageId = 'should-not-send' }; break
        }
        'session.detach' { @{ success = $true }; break }
        'runtime.shutdown' { @{ success = $true }; break }
        default { Write-Frame @{ jsonrpc = '2.0'; id = $request.id; error = @{ code = -32601; message = 'Unsupported fixture method' } }; continue }
    }
    Write-Frame @{ jsonrpc = '2.0'; id = $request.id; result = $result }
}
