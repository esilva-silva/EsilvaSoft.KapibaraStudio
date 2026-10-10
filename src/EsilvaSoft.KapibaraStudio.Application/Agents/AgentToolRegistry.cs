using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using System.Buffers;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Default-deny internal executable tool registry.</summary>
public sealed partial class AgentToolRegistry : IAgentToolRegistry
{
    public const string ListConnectionsToolName = "list_connections";
    public const string ListDatabasesToolName = "list_databases";
    public const string ListCollectionsToolName = "list_collections";
    public const string GetCollectionSchemaToolName = "get_collection_schema";
    public const string MongoFindToolName = "mongo_find";
    public const string MongoCountToolName = "mongo_count";
    public const string SampleDocumentsToolName = "sample_documents";
    public const string MongoFindOneToolName = "mongo_find_one";
    public const string GetDocumentToolName = "get_document";
    public const string MongoDistinctToolName = "mongo_distinct";
    public const string GetIndexesToolName = "get_indexes";
    public const string GetSearchIndexesToolName = "get_search_indexes";
    public const string CreateWorkspaceFileToolName = "create_workspace_file";
    public const string GetQueryResultsToolName = "get_query_results";
    public const string GetQueryDiagnosticsToolName = "get_query_diagnostics";
    public const string MongoExplainToolName = "mongo_explain";

    /// <summary>Per-session tool: reads the autocomplete schema cache only; never samples the database.</summary>
    public const string GetCachedSchemaToolName = "get_cached_schema";

    /// <summary>Per-session tool: workspace folder, active file and the tab's connection › database › collection.</summary>
    public const string GetWorkspaceContextToolName = "get_workspace_context";

    /// <summary>Per-session tool: registers an edit proposal for review; never writes to disk.</summary>
    public const string ProposeFileEditToolName = "propose_file_edit";

    /// <summary>
    /// Per-session permission-prompt tool of the Claude Code CLI (<c>--permission-prompt-tool mcp__kapibarastudio__approve</c>).
    /// Answers only from a human decision; never "always".
    /// </summary>
    public const string ApproveToolName = "approve";
    private const int MaximumInputBytes = 64 * 1024;
    private const int MaximumConnections = 200;
    private const string PermissionDenied = "PermissionDenied";
    private const string InvalidArguments = "InvalidArguments";
    private const string ConfirmationRejected = "ConfirmationRejected";
    private const string ConfirmationExpired = "ConfirmationExpired";
    private const string ConfirmationUnavailable = "ConfirmationUnavailable";
    private const string UnknownTool = "UnknownTool";
    private const string ResultTooLarge = "ResultTooLarge";
    private const string DeadlineExceeded = "DeadlineExceeded";
    private const string Busy = "Busy";
    private const string ToolCallLimitExceeded = "ToolCallLimitExceeded";

    /// <summary>
    /// Concurrent calls admitted per invocation session. For MCP the session is the enrolled channel, so every proxy
    /// connection of one channel shares these slots; ingress limits above this value only produce <c>Busy</c>.
    /// </summary>
    public const int MaximumConcurrentCallsPerSession = AgentToolInvocationQuota.MaximumPerSession;

    private const int MaximumOutputBytes = 256 * 1024;
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumExecutionTimeout = TimeSpan.FromSeconds(30);

    private const string ListConnectionsInputSchema = """
        {"type":"object","properties":{},"additionalProperties":false}
        """;

    private const string ListConnectionsOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["connections","truncated"],"properties":{"connections":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","name","readOnly"],"properties":{"id":{"type":"string","format":"uuid"},"name":{"type":"string"},"readOnly":{"type":"boolean"}}}},"truncated":{"type":"boolean"}}}
        """;

    private const string ListDatabasesInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId"],"properties":{"connectionId":{"type":"string","format":"uuid"},"skip":{"type":"integer","minimum":0,"maximum":10000}}}
        """;
    private const string ListCollectionsInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"skip":{"type":"integer","minimum":0,"maximum":10000}}}
        """;
    private const string NamesOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["names","truncated"],"properties":{"names":{"type":"array","maxItems":200,"items":{"type":"string"}},"truncated":{"type":"boolean"}}}
        """;
    private const string GetIndexesInputSchema = """
        {"type":"object","additionalProperties":false,"required":["connectionId","database","collection"],"properties":{"connectionId":{"type":"string","format":"uuid"},"database":{"type":"string","minLength":1,"maxLength":255},"collection":{"type":"string","minLength":1,"maxLength":255}}}
        """;
    // v2 (ADR-056): adds the direction/kind of each key field, the TTL and the field paths referenced by a partial
    // filter. Filter values never leave the index source.
    private const string GetIndexesOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["indexes","truncated"],"properties":{"indexes":{"type":"array","maxItems":200,"items":{"type":"object","additionalProperties":false,"required":["name","keyFields","unique","sparse","hidden"],"properties":{"name":{"type":"string"},"keyFields":{"type":"array","items":{"type":"string"}},"keyDirections":{"type":"array","items":{"type":"string"}},"unique":{"type":"boolean"},"sparse":{"type":"boolean"},"hidden":{"type":"boolean"},"ttlSeconds":{"type":"integer","minimum":0},"partialFilterFields":{"type":"array","maxItems":32,"items":{"type":"string"}}}}},"truncated":{"type":"boolean"},"truncationReason":{"type":"string","const":"OutputLimit"}}}
        """;

    private static readonly ReadOnlyCollection<AgentToolDescriptor> Descriptors = Array.AsReadOnly(
        [new AgentToolDescriptor(ListConnectionsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ListDatabasesToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ListCollectionsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(GetIndexesToolName, 2, AgentToolRisk.ReadOnly,
             [AgentPermission.ReadMetadata]),
         // Per-session tools (ADR-056): exposed only to the principal of a per-session channel and only when its turn
         // plan names them. None of them writes to MongoDB or to disk, so they are ReadOnly for the MongoDB risk model.
         // The workspace/proposal/confirmation tools touch no namespace; ReadMetadata is their audit classification.
         new AgentToolDescriptor(GetCachedSchemaToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadSchema]),
         new AgentToolDescriptor(GetWorkspaceContextToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ProposeFileEditToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(ApproveToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
         new AgentToolDescriptor(CreateWorkspaceFileToolName, 1, AgentToolRisk.Write, [AgentPermission.CreateWorkspaceFiles]),
         new AgentToolDescriptor(GetQueryResultsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(GetQueryDiagnosticsToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadDiagnostics, AgentPermission.ReadDocuments]),
         new AgentToolDescriptor(GetSearchIndexesToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata])]);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionProfileRepository _profiles;
    private readonly IAgentAuthorizationPolicyProvider _policies;
    private readonly IAgentPermissionEvaluator _permissions;
    private readonly IAgentAuditRepository _audit;
    private readonly IMongoMetadataSource? _metadata;
    private readonly IAgentMongoIndexSource? _indexes;
    private readonly AgentToolInvocationQuota _quota = new();
    private readonly AsyncLocal<AgentToolInvocationQuota.Lease?> _activeQuotaLease = new();
    private readonly TimeSpan _executionTimeout;
    private readonly AgentToolExposure _exposure;
    private readonly IAgentPrincipalAuthority? _principalAuthority;
    private readonly AgentSessionToolPorts? _sessionTools;
    private readonly AgentToolExposure _inProcessExposure;
    private readonly AgentToolExposure _copilotExposure;
    private readonly AgentToolExposure _claudeExposure;

    public AgentToolRegistry(
        IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies,
        IAgentPermissionEvaluator permissions,
        IAgentAuditRepository audit,
        TimeSpan? executionTimeout = null,
        IMongoMetadataSource? metadata = null,
        IAgentSchemaSamplingConsentProvider? schemaSamplingConsent = null,
        IAgentMongoFindSource? find = null,
        IAgentMongoCountSource? count = null,
        IAgentMongoDistinctSource? distinct = null,
        IAgentMongoIndexSource? indexes = null,
        IAgentMongoExplainSource? explain = null,
        AgentToolExposure? exposure = null,
        IAgentPrincipalAuthority? principalAuthority = null,
        IAgentMongoWriteSource? write = null,
        IAgentWriteApprovalAuthority? writeApprovals = null,
        AgentSessionToolPorts? sessionTools = null,
        AgentToolExposure? inProcessExposure = null,
        AgentToolExposure? copilotExposure = null,
        AgentToolExposure? claudeExposure = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _metadata = metadata;
        _indexes = indexes;
        // Legacy source/approval parameters remain source-compatible for test and migration callers only.
        // They are deliberately neither stored nor executed; removed tool names have no catalog entry.
        // Closed by default: a registry exposes nothing until its composition names an approved stage.
        _exposure = exposure ?? AgentToolExposure.None;
        _principalAuthority = principalAuthority;
        if (sessionTools is not null && (sessionTools.SessionScopes is null ||
            sessionTools.ApprovalTimeout <= TimeSpan.Zero ||
            sessionTools.ApprovalTimeout > AgentWriteApprovalCoordinator.DefaultApprovalTimeout))
            throw new ArgumentException("Portas das tools de sessão inválidas.", nameof(sessionTools));
        _sessionTools = sessionTools;
        // In-process providers announce only what this (narrower) gate releases; omitted = the registry exposure.
        _inProcessExposure = inProcessExposure ?? _exposure;
        _copilotExposure = copilotExposure ?? _inProcessExposure;
        _claudeExposure = claudeExposure ?? _exposure;
        var requestedTimeout = executionTimeout ?? DefaultExecutionTimeout;
        if (requestedTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        _executionTimeout = requestedTimeout > MaximumExecutionTimeout ? MaximumExecutionTimeout : requestedTimeout;
    }

    /// <summary>Stage released for this instance. Unreleased tools behave as unknown tools.</summary>
    public AgentToolExposureStage ExposureStage => _exposure.Stage;

    /// <summary>
    /// Tools announced to in-process providers. Provider-only tools such as <c>approve</c> remain excluded.
    /// </summary>
    public IReadOnlyList<AgentToolDescriptor> GetDescriptors() =>
        Descriptors.Where(descriptor => IsAvailable(descriptor.Name) && !IsSessionTool(descriptor.Name) &&
            _inProcessExposure.Exposes(descriptor.Name) && _exposure.Exposes(descriptor.Name)).ToArray();

    /// <summary>Provider-specific catalog for trusted in-process native chat; never used by MCP discovery.</summary>
    public IReadOnlyList<AgentToolDescriptor> GetInProcessDescriptors(string providerId) =>
        InProcessExposureFor(providerId) is { } exposure
            ? Descriptors.Where(descriptor => descriptor.Name != ApproveToolName && IsProductInProcessTool(descriptor.Name) &&
                IsAvailable(descriptor.Name, exposure)).ToArray()
            : [];

    /// <summary>
    /// Tools from the shared release stage for the MCP broker. Provider-scoped session tools are obtained through
    /// <see cref="GetSessionChannelDescriptors"/> so they do not expand unscoped/external MCP discovery.
    /// </summary>
    public IReadOnlyList<AgentToolDescriptor> GetChannelDescriptors() =>
        Descriptors.Where(descriptor => IsAvailable(descriptor.Name)).ToArray();

    public IReadOnlyList<AgentToolDescriptor> GetSessionChannelDescriptors(string providerId) =>
        string.Equals(providerId, AgentProviderIds.ClaudeCodeSubscription, StringComparison.Ordinal)
            ? Descriptors.Where(descriptor => IsAvailable(descriptor.Name) || IsAvailable(descriptor.Name, _claudeExposure)).ToArray()
            : GetChannelDescriptors();

    /// <summary>Tools that exist only for the principal of a per-session channel.</summary>
    public static bool IsSessionTool(string? name) =>
        name is CreateWorkspaceFileToolName or GetCachedSchemaToolName or GetWorkspaceContextToolName or ProposeFileEditToolName or ApproveToolName or GetQueryResultsToolName or GetQueryDiagnosticsToolName;

    public AgentToolDescriptor? FindDescriptor(string? name) =>
        IsAvailable(name)
            ? Descriptors.FirstOrDefault(descriptor => string.Equals(name, descriptor.Name, StringComparison.Ordinal))
            : null;

    public string? GetSessionChannelInputSchemaJson(string providerId, string? name) =>
        FindSessionChannelDescriptor(providerId, name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "input", CatalogInputSchemaJson(descriptor.Name))
            : null;

    public string? GetSessionChannelOutputSchemaJson(string providerId, string? name) =>
        FindSessionChannelDescriptor(providerId, name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "output", CatalogOutputSchemaJson(descriptor.Name))
            : null;

    private AgentToolDescriptor? FindSessionChannelDescriptor(string providerId, string? name) =>
        IsAvailable(name) ||
        (string.Equals(providerId, AgentProviderIds.ClaudeCodeSubscription, StringComparison.Ordinal) && IsAvailable(name, _claudeExposure))
            ? Descriptors.FirstOrDefault(descriptor => string.Equals(name, descriptor.Name, StringComparison.Ordinal))
            : null;

    /// <summary>Descriptor for an approved in-process product provider; its turn still bounds every invocation.</summary>
    public AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) =>
        name != ApproveToolName && IsProductInProcessTool(name) &&
        InProcessExposureFor(providerId) is { } exposure && IsAvailable(name, exposure)
            ? Descriptors.FirstOrDefault(descriptor => string.Equals(name, descriptor.Name, StringComparison.Ordinal))
            : null;

    private AgentToolDescriptor? FindDescriptorForInvocation(AgentPrincipal? principal,
        AgentInvocationContext? context, string? name)
    {
        if (principal?.IsSessionChannel == true &&
            SessionScopeOf(principal) is { ProviderId: AgentProviderIds.ClaudeCodeSubscription } &&
            IsAvailable(name, _claudeExposure))
            return Descriptors.FirstOrDefault(descriptor => string.Equals(name, descriptor.Name, StringComparison.Ordinal));
        if (principal?.Origin == AgentPrincipalOrigin.Internal && context?.ProviderId is { } providerId &&
            FindInProcessDescriptor(providerId, name) is { } inProcessDescriptor &&
            IsNativeProductToolInvocation(principal, context, name, providerId))
            return inProcessDescriptor;
        return FindDescriptor(name);
    }

    private bool IsNativeProductToolInvocation(AgentPrincipal principal, AgentInvocationContext context,
        string? name, string providerId)
    {
        if (context.SessionId is not { } sessionId || context.TurnId is not { } turnId ||
            _sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId) is not { } turn)
            return false;
        return string.Equals(turn.ProviderId, providerId, StringComparison.Ordinal) &&
            string.Equals(context.ProviderId, turn.ProviderId, StringComparison.Ordinal) &&
            !turn.Plan.IsBlocked && turn.Plan.ProductTools.Contains(name!, StringComparer.Ordinal) &&
            turn.Permissions.IsWellFormed &&
            string.Equals(turn.Permissions.ProviderId, turn.ProviderId, StringComparison.Ordinal) &&
            IsConsentSatisfied(turn.ProviderId, turn.Permissions) &&
            (!AgentProductToolNames.ReadTools.Contains(name!, StringComparer.Ordinal) ||
                turn.Permissions.EnabledReadTools?.Contains(name!, StringComparer.Ordinal) == true) &&
            (!AgentProductToolNames.IsMongoDocumentRead(name!) || turn.Permissions.DataSending.MongoDocuments) &&
            InProcessExposureFor(providerId)!.Exposes(name!);
    }

    private static bool IsProductInProcessTool(string? name) =>
        name is not null && name != GetCollectionSchemaToolName && name != ApproveToolName &&
            (AgentProductToolNames.ReadTools.Contains(name, StringComparer.Ordinal) ||
            name is ProposeFileEditToolName or CreateWorkspaceFileToolName);

    private AgentToolExposure? InProcessExposureFor(string providerId) => providerId switch
    {
        AgentProviderIds.GitHubCopilotSubscription => _copilotExposure,
        "local" => _inProcessExposure,
        _ => null
    };

    private static bool IsConsentSatisfied(string providerId, AgentProviderPermissions permissions) =>
        providerId == "local" || permissions.HasExternalDestinationConsent;

    // A tool is discoverable only when its stage is released, the channel authority that issues and revalidates
    // principals was composed, and its handler dependencies exist.
    private bool IsAvailable(string? name) => IsAvailable(name, _exposure);

    private bool IsAvailable(string? name, AgentToolExposure exposure) =>
        _principalAuthority is not null && exposure.Exposes(name) && name switch
    {
        ListConnectionsToolName => true,
        ListDatabasesToolName or ListCollectionsToolName => _metadata is not null,
        GetIndexesToolName or GetSearchIndexesToolName => _indexes is not null,
        GetQueryResultsToolName or GetQueryDiagnosticsToolName => _sessionTools?.WorkspaceContext is not null,
        GetCachedSchemaToolName => _sessionTools?.MetadataCache is not null,
        GetWorkspaceContextToolName => _sessionTools?.WorkspaceContext is not null,
        CreateWorkspaceFileToolName => _sessionTools is { WorkspaceContext: not null, FileCreator: not null },
        ProposeFileEditToolName => _sessionTools is { WorkspaceContext: not null, ProposalSink: not null },
        // Available with the session scopes alone: a missing confirmation port answers deny; it never hides the tool
        // the CLI was told to call.
        ApproveToolName => _sessionTools is not null,
        _ => false
    };

    public string? GetInputSchemaJson(string? name) =>
        FindDescriptor(name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "input", CatalogInputSchemaJson(descriptor.Name))
            : null;

    public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
        FindInProcessDescriptor(providerId, name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "input", CatalogInputSchemaJson(descriptor.Name))
            : null;

    public string? GetInProcessOutputSchemaJson(string providerId, string? name) =>
        FindInProcessDescriptor(providerId, name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "output", CatalogOutputSchemaJson(descriptor.Name))
            : null;

    public string? GetOutputSchemaJson(string? name) =>
        FindDescriptor(name) is { } descriptor
            ? WithSchemaIdentity(descriptor, "output", CatalogOutputSchemaJson(descriptor.Name))
            : null;

    // Every published schema is closed and carries a versioned identity bound to the descriptor version.
    // Changing a schema requires a new descriptor version; clients must not reuse a cached schema across versions.
    private static string? WithSchemaIdentity(AgentToolDescriptor descriptor, string kind, string? schema) =>
        schema is { Length: > 1 } && schema[0] == '{'
            ? "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"$id\":\"urn:esilvasoft:kapibarastudio:agent-tool:" +
              descriptor.Name + ":v" + descriptor.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) +
              ":" + kind + "\"," + schema[1..]
            : null;

    private static string? CatalogInputSchemaJson(string? name) =>
        name switch
        {
            ListConnectionsToolName => ListConnectionsInputSchema,
            ListDatabasesToolName => ListDatabasesInputSchema,
            ListCollectionsToolName => ListCollectionsInputSchema,
            GetIndexesToolName or GetSearchIndexesToolName => GetIndexesInputSchema,
            CreateWorkspaceFileToolName => CreateWorkspaceFileInputSchema,
            GetQueryResultsToolName => GetQueryResultsInputSchema,
            GetQueryDiagnosticsToolName => ListConnectionsInputSchema,
            GetCachedSchemaToolName => GetIndexesInputSchema,
            GetWorkspaceContextToolName => ListConnectionsInputSchema,
            ProposeFileEditToolName => ProposeFileEditInputSchema,
            ApproveToolName => ApproveInputSchema,
            _ => null
        };

    private static string? CatalogOutputSchemaJson(string? name) =>
        name switch
        {
            ListConnectionsToolName => ListConnectionsOutputSchema,
            ListDatabasesToolName or ListCollectionsToolName => NamesOutputSchema,
            GetIndexesToolName => GetIndexesOutputSchema,
            GetSearchIndexesToolName => GetSearchIndexesOutputSchema,
            CreateWorkspaceFileToolName => CreateWorkspaceFileOutputSchema,
            GetQueryResultsToolName => GetQueryResultsOutputSchema,
            GetQueryDiagnosticsToolName => GetQueryDiagnosticsOutputSchema,
            GetCachedSchemaToolName => GetCachedSchemaOutputSchema,
            GetWorkspaceContextToolName => GetWorkspaceContextOutputSchema,
            ProposeFileEditToolName => ProposeFileEditOutputSchema,
            ApproveToolName => ApproveOutputSchema,
            _ => null
        };

    public async Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken = default)
    {
        // The principal is minted only by trusted runtime/broker code (internal constructor). Its origin must
        // match the channel that carried the call: an external MCP client is always an external recipient.
        if (principal is not null && invocationContext is not null && destination is not null &&
            !IsAuthenticatedChannelBinding(principal, invocationContext, destination))
            return AgentToolInvocationResult.Failure(PermissionDenied);
        // Per-session tools and the turn plan: a session-only tool, or any tool outside the plan of a per-session
        // channel, is indistinguishable from an unknown tool (no audit, policy, profile or source access).
        if (!IsExposedToPrincipal(principal, name, invocationContext, destination, outputDataScope))
            return AgentToolInvocationResult.Failure(UnknownTool);
        var invocationDescriptor = FindDescriptorForInvocation(principal, invocationContext, name);
        if (invocationDescriptor is null) return AgentToolInvocationResult.Failure(UnknownTool);
        // The confirmation waits for a human (up to the approval window), dispatches nothing and releases no data: it
        // has its own path, outside the 30 s execution deadline, the audit ledger and the per-turn budget.
        if (name == ApproveToolName)
            return await InvokeApproveAsync(principal, invocationContext, destination, argumentsJson, cancellationToken)
                .ConfigureAwait(false);
        var confirmationFailure = await ConfirmInternalProductToolAsync(principal, invocationContext, destination,
            name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (confirmationFailure is not null) return confirmationFailure;
        var missingConfirmation = ConsumeRequiredConfirmation(principal, name, argumentsJson);
        if (missingConfirmation is not null) return missingConfirmation;
        if (name == CreateWorkspaceFileToolName)
            return await InvokeCreateWorkspaceFileAuditedAsync(principal!, invocationContext!, destination!,
                outputDataScope, argumentsJson, cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_executionTimeout);
        // Only a trusted principal and a complete invocation can identify an auditable operation.
        // Keep the registry private until an authenticated ingress constructs both objects.
        var auditable = principal is not null && IsCompleteInvocationContext(invocationContext) &&
            destination is not null && IsValidDestination(destination, invocationContext!) &&
            invocationDescriptor is not null;
        AgentAuditEvent? intent = null;
        var intentWritten = false;
        var terminalWritten = false;
        var successfulReadAudited = false;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (auditable)
            {
                intent = CreateAuditIntent(principal!, invocationContext!, destination!, name!, argumentsJson);
                try
                {
                    await AwaitWithCancellationAsync(_audit.AppendAsync(intent, deadline.Token), deadline.Token)
                        .ConfigureAwait(false);
                    intentWritten = true;
                }
                catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return AgentToolInvocationResult.Failure(PermissionDenied);
                }
            }

            // The intent is durable before admission. A rejected call is still counted for its
            // turn and receives a correlated, typed audit outcome without touching a source.
            // External (MCP) calls have no model turn: they share the session (= enrolled channel) and global
            // concurrency slots but no per-turn budget. Saturated slots answer Busy (retryable); an exhausted
            // turn budget answers ToolCallLimitExceeded. Both are audited as Denied/LimitExceeded.
            var quotaBusy = false;
            var nativeQuotaScope = auditable && principal!.Origin == AgentPrincipalOrigin.Internal
                ? _sessionTools?.NativeChatTurnScopes?.Find(invocationContext!.SessionId!.Value, invocationContext.TurnId!.Value)
                : null;
            // Use the permission snapshot of this exact in-process turn, never current persisted settings or model arguments.
            int? maximumCalls = nativeQuotaScope is not null &&
                (nativeQuotaScope.ProviderId is AgentProviderIds.GitHubCopilotSubscription or "local") &&
                nativeQuotaScope.ProviderId == invocationContext!.ProviderId && nativeQuotaScope.Permissions.IsWellFormed
                ? nativeQuotaScope.Permissions.MaximumToolCallsPerTurn : AgentToolInvocationQuota.LegacyMaximumPerTurn;
            using var quotaLease = auditable
                ? _quota.TryEnter(invocationContext!.SessionId!.Value, invocationContext.TurnId!.Value,
                    intent!.ConnectionId, trackTurn: principal!.Origin != AgentPrincipalOrigin.External,
                    out quotaBusy, maximumCalls)
                : null;
            var result = auditable && quotaLease is null
                ? AgentToolInvocationResult.Failure(quotaBusy ? Busy : ToolCallLimitExceeded,
                    AgentAuditDecisionReason.LimitExceeded)
                : auditable && await CheckPrincipalCurrentAsync(principal!, deadline.Token).ConfigureAwait(false) is
                    { } channelDenial
                    ? channelDenial
                    : await InvokeWithQuotaLeaseAsync(quotaLease, principal, invocationContext, destination,
                        outputDataScope, name, argumentsJson, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (result.Succeeded)
                result = await RevalidateReleaseAsync(result, principal!, invocationContext!, destination!,
                    outputDataScope, name!, argumentsJson, deadline.Token).ConfigureAwait(false) ?? result;
            if (intentWritten && !await AppendOutcomeAsync(intent!, result).ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            terminalWritten = intentWritten;
            successfulReadAudited = terminalWritten && result.Succeeded;
            if (result.Succeeded)
            {
                // Audit persistence can block. A result read before a revocation must not escape
                // merely because its terminal audit was already written.
                var lateDenial = await RevalidateReleaseAsync(result, principal!, invocationContext!, destination!,
                    outputDataScope, name!, argumentsJson, deadline.Token).ConfigureAwait(false);
                if (lateDenial is not null)
                {
                    if (!await AppendLateSuppressionAsync(intent!, lateDenial).ConfigureAwait(false))
                        return AgentToolInvocationResult.Failure(PermissionDenied);
                    return lateDenial;
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (successfulReadAudited && !await AppendLateSuppressionAsync(intent!,
                    AgentToolInvocationResult.Failure(DeadlineExceeded)).ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            if (intentWritten && !terminalWritten && !await AppendOutcomeAsync(intent!,
                    AgentToolInvocationResult.Failure(DeadlineExceeded)).ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            return AgentToolInvocationResult.Failure(DeadlineExceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (successfulReadAudited)
                await AppendLateSuppressionAsync(intent!, null).ConfigureAwait(false);
            else if (intentWritten && !terminalWritten)
                await AppendOutcomeAsync(intent!, null).ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (successfulReadAudited)
                await AppendLateSuppressionAsync(intent!, AgentToolInvocationResult.Failure("ExecutionFailed"))
                    .ConfigureAwait(false);
            else if (intentWritten && !terminalWritten)
                await AppendOutcomeAsync(intent!, AgentToolInvocationResult.Failure("ExecutionFailed"))
                    .ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }
    }

    // Before dispatch and before publication: the authenticated channel is still enrolled and the policy revision
    // captured when the principal was issued is still current. Unavailable authority denies.
    private async Task<AgentToolInvocationResult?> CheckPrincipalCurrentAsync(
        AgentPrincipal principal, CancellationToken cancellationToken)
    {
        if (_principalAuthority is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
        try
        {
            return await AwaitWithCancellationAsync(_principalAuthority.IsCurrentAsync(principal, cancellationToken),
                cancellationToken).ConfigureAwait(false)
                ? null
                : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
        }
    }

    private async Task<AgentToolInvocationResult> InvokeWithQuotaLeaseAsync(
        AgentToolInvocationQuota.Lease? lease, AgentPrincipal? principal,
        AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        var previous = _activeQuotaLease.Value;
        _activeQuotaLease.Value = lease;
        try
        {
            return await InvokeWithinDeadlineAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeQuotaLease.Value = previous;
        }
    }

    private static AgentAuditEvent CreateAuditIntent(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination,
        string name, string? argumentsJson)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Guid connectionId;
        string? database;
        bool parsed;
        if (name is GetIndexesToolName or GetSearchIndexesToolName or GetCachedSchemaToolName)
            parsed = TryParseGetIndexesArguments(argumentsJson, out connectionId, out database, out _);
        else if (IsSessionTool(name))
        {
            parsed = false;
            connectionId = Guid.Empty;
            database = null;
        }
        else
            parsed = TryParseMetadataArguments(name, argumentsJson, out connectionId, out database, out _);
        var permission = name switch
        {
            GetCachedSchemaToolName => AgentPermission.ReadSchema,
            GetQueryResultsToolName => AgentPermission.ReadDocuments,
            GetQueryDiagnosticsToolName => AgentPermission.ReadDiagnostics,
            _ => AgentPermission.ReadMetadata
        };
        return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion, startedAt,
            principal.Id, Guid.NewGuid(), context.SessionId, context.TurnId,
            AuditChannelOf(destination),
            destination.ProviderId,
            name, 1, AgentToolRisk.ReadOnly, permission,
            AgentAuditDecision.Requested, AgentAuditOutcome.Intent, principal.PolicyRevision,
            parsed && connectionId != Guid.Empty ? connectionId : null, 0, 0, 0)
        {
            NamespaceKind = database is null ? AgentAuditNamespaceKind.None : AgentAuditNamespaceKind.Pseudonym,
            NamespacePseudonym = database is null ? null : Guid.NewGuid().ToString("N"),
            DecisionReason = AgentAuditDecisionReason.NotEvaluated,
            ApprovalState = AgentAuditApprovalState.NotRequired,
            StartedAtUtc = startedAt
        }.Validate();
    }

    private async Task<bool> AppendOutcomeAsync(AgentAuditEvent intent, AgentToolInvocationResult? result)
    {
        var succeeded = result?.Succeeded == true;
        var cancelled = result is null || result.ErrorCode == DeadlineExceeded;
        // Quota refusals dispatch nothing and remain Denied/LimitExceeded, distinct from missing permission.
        var denied = result?.ErrorCode is PermissionDenied or InvalidArguments or UnknownTool or Busy or ToolCallLimitExceeded or
            ConfirmationRejected or ConfirmationExpired;
        var reason = succeeded ? AgentAuditDecisionReason.PolicyAllowed : cancelled ? AgentAuditDecisionReason.Cancelled :
            result?.AuditReason is { } auditReason ? auditReason :
            result?.ErrorCode == InvalidArguments ? AgentAuditDecisionReason.ValidationRejected :
            result?.ErrorCode == ResultTooLarge ? AgentAuditDecisionReason.LimitExceeded :
            denied ? AgentAuditDecisionReason.PermissionMissing : AgentAuditDecisionReason.ExecutionFailed;
        var outcome = succeeded ? AgentAuditOutcome.Succeeded : cancelled ? AgentAuditOutcome.Cancelled :
            reason == AgentAuditDecisionReason.ExecutionFailed ? AgentAuditOutcome.Failed :
            denied ? AgentAuditOutcome.Denied : AgentAuditOutcome.Failed;
        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            var elapsed = completedAt - intent.StartedAtUtc;
            // An unrepresentable duration leaves a visible unresolved intent and suppresses output.
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMilliseconds(AgentAuditEvent.MaximumDurationMilliseconds))
                return false;
            var duration = (elapsed.Ticks + TimeSpan.TicksPerMillisecond - 1) /
                TimeSpan.TicksPerMillisecond;
            var itemCount = 0;
            var outputBytes = 0;
            if (succeeded && result?.StructuredContentJson is { } json)
            {
                outputBytes = Encoding.UTF8.GetByteCount(json);
                using var document = JsonDocument.Parse(json);
                itemCount = intent.ToolName is GetQueryDiagnosticsToolName or GetWorkspaceContextToolName or ProposeFileEditToolName ? 1 :
                    document.RootElement.GetProperty(intent.ToolName switch
                    {
                        ListConnectionsToolName => "connections",
                        GetCachedSchemaToolName => "fields",
                        GetQueryResultsToolName => "results",
                        GetIndexesToolName or GetSearchIndexesToolName => "indexes",
                        _ => "names"
                    }).GetArrayLength();
            }
            var terminal = intent with
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = completedAt,
                Decision = succeeded ? AgentAuditDecision.Allowed : AgentAuditDecision.Denied,
                Outcome = outcome,
                DurationMilliseconds = duration,
                ItemCount = itemCount,
                OutputBytes = outputBytes,
                DecisionReason = reason,
                CompletedAtUtc = completedAt
            };
            using var appendDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await AwaitWithCancellationAsync(_audit.AppendAsync(terminal, appendDeadline.Token), appendDeadline.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<bool> AppendLateSuppressionAsync(AgentAuditEvent intent, AgentToolInvocationResult? result)
    {
        // The durable ledger allows one terminal per invocation. A second, terminal-only read
        // event records that output was withheld after execution was already audited. It carries
        // no result bytes and never implies the MongoDB read was rolled back.
        var suppression = intent with { InvocationId = Guid.NewGuid() };
        return AppendOutcomeAsync(suppression, result);
    }

    private async Task<AgentToolInvocationResult> InvokeWithinDeadlineAsync(
        AgentPrincipal? principal,
        AgentInvocationContext? invocationContext,
        AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope,
        string? name,
        string? argumentsJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FindDescriptorForInvocation(principal, invocationContext, name) is null)
            return AgentToolInvocationResult.Failure(UnknownTool);
        // A per-session channel reaches only the connections of its turn plan (null = all, empty = none).
        if (SessionConnectionDenial(principal, name, argumentsJson) is { } connectionDenial)
            return connectionDenial;
        if (name == GetCachedSchemaToolName)
            return await InvokeCachedSchemaAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name == GetWorkspaceContextToolName)
            return InvokeWorkspaceContext(principal, invocationContext, destination, outputDataScope, argumentsJson,
                cancellationToken);
        if (name == ProposeFileEditToolName)
            return await InvokeProposeFileEditAsync(principal, invocationContext, destination, outputDataScope,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name is ListDatabasesToolName or ListCollectionsToolName)
            return await InvokeMetadataAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name is GetQueryResultsToolName or GetQueryDiagnosticsToolName)
            return await InvokeQuerySnapshotAsync(principal, invocationContext, destination, outputDataScope, name,
                argumentsJson, cancellationToken).ConfigureAwait(false);
        if (name is GetIndexesToolName or GetSearchIndexesToolName)
            return await InvokeIndexesAsync(principal, invocationContext, destination, outputDataScope,
                name, argumentsJson, cancellationToken).ConfigureAwait(false);
        if (!IsClosedEmptyObject(argumentsJson)) return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null) return AgentToolInvocationResult.Failure(PermissionDenied);
        if (!IsCompleteInvocationContext(invocationContext) || destination is null ||
            outputDataScope != AgentOutputDataScope.Metadata || !IsValidDestination(destination, invocationContext!))
            return AgentToolInvocationResult.Failure(PermissionDenied);

        // A valid current policy is checked before even enumerating local profile names.
        var initialLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, initialLoad.DenialReason);
        var initialPolicy = initialLoad.Policy;

        IReadOnlyList<ConnectionProfile> loaded;
        try
        {
            loaded = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        // Freeze the internal profiles before any per-profile policy reads. Only allowlisted fields are projected.
        ConnectionProfile?[] snapshot;
        try
        {
            if (loaded is null) return AgentToolInvocationResult.Failure(PermissionDenied);
            snapshot = loaded.Cast<ConnectionProfile?>().ToArray();
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied);
        }

        var authorized = new List<ConnectionSummary>(MaximumConnections + 1);
        var authorizedProfiles = new List<ConnectionProfile>(MaximumConnections + 1);
        var authorizedSnapshots = new Dictionary<Guid, AuthorizedConnectionSnapshot>(MaximumConnections);
        var outputBytes = Utf8ByteCount("{\"connections\":[") + Utf8ByteCount("],\"truncated\":false}");
        var truncated = false;
        foreach (var profile in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Outside the turn plan of a per-session channel: skipped before validation, so a profile the channel may
            // not see can neither be listed nor make the whole listing fail.
            if (profile is not null && profile.Id != Guid.Empty &&
                !IsConnectionInSessionScope(principal, invocationContext, profile.Id)) continue;
            if (profile is null || profile.Id == Guid.Empty || profile.SourceGenerationId is not Guid generationId || generationId == Guid.Empty ||
                !IsValidProfileName(profile.Name))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(
                        principal,
                        AgentPermission.ReadMetadata,
                        AgentToolRisk.ReadOnly,
                        AgentNamespaceScope.ForConnection(profile.Id),
                        initialPolicy.Revision,
                        profile.IsReadOnly,
                        invocationContext,
                        generationId,
                        destination,
                        outputDataScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
            }

            if (decision.PolicyRevision != initialPolicy.Revision)
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
            if (!decision.IsAllowed)
            {
                if (decision.Reason == AgentPermissionDenialReason.MissingGrant) continue;
                return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }

            if (authorized.Count == MaximumConnections)
            {
                truncated = true;
                break;
            }

            // A user-defined name is arbitrary text and can contain credentials. External recipients receive
            // only an ID-derived alias; pattern matching cannot prove that a free-text name contains no secret.
            var outputName = destination.IsExternal
                ? $"Conexão {profile.Id:D}"
                : profile.Name;
            var summary = new ConnectionSummary(profile.Id, outputName, profile.IsReadOnly);
            if (Utf8ByteCount(outputName) > MaximumOutputBytes)
            {
                if (authorized.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }

            var itemJson = JsonSerializer.SerializeToUtf8Bytes(summary, SerializerOptions);
            var itemBytes = itemJson.Length + (authorized.Count == 0 ? 0 : 1);
            if (outputBytes + itemBytes > MaximumOutputBytes)
            {
                if (authorized.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }

            if (!authorizedSnapshots.TryAdd(profile.Id, new AuthorizedConnectionSnapshot(generationId, profile.Name, summary)))
                return AgentToolInvocationResult.Failure(PermissionDenied);
            authorized.Add(summary);
            authorizedProfiles.Add(profile);
            outputBytes += itemBytes;
        }

        // Revalidate the projected values and origin after all asynchronous permission reads. This does not
        // replace the eventual transport's authorization check at the point it releases output.
        if (await RevalidateProfilesAsync(authorizedSnapshots, cancellationToken,
                id => IsConnectionInSessionScope(principal, invocationContext, id)).ConfigureAwait(false) is { } revalidationFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, revalidationFailure);

        // Keep policy validation last, including revocation during the final profile read.
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != initialPolicy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var output = new ListConnectionsResponse(authorized, truncated);
        var json = JsonSerializer.Serialize(output, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes)
            return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, authorizedProfiles);
    }

    private async Task<AgentToolInvocationResult> InvokeMetadataAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string name, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseMetadataArguments(name, argumentsJson, out var connectionId, out var database, out var skip))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.Metadata || _metadata is null)
            return AgentToolInvocationResult.Failure(PermissionDenied);

        var initialLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, initialLoad.DenialReason);
        var policy = initialLoad.Policy;

        ConnectionProfile profile;
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return AgentToolInvocationResult.Failure(PermissionDenied);
            var matching = profiles.Where(item => item?.Id == connectionId).Take(2).ToArray();
            if (matching.Length != 1 || matching[0] is not { SourceGenerationId: { } generation } ||
                generation == Guid.Empty || !IsValidProfileName(matching[0].Name))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            profile = matching[0];
            if (HasDynamicMongoTarget(profile))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var generationId = profile.SourceGenerationId!.Value;
        // A scoped grant permits enumerating only to discover its own names. No result is released by this preflight.
        var hasEligibleGrant = policy.Grants.Any(grant => grant.PrincipalId == principal.Id &&
            grant.Scope.ConnectionId == connectionId && grant.SourceGenerationId == generationId &&
            grant.Permission == AgentPermission.ReadMetadata && grant.InvocationScope.Covers(context!) &&
            grant.Destination == destination && grant.OutputDataScope == outputScope &&
            (database is null || grant.Scope.DatabaseName is null ||
             string.Equals(grant.Scope.DatabaseName, database, StringComparison.Ordinal)));
        if (!hasEligibleGrant)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } preflightFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightFailure);
        var beforeRead = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (beforeRead.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, beforeRead.DenialReason);
        if (beforeRead.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        IReadOnlyList<string> names;
        bool overflow;
        try
        {
            if (name == ListDatabasesToolName)
            {
                var result = await AwaitWithCancellationAsync(
                    _metadata.ListDatabaseNamesBoundedAsync(profile, 10_000, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                names = result?.Items!;
                overflow = result?.Overflow ?? true;
            }
            else
            {
                var result = await AwaitWithCancellationAsync(
                    _metadata.ListCollectionNamesBoundedAsync(profile, database!, 10_000, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (result is null || result.Overflow || result.Items is null || result.Items.Count > 10_000)
                    return AgentToolInvocationResult.Failure(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);
                names = result.Items.Select(item => item?.Name!).ToArray();
                overflow = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (overflow || names is null || names.Count > 10_000)
            return AgentToolInvocationResult.Failure(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);

        var allowed = new List<string>(names.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeMetadataName(item) || !seen.Add(item))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var scope = name == ListDatabasesToolName
                ? AgentNamespaceScope.ForDatabase(connectionId, item)
                : AgentNamespaceScope.ForCollection(connectionId, database!, item);
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, AgentPermission.ReadMetadata, AgentToolRisk.ReadOnly,
                        scope, policy.Revision, profile.IsReadOnly, context, generationId, destination, outputScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
            }
            if (decision.PolicyRevision != policy.Revision)
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
            if (!decision.IsAllowed)
            {
                if (decision.Reason == AgentPermissionDenialReason.MissingGrant) continue;
                return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }
            allowed.Add(item);
        }

        // The offset indexes authorized names, not the server's untrusted/unfiltered list.
        allowed.Sort(StringComparer.Ordinal);
        var page = allowed.Skip(skip).Take(MaximumConnections).ToArray();
        var truncated = allowed.Count > skip + page.Length;

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } failure)
            return AgentToolInvocationResult.Failure(PermissionDenied, failure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
        var json = JsonSerializer.Serialize(new NamesResponse(page, truncated), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private async Task<AgentAuditDecisionReason?> RevalidateMetadataProfileAsync(
        ConnectionProfile expected, CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return AgentAuditDecisionReason.ExecutionFailed;
            var matching = profiles.Where(item => item?.Id == expected.Id).Take(2).ToArray();
            return matching.Length == 1 && matching[0] == expected
                ? null : AgentAuditDecisionReason.ValidationRejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentAuditDecisionReason.ExecutionFailed;
        }
    }

    private static bool IsSafeMetadataName(string? name)
    {
        if (name is not { Length: > 0 and <= 255 } || Utf8ByteCount(name) > 255 ||
            string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
            name.Any(c => char.IsControl(c) || c is '/' or '\\' or '@')) return false;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool TryParseMetadataArguments(string name, string? json, out Guid connectionId, out string? database, out int skip)
    {
        connectionId = Guid.Empty;
        database = null;
        skip = 0;
        if (name == ListConnectionsToolName) return IsClosedEmptyObject(json);
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var count = 0;
            var hasSkip = false;
            foreach (var property in root.EnumerateObject())
            {
                count++;
                if (property.NameEquals("connectionId") && property.Value.ValueKind == JsonValueKind.String &&
                    Guid.TryParseExact(property.Value.GetString(), "D", out var parsed) && parsed != Guid.Empty)
                    connectionId = parsed;
                else if (name == ListCollectionsToolName && property.NameEquals("database") &&
                    property.Value.ValueKind == JsonValueKind.String && IsSafeMetadataName(property.Value.GetString()))
                    database = property.Value.GetString();
                else if (property.NameEquals("skip") && !hasSkip &&
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsedSkip) &&
                    parsedSkip is >= 0 and <= 10_000)
                {
                    skip = parsedSkip;
                    hasSkip = true;
                }
                else return false;
            }
            return connectionId != Guid.Empty &&
                (name == ListDatabasesToolName && count == 1 + (hasSkip ? 1 : 0) ||
                 name == ListCollectionsToolName && count == 2 + (hasSkip ? 1 : 0) && database is not null);
        }
        catch (JsonException) { return false; }
    }

    private sealed record NamesResponse(
        [property: JsonPropertyName("names")] IReadOnlyList<string> Names,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private static int Utf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value);

    private async Task<AgentAuditDecisionReason?> RevalidateProfilesAsync(
        IReadOnlyDictionary<Guid, AuthorizedConnectionSnapshot> expected,
        CancellationToken cancellationToken, Func<Guid, bool>? inScope = null)
    {
        try
        {
            var current = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (current is null) return AgentAuditDecisionReason.ExecutionFailed;
            var remaining = new HashSet<Guid>(expected.Keys);
            foreach (var profile in current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (profile is not null && profile.Id != Guid.Empty && inScope?.Invoke(profile.Id) == false &&
                    !expected.ContainsKey(profile.Id)) continue;
                if (profile is null || profile.Id == Guid.Empty ||
                    profile.SourceGenerationId is not Guid generationId || generationId == Guid.Empty ||
                    !IsValidProfileName(profile.Name))
                    return AgentAuditDecisionReason.ValidationRejected;
                if (!expected.TryGetValue(profile.Id, out var snapshot)) continue;
                if (!remaining.Remove(profile.Id) || generationId != snapshot.SourceGenerationId ||
                    !string.Equals(profile.Name, snapshot.OriginalName, StringComparison.Ordinal) ||
                    profile.IsReadOnly != snapshot.Summary.ReadOnly)
                    return AgentAuditDecisionReason.ValidationRejected;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return remaining.Count == 0 ? null : AgentAuditDecisionReason.ValidationRejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentAuditDecisionReason.ExecutionFailed;
        }
    }

    private static bool IsAuthenticatedChannelBinding(
        AgentPrincipal principal, AgentInvocationContext context, AgentOutputDestination destination) =>
        principal.Origin switch
        {
            // The runtime (internal) never reaches the MCP channel; an authenticated MCP client (external) only
            // ever receives McpExternal, so its audit channel cannot be confused with a chat provider.
            AgentPrincipalOrigin.Internal => context.ClientId is null &&
                destination.Kind != AgentOutputDestinationKind.McpExternal,
            AgentPrincipalOrigin.External => context.ClientId is { } clientId && clientId != Guid.Empty &&
                destination.Kind == AgentOutputDestinationKind.McpExternal,
            _ => false
        };

    /// <summary>The audit channel is derived from the typed destination, never from a provider or client name.</summary>
    internal static AgentAuditChannel AuditChannelOf(AgentOutputDestination destination) => destination.Kind switch
    {
        AgentOutputDestinationKind.Local => AgentAuditChannel.Internal,
        AgentOutputDestinationKind.McpExternal => AgentAuditChannel.McpExternal,
        _ => AgentAuditChannel.ProviderExternal
    };

    private static bool IsCompleteInvocationContext(AgentInvocationContext? context) =>
        context is not null && context.SessionId is { } sessionId && sessionId != Guid.Empty &&
        context.TurnId is { } turnId && turnId != Guid.Empty;

    private static bool IsValidDestination(AgentOutputDestination destination, AgentInvocationContext context) =>
        Enum.IsDefined(destination.Kind) &&
        (destination.Kind == AgentOutputDestinationKind.Local && destination.ProviderId is null ||
         destination.IsExternal &&
         IsSafeAuditExternalIdentifier(destination.ProviderId) &&
         string.Equals(destination.ProviderId, context.ProviderId, StringComparison.Ordinal));

    private static bool IsSafeAuditExternalIdentifier(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        (char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0])) &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '.' or '-');

    private static bool IsValidProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) return false;
        var remaining = name.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    // A profile generation pins the template, not values resolved from ENV or the vault.
    // Reads that dispatch to MongoDB require a stable target for their namespace grant.
    private static bool HasDynamicMongoTarget(ConnectionProfile profile) =>
        profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
        profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
        profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
        profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true;

    private static AgentAuditDecisionReason MapDenialReason(AgentPermissionDenialReason reason) => reason switch
    {
        AgentPermissionDenialReason.PolicyUnavailable or AgentPermissionDenialReason.InvalidPolicy =>
            AgentAuditDecisionReason.PolicyUnavailable,
        AgentPermissionDenialReason.PolicyRevisionMismatch or AgentPermissionDenialReason.InvalidPolicyRevision =>
            AgentAuditDecisionReason.PolicyRevisionMismatch,
        _ => AgentAuditDecisionReason.PermissionMissing
    };

    private readonly record struct PolicyLoadResult(
        AgentAuthorizationPolicySnapshot? Policy, AgentAuditDecisionReason DenialReason);

    private async Task<PolicyLoadResult> LoadCurrentPolicyAsync(AgentPrincipal principal, CancellationToken cancellationToken)
    {
        AgentAuthorizationPolicySnapshot? policy;
        try
        {
            policy = await AwaitWithCancellationAsync(_policies.LoadAsync(principal.Id, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, AgentAuditDecisionReason.PolicyUnavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (policy is null) return new(null, AgentAuditDecisionReason.PolicyMissing);
        if (!policy.IsValid || policy.PrincipalId != principal.Id)
            return new(null, AgentAuditDecisionReason.PolicyUnavailable);
        if (policy.Revision != principal.PolicyRevision)
            return new(null, AgentAuditDecisionReason.PolicyRevisionMismatch);
        return new(policy, AgentAuditDecisionReason.PolicyAllowed);
    }

    private async Task<T> AwaitWithCancellationAsync<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HoldQuotaUntilCompletion(operation);

            throw;
        }
    }

    private async Task AwaitWithCancellationAsync(Task operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HoldQuotaUntilCompletion(operation);
            throw;
        }
    }

    private void HoldQuotaUntilCompletion(Task operation)
    {
        if (_activeQuotaLease.Value is { } lease)
            lease.HoldUntil(operation);
        else
            _ = operation.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private static bool IsClosedEmptyObject(string? json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ListConnectionsResponse(
        [property: JsonPropertyName("connections")] IReadOnlyList<ConnectionSummary> Connections,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record ConnectionSummary(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("readOnly")] bool ReadOnly);

    private sealed record AuthorizedConnectionSnapshot(Guid SourceGenerationId, string OriginalName, ConnectionSummary Summary);
}
