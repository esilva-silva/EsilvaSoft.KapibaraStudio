using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

internal enum TranslationKind
{
    Continue,

    /// <summary>A linha não é um objeto JSON com <c>type</c>; descartada (conta no limite de linhas descartadas).</summary>
    Invalid,

    /// <summary><c>result</c> recebido: o turno terminou do ponto de vista da CLI.</summary>
    Result,

    /// <summary>Violação de contrato ou segurança: o turno é abortado e a árvore de processos encerrada.</summary>
    Abort,
}

internal readonly record struct TranslationStep(TranslationKind Kind, string? ErrorCode = null)
{
    public static TranslationStep Continue { get; } = new(TranslationKind.Continue);

    public static TranslationStep Invalid { get; } = new(TranslationKind.Invalid);

    public static TranslationStep Abort(string code) => new(TranslationKind.Abort, code);
}

/// <summary>Resumo do <c>result</c> (somente números e tokens seguros; nunca texto da resposta ou dos erros).</summary>
internal sealed record ClaudeCodeResultInfo(
    bool IsError,
    string? Subtype,
    int? NumTurns,
    double? TotalCostUsd,
    long? InputTokens,
    long? OutputTokens,
    int PermissionDenials,
    string? TerminalReason,
    int? ApiErrorStatus,
    bool SessionNotFound);

/// <summary>
/// Tradução de uma linha stream-json do Claude Code (formato observado no spike P7-CL0-01, 2.1.268) para
/// <see cref="AgentProviderEvent"/>. Estado por turno. Tipos externos não saem desta classe; nenhum texto de
/// ferramenta, caminho, prompt, stderr ou identificador nativo é copiado para eventos. Blocos de <i>thinking</i>
/// (texto vazio + assinatura opaca) são descartados.
/// </summary>
internal sealed class ClaudeCodeStreamTranslator(
    string expectedSessionId, ClaudeCodeVersion minimumVersion, string requestedModel, ClaudeCodeTurnSetup setup
#if DEBUG
    , IClaudeCodeSystem? system = null, string? debugLogDirectory = null
#endif
    )
{
    private readonly IReadOnlySet<string> _expectedTools = setup.ExpectedInitTools;
    private readonly IReadOnlySet<string> _optionalTools = setup.OptionalInitTools;
    private readonly IReadOnlyDictionary<string, string> _callableTools = setup.CallableTools;
    private readonly bool _expectsMcpServer = setup.RequiresMcpChannel;

    private const string NoConversationMarker = "No conversation found";

    private readonly Dictionary<string, AgentMessageId> _openText = new(StringComparer.Ordinal);
    private readonly HashSet<string> _streamedMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (AgentToolCallId Id, string Name, bool Terminal)> _tools = new(StringComparer.Ordinal);
    private string? _currentMessageId;

    public bool InitValidated { get; private set; }

    public string? ObservedModel { get; private set; }

    public ClaudeCodeResultInfo? Result { get; private set; }

    public int ApiRetries { get; private set; }

    public string? LastApiRetryCategory { get; private set; }

    public bool RateLimitRejected { get; private set; }

    public int NativeToolCalls => _tools.Values.Count(static tool => IsNativeTool(tool.Name));

    public TranslationStep Translate(string line, List<AgentProviderEvent> output)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return TranslationStep.Invalid;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || String(root, "type") is not { } type)
            {
                return TranslationStep.Invalid;
            }

            return type switch
            {
                "system" => HandleSystem(root),
                "rate_limit_event" => HandleRateLimit(root),
                "stream_event" => InitValidated ? HandleStreamEvent(root, output) : TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation),
                "assistant" => InitValidated ? HandleAssistant(root, output) : TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation),
                "user" => InitValidated ? HandleUser(root, output) : TranslationStep.Continue,
                "result" => HandleResult(root, output),
                _ => TranslationStep.Continue,
            };
        }
    }

    /// <summary>Fecha mensagens de texto que ficaram abertas (fim do processo sem <c>content_block_stop</c>).</summary>
    public void CloseOpenMessages(List<AgentProviderEvent> output)
    {
        foreach (var id in _openText.Values)
        {
            output.Add(new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: id));
        }

        _openText.Clear();
    }

    /// <summary>Código de erro do <c>result</c>, ou nulo para sucesso.</summary>
    public string? ResultErrorCode()
    {
        if (Result is not { } result)
        {
            return ClaudeCodeErrorCodes.StreamIncomplete;
        }

        if (!result.IsError && string.Equals(result.Subtype, "success", StringComparison.Ordinal))
        {
            return null;
        }

        if (result.SessionNotFound)
        {
            return ClaudeCodeErrorCodes.SessionNotFound;
        }

        if (string.Equals(result.Subtype, "error_max_turns", StringComparison.Ordinal))
        {
            return ClaudeCodeErrorCodes.MaxTurnsReached;
        }

        return result.ApiErrorStatus switch
        {
            401 or 403 => ClaudeCodeErrorCodes.AuthenticationFailed,
            429 => ClaudeCodeErrorCodes.RateLimited,
            400 or 413 or 422 => ClaudeCodeErrorCodes.RequestRejected,
            >= 500 => ClaudeCodeErrorCodes.ProviderUnavailable,
            _ => RateLimitRejected ? ClaudeCodeErrorCodes.RateLimited : ClaudeCodeErrorCodes.ExecutionError,
        };
    }

    private TranslationStep HandleSystem(JsonElement root)
    {
        switch (String(root, "subtype"))
        {
            case "init":
                if (InitValidated)
                {
                    return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
                }

                if (!IsExpectedInit(root))
                {
                    return TranslationStep.Abort(ClaudeCodeErrorCodes.InitMismatch);
                }

                InitValidated = true;
                ObservedModel = SafeToken(String(root, "model"));
                return TranslationStep.Continue;
            case "api_retry":
                // A própria CLI repete; o turno só falha pelo result. Guarda a categoria (token seguro) para diagnóstico.
                ApiRetries++;
                LastApiRetryCategory = SafeToken(String(root, "error")) ?? LastApiRetryCategory;
                return TranslationStep.Continue;
            default:
                // status, thinking_tokens, task_*, hook_* (não pedidos), permission_denied: sem evento no chat.
                return TranslationStep.Continue;
        }
    }

    /// <summary>
    /// Validação do <c>system/init</c> contra o plano do turno: sessão esperada, <c>permissionMode</c> default, nenhuma
    /// chave de API, <c>tools</c> exatamente nativas do plano ∪ <c>mcp__kapibarastudio__</c>(tools do produto ∪ aprovação
    /// quando exigida), <c>mcp_servers</c> exatamente <c>[{kapibarastudio, connected}]</c> quando o plano usa o canal (senão
    /// vazio) e versão mínima. Divergência aborta o turno fail-closed (a requisição ao modelo já saiu: o <c>init</c> só
    /// chega depois da primeira mensagem, H-21).
    /// </summary>
    private bool IsExpectedInit(JsonElement root)
    {
        var reasons = new List<string>();
        if (!string.Equals(String(root, "session_id"), expectedSessionId, StringComparison.Ordinal))
        {
            reasons.Add("SessionId");
        }

        var permissionMode = String(root, "permissionMode");
        if (!string.Equals(permissionMode, "default", StringComparison.Ordinal)) reasons.Add("PermissionMode");
        var apiKeySource = String(root, "apiKeySource");
        if (!string.Equals(apiKeySource, "none", StringComparison.Ordinal)) reasons.Add("ApiKeySource");

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            reasons.Add("ToolsFormat");
        }
        else
        {
            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.String || tool.GetString() is not { Length: > 0 } name || !names.Add(name))
                {
                    reasons.Add("ToolsFormatOrDuplicate");
                    break;
                }
            }
        }

        var missingTools = _expectedTools.Except(names, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unexpectedTools = names.Except(_expectedTools, StringComparer.Ordinal)
            .Except(_optionalTools, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missingTools.Length > 0 || unexpectedTools.Length > 0) reasons.Add("Tools");
        if (!IsExpectedMcpServers(root)) reasons.Add("McpServers");
        var versionText = String(root, "claude_code_version");
        if (!ClaudeCodeVersion.TryParse(versionText, out var version) || version < minimumVersion) reasons.Add("Version");
        var model = String(root, "model");
        if (!IsRequestedModel(model)) reasons.Add("Model");

#if DEBUG
        if (reasons.Count > 0)
        {
            var servers = root.TryGetProperty("mcp_servers", out var value) && value.ValueKind == JsonValueKind.Array ? value : default;
            var serverCount = servers.ValueKind == JsonValueKind.Array ? servers.GetArrayLength() : -1;
            var serverStatus = serverCount > 0 && servers[0].ValueKind == JsonValueKind.Object ? String(servers[0], "status") : null;
            ClaudeCodeDebugLog.InitMismatch(system!, debugLogDirectory, reasons, missingTools, unexpectedTools, names.Count,
                _expectsMcpServer, serverCount, serverStatus, permissionMode, apiKeySource, model, versionText);
        }
#endif
        return reasons.Count == 0;
    }

    /// <summary>
    /// Sem canal: ausente, nulo ou vazio. Com canal: exatamente um servidor, o do produto, conectado; servidor extra,
    /// nome diferente ou qualquer outro status (failed, pending, needs-auth) é divergência.
    /// </summary>
    private bool IsExpectedMcpServers(JsonElement root)
    {
        if (!root.TryGetProperty("mcp_servers", out var servers) || servers.ValueKind == JsonValueKind.Null)
        {
            return !_expectsMcpServer;
        }

        if (servers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (!_expectsMcpServer)
        {
            return servers.GetArrayLength() == 0;
        }

        if (servers.GetArrayLength() != 1)
        {
            return false;
        }

        var server = servers[0];
        return server.ValueKind == JsonValueKind.Object &&
            string.Equals(String(server, "name"), Application.Agents.McpServerLaunchSpec.DefaultServerName, StringComparison.Ordinal) &&
            string.Equals(String(server, "status"), "connected", StringComparison.Ordinal);
    }

    /// <summary>
    /// M4: o modelo efetivo precisa corresponder ao pedido. Alias oficial (<c>sonnet</c>/<c>opus</c>/<c>haiku</c>, com
    /// sufixo opcional como <c>[1m]</c>) exige um ID <c>claude-…</c> da mesma família; ID completo exige igualdade.
    /// Assim <c>ANTHROPIC_MODEL</c>/<c>ANTHROPIC_DEFAULT_*_MODEL</c> vindos de fora do app não trocam o modelo em silêncio.
    /// </summary>
    private bool IsRequestedModel(string? observed)
    {
        if (observed is null)
        {
            return false;
        }

        var bracket = requestedModel.IndexOf('[', StringComparison.Ordinal);
        var family = bracket < 0 ? requestedModel : requestedModel[..bracket];
        var observedBase = observed.IndexOf('[', StringComparison.Ordinal) is var cut and >= 0 ? observed[..cut] : observed;
        return family is "sonnet" or "opus" or "haiku"
            ? observedBase.StartsWith("claude-", StringComparison.Ordinal) &&
              observedBase.Split('-').Contains(family, StringComparer.Ordinal)
            : string.Equals(observedBase, family, StringComparison.Ordinal);
    }

    private TranslationStep HandleRateLimit(JsonElement root)
    {
        if (root.TryGetProperty("rate_limit_info", out var info) && info.ValueKind == JsonValueKind.Object &&
            string.Equals(String(info, "status"), "rejected", StringComparison.Ordinal))
        {
            RateLimitRejected = true;
        }

        return TranslationStep.Continue;
    }

    private TranslationStep HandleStreamEvent(JsonElement root, List<AgentProviderEvent> output)
    {
        if (IsSubagentFrame(root))
        {
            return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
        }

        if (!root.TryGetProperty("event", out var streamEvent) || streamEvent.ValueKind != JsonValueKind.Object)
        {
            return TranslationStep.Continue;
        }

        switch (String(streamEvent, "type"))
        {
            case "message_start":
                _currentMessageId = streamEvent.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                    ? SafeToken(String(message, "id")) ?? Guid.NewGuid().ToString("N")
                    : Guid.NewGuid().ToString("N");
                return TranslationStep.Continue;
            case "content_block_start":
                return StartBlock(streamEvent, output);
            case "content_block_delta":
                if (BlockKey(streamEvent) is { } deltaKey && _openText.TryGetValue(deltaKey, out var deltaId) &&
                    streamEvent.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object &&
                    string.Equals(String(delta, "type"), "text_delta", StringComparison.Ordinal) &&
                    String(delta, "text") is { Length: > 0 } text)
                {
                    output.Add(new AgentProviderEvent(AgentEventKind.MessageDelta, text, MessageId: deltaId));
                }

                return TranslationStep.Continue;
            case "content_block_stop":
                if (BlockKey(streamEvent) is { } stopKey && _openText.Remove(stopKey, out var stopId))
                {
                    output.Add(new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: stopId));
                }

                return TranslationStep.Continue;
            default:
                // message_delta/message_stop: uso vem do result; thinking/signature descartados.
                return TranslationStep.Continue;
        }
    }

    private TranslationStep StartBlock(JsonElement streamEvent, List<AgentProviderEvent> output)
    {
        if (!streamEvent.TryGetProperty("content_block", out var block) || block.ValueKind != JsonValueKind.Object)
        {
            return TranslationStep.Continue;
        }

        switch (String(block, "type"))
        {
            case "text" when BlockKey(streamEvent) is { } key && !_openText.ContainsKey(key):
                var id = AgentMessageId.New();
                _openText[key] = id;
                if (_currentMessageId is not null)
                {
                    _streamedMessages.Add(_currentMessageId);
                }

                output.Add(new AgentProviderEvent(AgentEventKind.MessageStarted, MessageId: id));
                return TranslationStep.Continue;
            case "tool_use":
                // O nome parcial já permite recusar cedo uma ferramenta fora do plano (inclui a de aprovação, que o modelo
                // nunca chama diretamente) antes que a CLI a execute. EndConversation é controle interno documentado da
                // CLI e aparece quando há MCP; não é despachado pelo produto.
                return IsHostControlTool(String(block, "name")) || IsCallableTool(String(block, "name"), out _)
                    ? TranslationStep.Continue
                    : TranslationStep.Abort(ClaudeCodeErrorCodes.ToolOutsideAllowlist);
            default:
                return TranslationStep.Continue;
        }
    }

    private TranslationStep HandleAssistant(JsonElement root, List<AgentProviderEvent> output)
    {
        if (IsSubagentFrame(root))
        {
            return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
        }

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return TranslationStep.Continue;
        }

        var nativeMessageId = SafeToken(String(message, "id"));
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (String(block, "type"))
            {
                case "tool_use":
                    if (IsHostControlTool(String(block, "name")))
                    {
                        // A CLI executa este controle internamente; não o publique como tool call do registry.
                        break;
                    }

                    if (!IsCallableTool(String(block, "name"), out var name))
                    {
                        return TranslationStep.Abort(ClaudeCodeErrorCodes.ToolOutsideAllowlist);
                    }

                    if (SafeToken(String(block, "id")) is { } nativeId && !_tools.ContainsKey(nativeId))
                    {
                        var callId = AgentToolCallId.New();
                        _tools[nativeId] = (callId, name, false);
                        // Observação visível: só o nome (leitura nativa, ou tool do produto pelo nome do registry, que
                        // o broker já executou e auditou); nunca caminho, argumentos ou resultado.
                        output.Add(new AgentProviderEvent(AgentEventKind.ToolStarted, ToolCallId: callId, ToolName: name));
                    }

                    break;
                case "text":
                    // Fallback sem mensagens parciais: texto inteiro quando nenhum bloco desta mensagem veio por stream.
                    if ((nativeMessageId is null || !_streamedMessages.Contains(nativeMessageId)) &&
                        String(block, "text") is { Length: > 0 } text)
                    {
                        var id = AgentMessageId.New();
                        output.Add(new AgentProviderEvent(AgentEventKind.MessageStarted, MessageId: id));
                        output.Add(new AgentProviderEvent(AgentEventKind.MessageDelta, text, MessageId: id));
                        output.Add(new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: id));
                    }

                    break;
            }
        }

        return TranslationStep.Continue;
    }

    private TranslationStep HandleUser(JsonElement root, List<AgentProviderEvent> output)
    {
        if (IsSubagentFrame(root))
        {
            return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
        }

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return TranslationStep.Continue;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !string.Equals(String(block, "type"), "tool_result", StringComparison.Ordinal) ||
                SafeToken(String(block, "tool_use_id")) is not { } nativeId || !_tools.TryGetValue(nativeId, out var tool) || tool.Terminal)
            {
                continue;
            }

            _tools[nativeId] = tool with { Terminal = true };
            var failed = block.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;
            // O conteúdo lido nunca é copiado: só o estado da chamada.
            output.Add(failed
                ? new AgentProviderEvent(AgentEventKind.ToolFailed, ClaudeCodeErrorCodes.NativeToolFailed, ToolCallId: tool.Id, ToolName: tool.Name)
                : new AgentProviderEvent(AgentEventKind.ToolCompleted, ToolCallId: tool.Id, ToolName: tool.Name));
        }

        return TranslationStep.Continue;
    }

    private TranslationStep HandleResult(JsonElement root, List<AgentProviderEvent> output)
    {
        // L2: result sem session_id ou de outra sessão é violação.
        if (!string.Equals(String(root, "session_id"), expectedSessionId, StringComparison.Ordinal))
        {
            return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
        }

        // L1: sucesso sem init validado significa que nada do que foi verificado (tools, chave, modo) vale para o turno.
        // Erros sem init (ex.: --resume de sessão inexistente, que falha antes do modelo) continuam classificados.
        var isErrorResult = root.TryGetProperty("is_error", out var errorFlag) && errorFlag.ValueKind == JsonValueKind.True;
        if (!InitValidated && !isErrorResult)
        {
            return TranslationStep.Abort(ClaudeCodeErrorCodes.ProtocolViolation);
        }

        CloseOpenMessages(output);
        var sessionNotFound = false;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                sessionNotFound |= error.ValueKind == JsonValueKind.String &&
                    error.GetString()!.Contains(NoConversationMarker, StringComparison.Ordinal);
            }
        }

        long? input = null;
        long? outputTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            input = Int64(usage, "input_tokens");
            outputTokens = Int64(usage, "output_tokens");
        }

        var denials = root.TryGetProperty("permission_denials", out var denied) && denied.ValueKind == JsonValueKind.Array
            ? denied.GetArrayLength()
            : 0;
        Result = new ClaudeCodeResultInfo(
            root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True,
            SafeToken(String(root, "subtype")),
            Int32(root, "num_turns"),
            root.TryGetProperty("total_cost_usd", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.TryGetDouble(out var value) ? value : null,
            input,
            outputTokens,
            denials,
            SafeToken(String(root, "terminal_reason")),
            Int32(root, "api_error_status"),
            sessionNotFound);
        return new TranslationStep(TranslationKind.Result);
    }

    /// <summary>Ferramenta que o modelo pode chamar neste turno; <paramref name="displayName"/> é o nome publicado.</summary>
    private bool IsCallableTool(string? name, out string displayName)
    {
        displayName = string.Empty;
        if (name is null || !_callableTools.TryGetValue(name, out var mapped))
        {
            return false;
        }

        displayName = mapped;
        return true;
    }

    private bool IsHostControlTool(string? name) => _expectsMcpServer &&
        string.Equals(name, "EndConversation", StringComparison.Ordinal);

    /// <summary>Chamadas de tools do produto via MCP observadas neste turno (diagnóstico; nomes nunca persistidos).</summary>
    public int ProductToolCalls => _tools.Values.Count(static tool => !IsNativeTool(tool.Name));

    private static bool IsNativeTool(string name) =>
        ClaudeCodeAgentProviderOptions.NativeToolAllowlist.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Quadro de subagente (<c>parent_tool_use_id</c> preenchido). Agent/Task estão fora de <c>--tools</c>, então qualquer
    /// quadro assim é violação de contrato e aborta o turno (revisão M2).
    /// </summary>
    private static bool IsSubagentFrame(JsonElement root) =>
        root.TryGetProperty("parent_tool_use_id", out var parent) && parent.ValueKind == JsonValueKind.String;

    private string? BlockKey(JsonElement streamEvent) =>
        streamEvent.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var value)
            ? (_currentMessageId ?? string.Empty) + ":" + value.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Int64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) &&
        number is >= 0 and <= int.MaxValue * 1024L
            ? number
            : null;

    private static int? Int32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0
            ? number
            : null;

    private static string? SafeToken(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':')
            ? value
            : null;
}
