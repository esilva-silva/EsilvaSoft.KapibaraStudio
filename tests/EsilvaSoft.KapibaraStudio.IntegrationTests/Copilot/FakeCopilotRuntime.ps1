$ErrorActionPreference = 'Stop'
$inputStream = [Console]::OpenStandardInput()
$outputStream = [Console]::OpenStandardOutput()
$utf8 = [System.Text.UTF8Encoding]::new($false)
$authenticated = -not ($args -contains '-NoLogin')
$sessionExists = -not ($args -contains '-MissingSession')
$resumeFails = $args -contains '-ResumeFail'
$nativeToolRequest = $args -contains '-NativeToolRequest'
$productToolRequest = $args -contains '-ProductToolRequest'
$lateProductToolRequest = $args -contains '-LateProductToolRequest'
$rejectBuiltInAgentRestriction = $args -contains '-RejectBuiltInAgentRestriction'
$builtInAgentRestrictionMethodMissing = $args -contains '-BuiltInAgentRestrictionMethodMissing'
$editProposalToolRequest = $args -contains '-EditProposalToolRequest'
$noIdle = $args -contains '-NoIdle'
$delayIdleMs = 0
for ($index = 0; $index -lt $args.Length - 1; $index++) {
    if ($args[$index] -eq '-DelayIdleMs') { $delayIdleMs = [int]$args[$index + 1] }
}
$contractLogPath = $null
$scriptedToolRequestsPath = $null
for ($index = 0; $index -lt $args.Length - 1; $index++) {
    if ($args[$index] -eq '-ContractLogPath') { $contractLogPath = [string]$args[$index + 1] }
    if ($args[$index] -eq '-ScriptedToolRequestsPath') { $scriptedToolRequestsPath = [string]$args[$index + 1] }
}
$scriptedToolRequests = @()
$pendingScriptedRequests = @{}
if ($scriptedToolRequestsPath) {
    $scriptedToolRequests = Get-Content -LiteralPath $scriptedToolRequestsPath -Raw | ConvertFrom-Json
    foreach ($call in $scriptedToolRequests) { $pendingScriptedRequests[[string]$call.requestId] = $true }
}

function Read-Frame {
    $headerBytes = [System.Collections.Generic.List[byte]]::new()
    while ($true) {
        $b = $inputStream.ReadByte()
        if ($b -lt 0) { return $null }
        $headerBytes.Add([byte]$b)
        $n = $headerBytes.Count
        if ($n -ge 4 -and $headerBytes[$n - 4] -eq 13 -and $headerBytes[$n - 3] -eq 10 -and $headerBytes[$n - 2] -eq 13 -and $headerBytes[$n - 1] -eq 10) { break }
    }
    $header = [System.Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
    if ($header -notmatch '(?im)^Content-Length:\s*(\d+)') { throw 'Missing Content-Length frame header.' }
    $length = [int]$Matches[1]
    $body = [byte[]]::new($length)
    $offset = 0
    while ($offset -lt $length) {
        $read = $inputStream.Read($body, $offset, $length - $offset)
        if ($read -le 0) { throw 'Unexpected EOF inside JSON-RPC body.' }
        $offset += $read
    }
    return [System.Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
}

function Write-Frame($value) {
    $json = ConvertTo-Json -InputObject $value -Depth 80 -Compress
    $body = $utf8.GetBytes($json)
    $header = $utf8.GetBytes("Content-Length: $($body.Length)`r`n`r`n")
    $outputStream.Write($header, 0, $header.Length)
    $outputStream.Write($body, 0, $body.Length)
    $outputStream.Flush()
}

while ($null -ne ($request = Read-Frame)) {
    $method = [string]$request.method
    $requestId = $request.id
    if ($contractLogPath -and $method -in @('session.create', 'session.resume', 'session.options.update', 'session.send', 'session.getMetadata', 'session.tools.handlePendingToolCall', 'session.detach', 'session.abort', 'session.delete')) {
        $entry = @{ method = $method; params = $request.params; processId = $PID } | ConvertTo-Json -Depth 80 -Compress
        Add-Content -LiteralPath $contractLogPath -Value $entry -Encoding UTF8
    }
    $sessionId = 'fake-copilot-session'
    if ($request.params -and $request.params.sessionId) { $sessionId = [string]$request.params.sessionId }
    if ($null -eq $requestId) { continue }
    if ($method -eq 'session.resume' -and $resumeFails) {
        Write-Frame @{ jsonrpc = '2.0'; id = $requestId; error = @{ code = -32000; message = 'synthetic resume failure' } }
        continue
    }
    $isBuiltInAgentRestriction = $method -eq 'session.options.update' -and
        $request.params.PSObject.Properties.Name -contains 'includedBuiltinAgents'
    if ($isBuiltInAgentRestriction -and $builtInAgentRestrictionMethodMissing) {
        Write-Frame @{ jsonrpc = '2.0'; id = $requestId; error = @{ code = -32601; message = 'synthetic restriction method unavailable' } }
        continue
    }
    $result = switch ($method) {
        'connect' { @{ ok = $true; protocolVersion = 3; version = 'fake-runtime'; taskKinds = @('agent', 'client', 'shell') }; break }
        'auth.getStatus' { @{ isAuthenticated = $authenticated; authType = $(if ($authenticated) { 'user' } else { 'none' }); statusMessage = 'synthetic test account' }; break }
        'models.list' { @{ models = @(@{ id = 'fake-model'; name = 'Fake model' }) }; break }
        'session.getMetadata' { if ($sessionExists) { @{ session = @{ sessionId = $sessionId; startTime = '2026-09-28T00:00:00Z' } } } else { @{ session = $null } }; break }
        'session.create' { @{ sessionId = $sessionId; capabilities = @{} }; break }
        'session.resume' { @{ sessionId = $sessionId; capabilities = @{} }; break }
        'session.fs.setProvider' { @{ success = $true }; break }
        'session.options.update' { @{ success = -not ($isBuiltInAgentRestriction -and $rejectBuiltInAgentRestriction) }; break }
        'session.delete' { @{ success = $true }; break }
        'session.tools.handlePendingToolCall' {
            if ($scriptedToolRequestsPath) {
                $returnedRequest = [string]$request.params.requestId
                if (-not $pendingScriptedRequests.ContainsKey($returnedRequest)) { throw 'Unknown or duplicate scripted tool result.' }
                $pendingScriptedRequests.Remove($returnedRequest)
                if ($pendingScriptedRequests.Count -gt 0) { @{ success = $true }; break }
            }
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message_delta'; data = @{ messageId = 'assistant-after-tool'; deltaContent = 'ferramenta autorizada' } } } }
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message'; data = @{ messageId = 'assistant-after-tool'; content = 'ferramenta autorizada' } } } }
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'session.idle'; data = @{ mode = 'interactive' } } } }
            @{ success = $true }
            break
        }
        'session.send' {
            @{ messageId = 'fake-message-1' }
            if ($scriptedToolRequestsPath) {
                foreach ($call in $scriptedToolRequests) {
                    Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ id = [guid]::NewGuid().ToString(); type = 'external_tool.requested'; data = @{ requestId = [string]$call.requestId; sessionId = $sessionId; toolCallId = [string]$call.requestId; toolName = [string]$call.toolName; arguments = $call.arguments } } } }
                }
                break
            }
            if ($productToolRequest) {
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'external_tool.requested'; data = @{ requestId = 'rpc-product-1'; sessionId = $sessionId; toolCallId = 'tool-product-1'; toolName = 'get_workspace_context'; arguments = @{ scope = 'active' } } } } }
                break
            }
            if ($lateProductToolRequest) {
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message_delta'; data = @{ messageId = 'assistant-1'; deltaContent = 'resposta fake' } } } }
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message'; data = @{ messageId = 'assistant-1'; content = 'resposta fake' } } } }
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'session.idle'; data = @{ mode = 'interactive' } } } }
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'external_tool.requested'; data = @{ requestId = 'rpc-product-late'; sessionId = $sessionId; toolCallId = 'tool-product-late'; toolName = 'get_workspace_context'; arguments = @{ scope = 'active' } } } } }
                break
            }
            if ($editProposalToolRequest) {
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'external_tool.requested'; data = @{ requestId = 'rpc-edit-proposal-1'; sessionId = $sessionId; toolCallId = 'tool-edit-proposal-1'; toolName = 'propose_file_edit'; arguments = @{ target = 'active_buffer'; edits = @(@{ old_text = '.limit(10)'; new_text = '.limit(5)' }) } } } } }
                break
            }
            if ($nativeToolRequest) {
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'external_tool.requested'; data = @{ requestId = 'rpc-native-1'; sessionId = $sessionId; toolCallId = 'tool-native-1'; toolName = 'bash'; arguments = @{ command = 'echo must-not-run' } } } } }
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'session.idle'; data = @{ mode = 'interactive' } } } }
                break
            }
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message_delta'; data = @{ messageId = 'assistant-1'; deltaContent = 'resposta fake' } } } }
            Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'assistant.message'; data = @{ messageId = 'assistant-1'; content = 'resposta fake' } } } }
            if ($delayIdleMs -gt 0) { Start-Sleep -Milliseconds $delayIdleMs }
            if (-not $noIdle) {
                Write-Frame @{ jsonrpc = '2.0'; method = 'session.event'; params = @{ sessionId = $sessionId; event = @{ type = 'session.idle'; data = @{ mode = 'interactive' } } } }
            }
            break
        }
        'session.detach' { @{ success = $true }; break }
        'session.abort' { @{ success = $true }; break }
        'runtime.shutdown' { @{ success = $true }; break }
        default { Write-Frame @{ jsonrpc = '2.0'; id = $requestId; error = @{ code = -32601; message = "Unhandled fake method: $method" } }; continue }
    }
    Write-Frame @{ jsonrpc = '2.0'; id = $requestId; result = $result }
}
