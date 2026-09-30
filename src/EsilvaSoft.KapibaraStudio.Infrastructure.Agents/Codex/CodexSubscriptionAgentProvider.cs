using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;

/// <summary>Experimental Codex subscription adapter. It never uses an OpenAI API key or API transport.</summary>
public sealed class CodexSubscriptionAgentProvider : IAgentProvider, IAsyncDisposable
{
    public const string Id = "codex-subscription";
    public const string DisplayName = "Codex — assinatura ChatGPT";
    private readonly CodexSubscriptionAgentProviderOptions _options;
    private readonly IAgentToolRegistry? _tools;
    private readonly IAgentWorkspaceDirectoryProbe _directories;
    private readonly Func<CancellationToken, Task<ICodexAppServerConnection>> _connect;
    private readonly SemaphoreSlim _accountGate = new(1, 1);
    private int _disposed;

    public CodexSubscriptionAgentProvider(CodexSubscriptionAgentProviderOptions options,
        ICodexAppServerProcessLauncher processLauncher, IAgentWorkspaceDirectoryProbe directories,
        IAgentToolRegistry? tools = null)
        : this(options, tools, token => StartAsync(options, processLauncher, token), directories)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
    }

    internal CodexSubscriptionAgentProvider(CodexSubscriptionAgentProviderOptions options, IAgentToolRegistry? tools,
        Func<CancellationToken, Task<ICodexAppServerConnection>> connect, IAgentWorkspaceDirectoryProbe directories)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = tools;
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public string ProviderId => Id;
    public bool IsLocal => false;
    public AgentProviderDescriptor Describe() => new(Id, DisplayName,
        [AgentAuthenticationMethod.OfficialAppServerDelegated], Capabilities);

    // Experimental App Server: these are contract-tested capabilities, not production-homologated claims.
    internal static AgentProviderCapabilities Capabilities { get; } = new()
    {
        Chat = true, Streaming = true, ToolCalling = true, Sessions = true, ModelSelection = true,
        TurnPlan = true, UsesNetwork = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
    };

    /// <summary>Checks only the Codex-managed ChatGPT account; it never returns identity or credential material.</summary>
    public async Task<CodexSubscriptionStatus> GetSubscriptionStatusAsync(CancellationToken cancellationToken = default)
    {
        await _accountGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var flow = await CreateAccountFlowAsync(cancellationToken).ConfigureAwait(false);
            return await flow.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _accountGate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _accountGate.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Runs the official Codex browser login; the callback receives only a validated OpenAI HTTPS URL.</summary>
    public async Task<bool> LoginAsync(Func<Uri, CancellationToken, Task> openBrowser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);
        await _accountGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var flow = await CreateAccountFlowAsync(cancellationToken).ConfigureAwait(false);
            var login = await flow.BeginLoginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await openBrowser(login.AuthorizationUrl, cancellationToken).ConfigureAwait(false);
                return await flow.WaitForLoginAsync(login.LoginId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try { await flow.CancelLoginAsync(login.LoginId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
        }
        finally { _accountGate.Release(); }
    }

    /// <summary>Performs a Codex-managed global logout after an explicit user confirmation.</summary>
    public async Task LogoutAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        await _accountGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var flow = await CreateAccountFlowAsync(cancellationToken).ConfigureAwait(false);
            await flow.LogoutAsync(confirmed, cancellationToken).ConfigureAwait(false);
        }
        finally { _accountGate.Release(); }
    }

    private async Task<CodexAppServerAccountFlow> CreateAccountFlowAsync(CancellationToken cancellationToken)
    {
        var connection = await _connect(cancellationToken).ConfigureAwait(false);
        try { return await CodexAppServerAccountFlow.InitializeAsync(connection, cancellationToken).ConfigureAwait(false); }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connect(cancellationToken).ConfigureAwait(false);
            await InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
            var account = await ReadAccountAsync(connection, cancellationToken).ConfigureAwait(false);
            if (account is null)
                return new(false, AgentProviderAuthState.NotConfigured, AgentProviderCapabilities.None with { UsesNetwork = true }, unavailableCode: "ChatGptAccountRequired");
            var models = await ReadModelsAsync(connection, cancellationToken).ConfigureAwait(false);
            return new(true, AgentProviderAuthState.Configured, Capabilities, models, models.Count > 0 ? models[0] : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(false, AgentProviderAuthState.Unknown, AgentProviderCapabilities.None with { UsesNetwork = true }, unavailableCode: "CodexAppServerUnavailable");
        }
    }

    public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(options.ProviderId, Id, StringComparison.Ordinal))
            throw new ArgumentException("A sessão não pertence ao provider Codex por assinatura.", nameof(options));
        var connection = await _connect(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
            if (await ReadAccountAsync(connection, cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("Uma conta ChatGPT autenticada é necessária para usar o Codex por assinatura.");
            var models = await ReadModelsAsync(connection, cancellationToken).ConfigureAwait(false);
            var model = string.IsNullOrWhiteSpace(options.ModelId) ? (models.Count > 0 ? models[0] : null) : options.ModelId;
            if (model is null || !models.Contains(model, StringComparer.Ordinal))
                throw new InvalidOperationException("O modelo Codex selecionado não está disponível para a conta ChatGPT.");
            var session = new CodexSubscriptionAgentSession(connection, _tools, model, options, _directories);
            connection = null!;
            return session;
        }
        finally
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task InitializeAsync(ICodexAppServerConnection connection, CancellationToken token)
    {
        var result = await connection.RequestAsync("initialize", new
        {
            clientInfo = new { name = "kapibarastudio", title = "EsilvaSoft.KapibaraStudio", version = "0.11.0" },
            capabilities = new { experimentalApi = true },
        }, cancellationToken: token).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("userAgent", out _))
            throw new InvalidDataException("Codex App Server initialization response failed validation.");
        await connection.NotifyAsync("initialized", new { }, token).ConfigureAwait(false);
    }

    internal static async Task<CodexChatGptAccount?> ReadAccountAsync(ICodexAppServerConnection connection, CancellationToken token)
    {
        var bytes = CodexAccountProtocol.BuildAccountReadRequest(1);
        using var request = JsonDocument.Parse(bytes);
        var result = await connection.RequestAsync(CodexAccountProtocol.AccountReadMethod,
            request.RootElement.GetProperty("params"), cancellationToken: token).ConfigureAwait(false);
        var response = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, result });
        return CodexAccountProtocol.TryParseAccountReadResponse(response, out var account) ? account : null;
    }

    private static async Task<IReadOnlyList<string>> ReadModelsAsync(ICodexAppServerConnection connection, CancellationToken token)
    {
        var result = await connection.RequestAsync("model/list", new { }, cancellationToken: token).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Codex model list failed validation.");
        var models = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("model", out var model) &&
                model.ValueKind == JsonValueKind.String && model.GetString() is { Length: > 0 and <= 128 } id &&
                !id.Any(char.IsControl) && !models.Contains(id, StringComparer.Ordinal)) models.Add(id);
        }
        if (models.Count == 0) throw new InvalidDataException("Codex returned no usable subscription models.");
        return models;
    }

    private static Task<ICodexAppServerConnection> StartAsync(CodexSubscriptionAgentProviderOptions options,
        ICodexAppServerProcessLauncher processLauncher, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<ICodexAppServerConnection>(new CodexAppServerConnection(
            CodexAppServerJsonRpcTransport.Start(processLauncher, options.ExecutablePath,
                options.WorkingDirectory, options.CodexHome)));
    }
}

public sealed record CodexSubscriptionAgentProviderOptions(string CodexHome, string? ExecutablePath = null,
    string? WorkingDirectory = null);

internal interface ICodexAppServerConnection : IAsyncDisposable
{
    IAsyncEnumerable<CodexAppServerInboundMessage> Messages { get; }
    Task<JsonElement> RequestAsync(string method, object? parameters = null, CancellationToken cancellationToken = default);
    Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default);
    Task RespondAsync(JsonElement id, object? result, CancellationToken cancellationToken = default);
    Task RespondErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken = default);
}

internal sealed class CodexAppServerConnection(CodexAppServerJsonRpcTransport transport) : ICodexAppServerConnection
{
    public IAsyncEnumerable<CodexAppServerInboundMessage> Messages => transport.Messages.ReadAllAsync();
    public Task<JsonElement> RequestAsync(string method, object? parameters = null, CancellationToken cancellationToken = default) => transport.RequestAsync(method, parameters, cancellationToken: cancellationToken);
    public Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default) => transport.NotifyAsync(method, parameters, cancellationToken);
    public Task RespondAsync(JsonElement id, object? result, CancellationToken cancellationToken = default) => transport.RespondAsync(id, result, cancellationToken);
    public Task RespondErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken = default) => transport.RespondErrorAsync(id, code, message, cancellationToken);
    public ValueTask DisposeAsync() => transport.DisposeAsync();
}

internal sealed class CodexSubscriptionAgentSession(ICodexAppServerConnection connection, IAgentToolRegistry? registry,
    string model, AgentSessionOptions options, IAgentWorkspaceDirectoryProbe directories) : IAgentSession
{
    private readonly Lock _gate = new();
    private readonly Dictionary<AgentToolCallId, (JsonElement RequestId, string CallId)> _pendingTools = [];
    private string? _threadId;
    private string? _providerTurnId;
    private string? _toolCatalogFingerprint;
    private AgentTurnId? _activeTurn;
    private bool _disposed;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request is null || !request.TurnId.IsValid) throw new ArgumentException("Turno inválido.", nameof(request));
        if (request.Plan is null || request.Plan.IsBlocked || request.Plan.NativeTools.Count != 0 ||
            request.Plan.ProductTools is null || request.Plan.ProductTools.Any(n => !IsSafeTool(n, request.Plan)) ||
            (request.Plan.ProductTools.Count > 0 && request.Permissions is null))
        {
            yield return new(AgentEventKind.AgentError, "CodexPolicyUnsupported"); yield break;
        }
        if (await CodexSubscriptionAgentProvider.ReadAccountAsync(connection, cancellationToken).ConfigureAwait(false) is null)
        { yield return new(AgentEventKind.AgentError, "ChatGptAccountRequired"); yield break; }
        if (string.IsNullOrWhiteSpace(request.UserMessage) || request.UserMessage.Length > 64_000)
        { yield return new(AgentEventKind.AgentError, "InputInvalid"); yield break; }

        lock (_gate) { if (_disposed || _activeTurn is not null) throw new InvalidOperationException("Codex session is unavailable."); _activeTurn = request.TurnId; }
        try
        {
            var dynamicTools = BuildTools(request.Plan);
            var catalogFingerprint = FingerprintToolCatalog(dynamicTools);
            var workspace = request.WorkspaceContext?.WorkspaceFolder;
            if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace) || !directories.Exists(workspace))
            {
                yield return new(AgentEventKind.AgentError, "CodexWorkspaceRequired");
                yield break;
            }
            var catalogChangedInSession = _threadId is not null &&
                !string.Equals(_toolCatalogFingerprint, catalogFingerprint, StringComparison.Ordinal);
            if (catalogChangedInSession)
            {
                // dynamicTools is bound when a Codex thread starts. There is no verified in-thread update API;
                // never let a permission/schema change continue against a stale model-visible catalog.
                NotifyResumeFallback("CodexToolPolicyChanged");
                _threadId = null;
                _toolCatalogFingerprint = null;
            }

            if (_threadId is null)
            {
                var resumeId = catalogChangedInSession ? null : options.ResumeProviderSessionId;
                var canResume = TryParseResumeHandle(resumeId, out var resumeThreadId, out var previousFingerprint) &&
                    string.Equals(previousFingerprint, catalogFingerprint, StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(resumeId) && !canResume)
                {
                    // Legacy raw IDs and IDs with a changed tool catalog carry no proof that the saved model-visible
                    // tools match today's permissions. Start a clean thread and make the lost context visible.
                    NotifyResumeFallback(TryParseResumeHandle(resumeId, out _, out _) ?
                        "CodexToolPolicyChanged" : "CodexSessionPolicyUnknown");
                }
                if (canResume)
                {
                    try
                    {
                        var resumed = await connection.RequestAsync("thread/resume", new
                        {
                            threadId = resumeThreadId, model, cwd = workspace,
                            approvalPolicy = "on-request", sandbox = "readOnly",
                        }, cancellationToken).ConfigureAwait(false);
                        _threadId = RequiredId(resumed, "thread", "id");
                    }
                    catch (CodexAppServerRpcException)
                    {
                        NotifyResumeFallback("CodexSessionNotFound");
                        _threadId = await StartThreadAsync(model, workspace, dynamicTools, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    _threadId = await StartThreadAsync(model, workspace, dynamicTools, cancellationToken).ConfigureAwait(false);
                }
                _toolCatalogFingerprint = catalogFingerprint;
                NotifyEstablished(_threadId, catalogFingerprint);
            }

            var turnStart = await connection.RequestAsync("turn/start", new
            {
                threadId = _threadId,
                cwd = workspace,
                model,
                input = new[] { new { type = "text", text = ComposePrompt(request) } },
                sandboxPolicy = new
                {
                    type = "readOnly",
                    networkAccess = false,
                    access = new { type = "restricted", includePlatformDefaults = false, readableRoots = new[] { workspace } },
                },
                approvalPolicy = "on-request",
            }, cancellationToken).ConfigureAwait(false);
            _providerTurnId = RequiredId(turnStart, "turn", "id");

            var messageId = AgentMessageId.New();
            yield return new(AgentEventKind.MessageStarted, MessageId: messageId);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Timeout);
            await foreach (var message in connection.Messages.WithCancellation(deadline.Token).ConfigureAwait(false))
            {
                if (message.Method == "item/tool/call" && message.Id is { } dynamicToolRequestId)
                {
                    var toolEvent = QueueToolCall(message, dynamicToolRequestId, request);
                    if (toolEvent is null)
                        await connection.RespondErrorAsync(dynamicToolRequestId, -32602, "Tool call rejected by local policy.", deadline.Token).ConfigureAwait(false);
                    else yield return toolEvent;
                }
                else if (message.Id is { } unexpectedServerRequestId)
                {
                    await connection.RespondErrorAsync(unexpectedServerRequestId, -32601,
                        "Codex native requests are disabled by this integration.", deadline.Token).ConfigureAwait(false);
                    await InterruptProviderTurnAsync(deadline.Token).ConfigureAwait(false);
                    yield return new(AgentEventKind.AgentError, "CodexNativeToolBlocked");
                    yield break;
                }
                else if (message.Method == "item/started" && IsCurrentTurnEvent(message.Parameters) &&
                    IsUnsupportedNativeWork(message.Parameters))
                {
                    await InterruptProviderTurnAsync(deadline.Token).ConfigureAwait(false);
                    yield return new(AgentEventKind.AgentError, "CodexNativeToolBlocked");
                    yield break;
                }
                else if (message.Method == "item/agentMessage/delta" && IsCurrentTurnEvent(message.Parameters) &&
                    message.Parameters.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                    yield return new(AgentEventKind.MessageDelta, delta.GetString(), MessageId: messageId);
                else if (message.Method == "turn/completed" && IsCurrentTurnEvent(message.Parameters))
                {
                    if (message.Parameters.TryGetProperty("turn", out var turn) && turn.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "completed")
                    { yield return new(AgentEventKind.MessageCompleted, MessageId: messageId); yield return new(AgentEventKind.SessionCompleted); }
                    else yield return new(AgentEventKind.AgentError, "CodexTurnFailed");
                    yield break;
                }
            }
            yield return new(AgentEventKind.AgentError, "CodexStreamEnded");
        }
        finally
        {
            lock (_gate)
            {
                _pendingTools.Clear();
                _providerTurnId = null;
                _activeTurn = null;
            }
        }
    }

    public async Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
    {
        (JsonElement RequestId, string CallId) pending;
        lock (_gate)
        {
            if (_activeTurn != result.TurnId || !_pendingTools.Remove(result.ToolCallId, out pending))
                throw new InvalidOperationException("Chamada de tool desconhecida ou expirada.");
        }
        var content = result.Data is { Length: > 0 } data ? new[] { new { type = "inputText", text = data } } : Array.Empty<object>();
        await connection.RespondAsync(pending.RequestId, new { contentItems = content, success = result.Status == AgentToolResultStatus.Succeeded }, cancellationToken).ConfigureAwait(false);
    }

    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("Codex não solicita aprovações do runtime; escritas estão bloqueadas neste experimento."));

    public async Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        if (_activeTurn != turnId || _threadId is null || _providerTurnId is null) return;
        try { await connection.RequestAsync("turn/interrupt", new { threadId = _threadId, turnId = _providerTurnId }, cancellationToken).ConfigureAwait(false); }
        catch (Exception) { /* status do cancelamento será OutcomeUnknown */ }
    }

    public AgentTurnCancellationReport GetCancellationReport(AgentTurnId turnId) =>
        _activeTurn == turnId ? AgentTurnCancellationReport.MayHaveTakenEffect : AgentTurnCancellationReport.NotReported;

    public async ValueTask DisposeAsync() { lock (_gate) { if (_disposed) return; _disposed = true; } await connection.DisposeAsync().ConfigureAwait(false); }

    private AgentProviderEvent? QueueToolCall(CodexAppServerInboundMessage message, JsonElement requestId, AgentTurnRequest request)
    {
        if (message.Parameters.ValueKind != JsonValueKind.Object ||
            !message.Parameters.TryGetProperty("tool", out var tool) || tool.ValueKind != JsonValueKind.String || tool.GetString() is not { } name ||
            !request.Plan!.ProductTools.Contains(name, StringComparer.Ordinal) || !IsSafeTool(name, request.Plan) ||
            !message.Parameters.TryGetProperty("arguments", out var args) ||
            !message.Parameters.TryGetProperty("threadId", out var thread) || thread.ValueKind != JsonValueKind.String || thread.GetString() != _threadId ||
            !message.Parameters.TryGetProperty("turnId", out var turn) || turn.ValueKind != JsonValueKind.String || turn.GetString() != _providerTurnId ||
            !message.Parameters.TryGetProperty("callId", out var rawCallId) || rawCallId.ValueKind != JsonValueKind.String ||
            rawCallId.GetString() is not { Length: > 0 and <= 256 } callIdText || callIdText.Any(char.IsControl)) return null;
        var callId = AgentToolCallId.New();
        lock (_gate) _pendingTools.Add(callId, (requestId.Clone(), callIdText));
        return new(AgentEventKind.ToolRequested, ToolCallId: callId, ToolName: name,
            ArgumentsJson: args.ValueKind == JsonValueKind.String ? args.GetString() : args.GetRawText());
    }

    private object[] BuildTools(AgentTurnPlan plan)
    {
        if (plan.ProductTools.Count == 0) return [];
        if (registry is null) throw new InvalidOperationException("Codex product tool registry is unavailable.");
        var result = new List<object>();
        if (plan.ProductTools.Distinct(StringComparer.Ordinal).Count() != plan.ProductTools.Count)
            throw new InvalidDataException("Codex turn plan contains duplicate product tools.");
        foreach (var name in plan.ProductTools)
        {
            if (!IsSafeTool(name, plan) || registry.FindDescriptor(name) is not { } descriptor)
                throw new InvalidDataException("Codex turn plan references an unavailable product tool.");
            var schemaJson = registry.GetInputSchemaJson(name);
            if (schemaJson is null) throw new InvalidDataException("Codex product tool schema is unavailable.");
            try
            {
                using var schema = JsonDocument.Parse(schemaJson, new JsonDocumentOptions { MaxDepth = 16 });
                if (schema.RootElement.ValueKind != JsonValueKind.Object || !schema.RootElement.TryGetProperty("type", out var type) || type.GetString() != "object" ||
                    !schema.RootElement.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
                    throw new InvalidDataException("Codex product tool schema failed validation.");
                result.Add(new { name, description = $"{descriptor.Name} (KapibaraStudio registry)", inputSchema = schema.RootElement.Clone() });
            }
            catch (JsonException) { throw new InvalidDataException("Codex product tool schema is invalid."); }
        }
        return [.. result];
    }

    private bool IsSafeTool(string name, AgentTurnPlan plan) => registry?.FindDescriptor(name) is { Risk: AgentToolRisk.ReadOnly } &&
        plan.ProductTools.Contains(name, StringComparer.Ordinal);

    private static string RequiredId(JsonElement root, string parent, string property) => root.TryGetProperty(parent, out var nested) && nested.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 256 } id && !id.Any(char.IsControl) ? id : throw new InvalidDataException("Codex returned an invalid session ID.");

    private bool IsCurrentTurnEvent(JsonElement parameters) => parameters.ValueKind == JsonValueKind.Object &&
        parameters.TryGetProperty("threadId", out var thread) && thread.ValueKind == JsonValueKind.String && thread.GetString() == _threadId &&
        parameters.TryGetProperty("turnId", out var turn) && turn.ValueKind == JsonValueKind.String && turn.GetString() == _providerTurnId;

    private static bool IsUnsupportedNativeWork(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) return true;
        if (!item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return true;
        // Unknown and native items fail closed. Dynamic tools are the only delegated operation supported here.
        return type.GetString() is not ("agentMessage" or "reasoning" or "dynamicToolCall");
    }

    private async Task InterruptProviderTurnAsync(CancellationToken cancellationToken)
    {
        if (_threadId is null || _providerTurnId is null) return;
        try
        {
            await connection.RequestAsync("turn/interrupt", new { threadId = _threadId, turnId = _providerTurnId }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static bool IsSafeProviderId(string value) => value.Length is > 0 and <= 256 &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private async Task<string> StartThreadAsync(string modelId, string workspace, object[] dynamicTools,
        CancellationToken cancellationToken)
    {
        var started = await connection.RequestAsync("thread/start", new
        {
            model = modelId, cwd = workspace, approvalPolicy = "on-request", sandbox = "readOnly", dynamicTools,
        }, cancellationToken).ConfigureAwait(false);
        return RequiredId(started, "thread", "id");
    }

    private void NotifyResumeFallback(string noticeCode)
    {
        try
        {
            options.ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(options.ConversationId,
                AgentProviderSessionChange.ResumeFallback, null, noticeCode));
        }
        catch (Exception) { }
    }

    private void NotifyEstablished(string threadId, string fingerprint)
    {
        var handle = CreateResumeHandle(threadId, fingerprint);
        try { options.ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(options.ConversationId,
            AgentProviderSessionChange.Established, handle)); }
        catch (Exception) { }
    }

    private static string FingerprintToolCatalog(object[] dynamicTools)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dynamicTools);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string CreateResumeHandle(string threadId, string fingerprint)
    {
        if (!IsSafeProviderId(threadId) || fingerprint.Length != 64 || fingerprint.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Codex resume metadata failed validation.");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(threadId)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var handle = $"cs1.{encoded}.{fingerprint}";
        if (handle.Length > 256) throw new InvalidDataException("Codex resume metadata exceeds its storage limit.");
        return handle;
    }

    private static bool TryParseResumeHandle(string? value, out string threadId, out string fingerprint)
    {
        threadId = string.Empty;
        fingerprint = string.Empty;
        if (value is not { Length: > 0 and <= 256 }) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || parts[0] != "cs1" || parts[1].Length is < 1 or > 344 ||
            parts[2].Length != 64 || parts[2].Any(static c => !char.IsAsciiHexDigit(c))) return false;
        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            if (!IsSafeProviderId(decoded)) return false;
            threadId = decoded;
            fingerprint = parts[2].ToLowerInvariant();
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static string ComposePrompt(AgentTurnRequest request)
    {
        var builder = new System.Text.StringBuilder("KapibaraStudio experimental Codex subscription integration. Use only declared KapibaraStudio registry tools; do not use built-in file, shell, network, or collaboration tools.\n");
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) builder.AppendLine(request.SystemPrompt);
        if (!string.IsNullOrWhiteSpace(request.AuthorizedContext)) builder.AppendLine(request.AuthorizedContext);
        foreach (var attachment in request.Attachments)
        {
            builder.Append("\n<attachment kind=\"").Append(attachment.Kind).Append("\" name=\"")
                .Append(attachment.DisplayName.Replace("\"", "'", StringComparison.Ordinal)).AppendLine("\">");
            builder.AppendLine(attachment.Content);
            builder.AppendLine("</attachment>");
        }
        builder.AppendLine().Append(request.UserMessage);
        return builder.ToString();
    }

}
