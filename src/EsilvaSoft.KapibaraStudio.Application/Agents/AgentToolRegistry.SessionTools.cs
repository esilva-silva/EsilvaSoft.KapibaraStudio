using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Per-session channel rules (ADR-056). The scope is found by the authenticated principal only: tool arguments, client
/// names and model text never select it. A principal of a per-session channel sees exactly the tools of its current
/// turn plan and the connections of <see cref="AgentTurnPlan.AllowedConnectionIds"/>; every other principal never sees
/// a per-session tool.
/// </summary>
public sealed partial class AgentToolRegistry
{
    private const string GetWorkspaceContextOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["workspaceFolder","activeFile","tab"],"properties":{"workspaceFolder":{"type":["string","null"],"maxLength":1024},"activeFile":{"type":["object","null"],"additionalProperties":false,"required":["name","relativePath","insideWorkspace","excluded"],"properties":{"name":{"type":["string","null"],"maxLength":255},"relativePath":{"type":["string","null"],"maxLength":1024},"insideWorkspace":{"type":"boolean"},"excluded":{"type":"boolean"}}},"tab":{"type":["object","null"],"additionalProperties":false,"required":["connectionInScope"],"properties":{"connectionId":{"type":"string","format":"uuid"},"connectionName":{"type":"string","maxLength":255},"database":{"type":"string","maxLength":255},"collection":{"type":"string","maxLength":255},"connectionInScope":{"type":"boolean"}}}}}
        """;

    private static readonly JsonSerializerOptions SessionSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private AgentMcpSessionScope? SessionScopeOf(AgentPrincipal? principal) =>
        principal is { Origin: AgentPrincipalOrigin.External } && _sessionTools is { } ports
            ? ports.SessionScopes.FindByPrincipal(principal.Id)
            : null;

    private bool IsExposedToPrincipal(AgentPrincipal? principal, string? name, AgentInvocationContext? context,
        AgentOutputDestination? destination, AgentOutputDataScope? outputScope)
    {
        var scope = SessionScopeOf(principal);
        // A session channel is authorized only through its active scope: an orphan (crash, kill, closed session) is
        // denied even when external MCP clients are enabled.
        if (principal is { IsSessionChannel: true } && scope is null) return false;
        if (IsSessionTool(name))
            return scope?.Exposes(name) == true ||
                name is GetWorkspaceContextToolName or GetCachedSchemaToolName or ProposeFileEditToolName &&
                IsSessionCallBound(principal, context, destination, outputScope, name, out _);
        if (principal?.Origin == AgentPrincipalOrigin.Internal && context?.SessionId is { } sessionId &&
            context.TurnId is { } turnId && _sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId) is { } native)
            return !native.Plan.IsBlocked && native.Permissions.IsWellFormed &&
                native.Permissions.HasExternalDestinationConsent &&
                native.Permissions.EnabledReadTools?.Contains(name!, StringComparer.Ordinal) == true &&
                native.Plan.ProductTools.Contains(name!, StringComparer.Ordinal) &&
                string.Equals(native.ProviderId, context.ProviderId, StringComparison.Ordinal) &&
                (!IsCopilotDocumentTool(name) ||
                 string.Equals(native.ProviderId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal) &&
                 native.Permissions.DataSending.MongoDocuments && _copilotExposure.Exposes(name));
        return scope is null || scope.Exposes(name);
    }

    private bool IsConnectionInSessionScope(AgentPrincipal? principal, AgentInvocationContext? context,
        Guid connectionId)
    {
        if (SessionScopeOf(principal) is { } scope) return scope.AllowsConnection(connectionId);
        if (principal is { IsSessionChannel: true }) return false;
        if (principal?.Origin == AgentPrincipalOrigin.Internal && context?.SessionId is { } sessionId &&
            context.TurnId is { } turnId && _sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId) is { } native)
            return (native.Plan.AllowedConnectionIds is null || native.Plan.AllowedConnectionIds.Contains(connectionId)) &&
                (native.Permissions.ConnectionScope != AgentConnectionScope.Selected ||
                 native.Permissions.SelectedConnectionIds?.Contains(connectionId) == true);
        return true;
    }

    private AgentToolInvocationResult? SessionConnectionDenial(AgentPrincipal? principal, string? name, string? argumentsJson)
    {
        if (SessionScopeOf(principal) is not { } scope) return null;
        if (!scope.Exposes(name))
            return AgentToolInvocationResult.Failure(UnknownTool);
        if (argumentsJson is null || Utf8ByteCount(argumentsJson) > MaximumInputBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(argumentsJson, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("connectionId", out var raw) && raw.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(raw.GetString(), "D", out var connectionId) && !scope.AllowsConnection(connectionId))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
        }
        catch (JsonException)
        {
            // The handler rejects malformed arguments with its own typed error.
        }
        return null;
    }

    private bool IsSessionCallBound(AgentPrincipal? principal, AgentInvocationContext? context,
        AgentOutputDestination? destination, AgentOutputDataScope? outputScope, string name,
        out AgentMcpSessionScope scope)
    {
        scope = null!;
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentToolOutputScopes.For(name))
            return false;

        if (SessionScopeOf(principal) is { } found)
        {
            if (!found.Exposes(name) || found.Permissions is null) return false;
            scope = found;
            return true;
        }

        // Native chat receives only the explicitly planned session tools from its exact active runtime turn.
        if (name is not (GetWorkspaceContextToolName or GetCachedSchemaToolName or ProposeFileEditToolName) ||
            principal.Origin != AgentPrincipalOrigin.Internal ||
            destination.Kind != AgentOutputDestinationKind.ProviderExternal || context!.SessionId is not { } sessionId ||
            context.TurnId is not { } turnId || _sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId) is not { } native ||
            !string.Equals(native.ProviderId, context.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(native.ProviderId, destination.ProviderId, StringComparison.Ordinal) ||
            !native.Plan.ProductTools.Contains(name, StringComparer.Ordinal) || native.Plan.IsBlocked ||
            !native.Permissions.IsWellFormed || !native.Permissions.HasExternalDestinationConsent ||
            (name == GetWorkspaceContextToolName && native.Permissions.DataSending?.TabMetadata != true) ||
            (name == GetCachedSchemaToolName && (native.Permissions.EnabledReadTools?.Contains(name, StringComparer.Ordinal) != true ||
                native.Permissions.DataSending?.InferredSchema != true)) ||
            (name == GetWorkspaceContextToolName && native.Permissions.EnabledReadTools?.Contains(name, StringComparer.Ordinal) != true) ||
            (name == ProposeFileEditToolName && (native.ConversationId == Guid.Empty ||
                native.Plan.ProposalHandling == AgentProposalHandling.Disabled || native.Plan.Mode == AgentOperationMode.Planning ||
                !(native.Permissions.EditProposals?.ActiveFile == true && native.Permissions.DataSending?.ActiveFile == true ||
                  native.Permissions.EditProposals?.OtherWorkspaceFiles == true && native.Permissions.DataSending?.WorkspaceFiles == true &&
                  native.Permissions.Workspace?.UseFilesFolder == true))))
            return false;

        scope = new AgentMcpSessionScope(Guid.Empty, Guid.Empty, native.ProviderId, native.ConversationId,
            native.Plan with { ProductTools = [name] }, native.Permissions,
            WorkspaceContext: native.WorkspaceContext)
        {
            ActiveFileAttachmentResolved = native.ActiveFileAttachmentResolved,
            ActiveFileAttachmentMatchesSnapshot = native.ActiveFileAttachmentMatchesSnapshot,
        };
        return true;
    }

    private AgentToolInvocationResult InvokeWorkspaceContext(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!IsClosedEmptyObject(argumentsJson)) return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, GetWorkspaceContextToolName, out var scope) ||
            scope.WorkspaceContext is not { } snapshot)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        cancellationToken.ThrowIfCancellationRequested();

        var permissions = scope.Permissions!;
        var exclusions = permissions.Workspace?.Exclusions ?? [];
        var workspace = string.Empty;
        var hasWorkspace = permissions.Workspace?.UseFilesFolder == true &&
            AgentWorkspacePaths.TryGetWorkspaceRoot(snapshot.WorkspaceFolder, out workspace, _sessionTools?.PathProbe);
        var workspaceFolder = hasWorkspace && workspace.Length <= 1_024 ? workspace : null;

        WorkspaceActiveFile? active = null;
        if (permissions.DataSending?.ActiveFile == true && (snapshot.ActiveFilePath is not null || snapshot.ActiveFileName is not null))
        {
            string? relative = null;
            var inside = false;
            var excluded = false;
            if (snapshot.ActiveFilePath is { } activePath)
            {
                if (hasWorkspace &&
                    AgentWorkspacePaths.TryResolveInside(workspace, activePath, exclusions, out _, out var resolved, out _, _sessionTools?.PathProbe))
                {
                    inside = true;
                    if (resolved.Length <= 1_024) relative = resolved;
                }
                else
                {
                    // Exclusions apply by the real path, inside the workspace or not (e.g. an open ".env" elsewhere).
                    var check = AgentWorkspacePaths.CheckFile(activePath, hasWorkspace ? workspace : null, exclusions, _sessionTools?.PathProbe);
                    excluded = check is AgentWorkspacePathError.Excluded or AgentWorkspacePathError.InvalidExclusion;
                    // A lexical path below the root is not enough when a link or junction prevents proving the
                    // actual target. Keep the context response fail-closed instead of describing it as in-workspace.
                    inside = hasWorkspace && check != AgentWorkspacePathError.LinkTraversal &&
                        AgentWorkspacePaths.IsStrictlyInside(activePath, workspace);
                }
            }
            // An excluded file keeps only the fact that it is excluded; outside the workspace only its name is given.
            var name = excluded ? null : CleanSessionName(snapshot.ActiveFileName ?? Path.GetFileName(snapshot.ActiveFilePath));
            active = new WorkspaceActiveFile(name, relative, inside, excluded);
        }

        WorkspaceTab? tab = null;
        if (permissions.DataSending?.TabMetadata == true &&
            (snapshot.ConnectionId is not null || snapshot.ConnectionName is not null))
        {
            var connectionId = Guid.TryParse(snapshot.ConnectionId, out var parsed) && parsed != Guid.Empty ? parsed : (Guid?)null;
            var inScope = connectionId is { } id && scope.AllowsConnection(id);
            // A connection outside the turn plan is not described at all (name, id, database and collection omitted).
            var database = inScope ? CleanSessionName(snapshot.DatabaseName) : null;
            tab = inScope
                ? new WorkspaceTab(connectionId!.Value.ToString("D"), CleanSessionName(snapshot.ConnectionName), database,
                    database is null ? null : CleanSessionName(snapshot.CollectionName), true)
                : new WorkspaceTab(null, null, null, null, false);
        }

        var json = JsonSerializer.Serialize(new WorkspaceContextResponse(workspaceFolder, active, tab),
            SessionSerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.SuccessWorkspaceContext(json, snapshot);
    }

    /// <summary>
    /// Release gate of per-session tools that touch no MongoDB namespace: the channel is still current and the tool is
    /// still in the turn plan of its scope. There is no profile or grant to revalidate.
    /// </summary>
    private async Task<AgentToolInvocationResult?> RevalidateSessionReleaseAsync(AgentPrincipal principal,
        AgentInvocationContext context, AgentOutputDestination destination, AgentOutputDataScope? outputScope,
        string name, CancellationToken cancellationToken)
    {
        if (principal.Origin == AgentPrincipalOrigin.External &&
            await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is { } channelDenial)
            return channelDenial;
        return IsSessionCallBound(principal, context, destination, outputScope, name, out _)
            ? null
            : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
    }

    /// <summary>Best-effort redaction of free text (names can carry secrets); invalid or failing text is omitted.</summary>
    private static string? CleanSessionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl)) return null;
        try
        {
            var redacted = AgentSecretRedaction.Redact(name.Trim());
            return redacted is { Length: > 0 and <= 255 } ? redacted : null;
        }
        catch (AgentRuntimeException)
        {
            return null;
        }
    }

    private sealed record WorkspaceContextResponse(
        [property: JsonPropertyName("workspaceFolder"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WorkspaceFolder,
        [property: JsonPropertyName("activeFile"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] WorkspaceActiveFile? ActiveFile,
        [property: JsonPropertyName("tab"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] WorkspaceTab? Tab);

    private sealed record WorkspaceActiveFile(
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name,
        [property: JsonPropertyName("relativePath"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RelativePath,
        [property: JsonPropertyName("insideWorkspace")] bool InsideWorkspace,
        [property: JsonPropertyName("excluded")] bool Excluded);

    private sealed record WorkspaceTab(
        [property: JsonPropertyName("connectionId")] string? ConnectionId,
        [property: JsonPropertyName("connectionName")] string? ConnectionName,
        [property: JsonPropertyName("database")] string? Database,
        [property: JsonPropertyName("collection")] string? Collection,
        [property: JsonPropertyName("connectionInScope")] bool ConnectionInScope);
}
