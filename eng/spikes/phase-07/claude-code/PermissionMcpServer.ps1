#Requires -Version 7.0
<#
.SYNOPSIS
  Servidor MCP STDIO mínimo (JSON-RPC por linha) usado SOMENTE pelo spike P7-CL0-01 como alvo de
  --permission-prompt-tool. Expõe a tool "approve" e registra cada pedido em -LogPath (JSONL).

.DESCRIPTION
  Não é o McpServer do produto e não fala com o broker. Políticas:
    allow        -> {"behavior":"allow","updatedInput":<input recebido>}
    allow-bare   -> {"behavior":"allow"} (sem updatedInput)
    deny         -> {"behavior":"deny","message":"negado pelo host do spike"}
    rewrite      -> allow com updatedInput alterado (Bash: command = "echo REWRITTEN-BY-HOST")
    persist      -> allow com updatedPermissions (addRules/session) para observar se a CLI grava regra
    delay:<s>    -> espera <s> segundos e então responde allow
    deny-webfetch-> allow para tudo, exceto WebFetch/WebSearch (deny)
  O log contém apenas dados sintéticos do spike e NOMES (nunca valores) de variáveis de ambiente
  com prefixo SLOP_SPIKE_/CLAUDE/ANTHROPIC, para responder H-11.
#>
param(
    [Parameter(Mandatory)][string]$LogPath,
    [string]$Policy = 'allow'
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$stdout = [Console]::OpenStandardOutput()
$writer = [System.IO.StreamWriter]::new($stdout, $utf8)
$writer.AutoFlush = $true
$reader = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), $utf8)

function Write-Log([hashtable]$entry) {
    $entry['ts'] = [DateTimeOffset]::UtcNow.ToString('o')
    Add-Content -LiteralPath $LogPath -Value ($entry | ConvertTo-Json -Compress -Depth 20) -Encoding utf8NoBOM
}

function Send([object]$message) {
    $writer.WriteLine(($message | ConvertTo-Json -Compress -Depth 30))
}

$envNames = [System.Environment]::GetEnvironmentVariables().Keys |
    Where-Object { $_ -match '^(SLOP_SPIKE_|CLAUDE|ANTHROPIC)' } | Sort-Object
Write-Log @{ kind = 'start'; pid = $PID; policy = $Policy; envNames = @($envNames) }

$tool = @{
    name        = 'approve'
    description = 'Permission prompt handler for the Slop spike. Not for direct use.'
    inputSchema = @{ type = 'object'; additionalProperties = $true; properties = @{} }
}

while ($null -ne ($line = $reader.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $msg = $line | ConvertFrom-Json -AsHashtable -Depth 50
    $method = $msg['method']
    Write-Log @{ kind = 'recv'; method = $method; id = $msg['id']; params = $msg['params'] }
    switch ($method) {
        'initialize' {
            $pv = $msg['params']['protocolVersion']
            Send @{ jsonrpc = '2.0'; id = $msg['id']; result = @{
                    protocolVersion = $pv; capabilities = @{ tools = @{} }
                    serverInfo = @{ name = 'slop-spike-permission'; version = '0.0.1' } } }
        }
        'tools/list' {
            Send @{ jsonrpc = '2.0'; id = $msg['id']; result = @{ tools = @($tool) } }
        }
        'tools/call' {
            $arguments = $msg['params']['arguments']
            $toolName = $arguments['tool_name']
            $toolInput = $arguments['input']
            $effective = $Policy
            if ($Policy -like 'delay:*') {
                Start-Sleep -Seconds ([int]($Policy.Split(':')[1]))
                $effective = 'allow'
            }
            if ($Policy -eq 'deny-webfetch') {
                $effective = if ($toolName -in @('WebFetch', 'WebSearch')) { 'deny' } else { 'allow' }
            }
            $decision = switch ($effective) {
                'allow' { @{ behavior = 'allow'; updatedInput = $toolInput } }
                'allow-bare' { @{ behavior = 'allow' } }
                'deny' { @{ behavior = 'deny'; message = 'negado pelo host do spike' } }
                'rewrite' {
                    $copy = @{} + $toolInput
                    if ($copy.ContainsKey('command')) { $copy['command'] = 'echo REWRITTEN-BY-HOST' }
                    @{ behavior = 'allow'; updatedInput = $copy }
                }
                'persist' {
                    @{ behavior = 'allow'; updatedInput = $toolInput; updatedPermissions = @(
                            @{ type = 'addRules'; rules = @(@{ toolName = $toolName }); behavior = 'allow'; destination = 'session' }) }
                }
                default { @{ behavior = 'deny'; message = "politica desconhecida: $Policy" } }
            }
            $text = $decision | ConvertTo-Json -Compress -Depth 30
            Write-Log @{ kind = 'decision'; tool_name = $toolName; decision = $text }
            Send @{ jsonrpc = '2.0'; id = $msg['id']; result = @{ content = @(@{ type = 'text'; text = $text }) } }
        }
        default {
            if ($null -ne $msg['id']) {
                Send @{ jsonrpc = '2.0'; id = $msg['id']; error = @{ code = -32601; message = 'method not found' } }
            }
        }
    }
}
Write-Log @{ kind = 'eof' }
