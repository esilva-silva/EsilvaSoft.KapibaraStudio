using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

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

    private bool IsExposedToPrincipal(AgentPrincipal? principal, string? name)
    {
        var scope = SessionScopeOf(principal);
        // A session channel is authorized only through its active scope: an orphan (crash, kill, closed session) is
        // denied even when external MCP clients are enabled.
        if (principal is { IsSessionChannel: true } && scope is null) return false;
        if (IsSessionTool(name)) return scope?.Exposes(name) == true;
        return scope is null || scope.Exposes(name);
    }

    private bool IsConnectionInSessionScope(AgentPrincipal? principal, Guid connectionId) =>
        SessionScopeOf(principal) is { } scope ? scope.AllowsConnection(connectionId) : principal is not { IsSessionChannel: true };

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
            !IsValidDestination(destination, context!) || outputScope != AgentToolOutputScopes.For(name) ||
            SessionScopeOf(principal) is not { } found || !found.Exposes(name) || found.Permissions is null)
            return false;
        scope = found;
        return true;
    }

    private AgentToolInvocationResult InvokeWorkspaceContext(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!IsClosedEmptyObject(argumentsJson)) return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, GetWorkspaceContextToolName, out var scope) ||
            _sessionTools?.WorkspaceContext is not { } source)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        cancellationToken.ThrowIfCancellationRequested();

        AgentWorkspaceContext snapshot;
        try
        {
            snapshot = source.Capture() ?? throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var permissions = scope.Permissions!;
        var exclusions = permissions.Workspace?.Exclusions ?? [];
        var workspace = string.Empty;
        var hasWorkspace = permissions.Workspace?.UseFilesFolder == true &&
            AgentWorkspacePaths.TryGetWorkspaceRoot(snapshot.WorkspaceFolder, out workspace);

        WorkspaceActiveFile? active = null;
        if (permissions.DataSending?.ActiveFile == true && (snapshot.ActiveFilePath is not null || snapshot.ActiveFileName is not null))
        {
            string? relative = null;
            var inside = false;
            var excluded = false;
            if (snapshot.ActiveFilePath is { } activePath)
            {
                if (hasWorkspace &&
                    AgentWorkspacePaths.TryResolveInside(workspace, activePath, exclusions, out _, out var resolved, out _))
                {
                    inside = true;
                    relative = resolved;
                }
                else
                {
                    // Exclusions apply by the real path, inside the workspace or not (e.g. an open ".env" elsewhere).
                    var check = AgentWorkspacePaths.CheckFile(activePath, hasWorkspace ? workspace : null, exclusions);
                    excluded = check is AgentWorkspacePathError.Excluded or AgentWorkspacePathError.InvalidExclusion;
                    inside = hasWorkspace && AgentWorkspacePaths.IsStrictlyInside(activePath, workspace);
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

        var json = JsonSerializer.Serialize(new WorkspaceContextResponse(hasWorkspace ? workspace : null, active, tab),
            SessionSerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json);
    }

    /// <summary>
    /// Release gate of per-session tools that touch no MongoDB namespace: the channel is still current and the tool is
    /// still in the turn plan of its scope. There is no profile or grant to revalidate.
    /// </summary>
    private async Task<AgentToolInvocationResult?> RevalidateSessionReleaseAsync(AgentPrincipal principal, string name,
        CancellationToken cancellationToken)
    {
        if (await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is { } channelDenial)
            return channelDenial;
        return SessionScopeOf(principal)?.Exposes(name) == true
            ? null
            : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
    }

    /// <summary>Best-effort redaction of free text (names can carry secrets); invalid or failing text is omitted.</summary>
    private static string? CleanSessionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl)) return null;
        try
        {
            return AgentSecretRedaction.Redact(name.Trim());
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
