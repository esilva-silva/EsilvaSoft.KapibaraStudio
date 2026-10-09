using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private const string CreateWorkspaceFileInputSchema = """
        {"type":"object","additionalProperties":false,"required":["path","content"],"properties":{"path":{"type":"string","minLength":1,"maxLength":1024},"content":{"type":"string","maxLength":65536}}}
        """;
    private const string CreateWorkspaceFileOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["status","path","bytes"],"properties":{"status":{"type":"string","const":"created"},"path":{"type":"string"},"bytes":{"type":"integer","minimum":0}}}
        """;

    private async Task<AgentToolInvocationResult> InvokeCreateWorkspaceFileAuditedAsync(AgentPrincipal principal,
        AgentInvocationContext context, AgentOutputDestination destination, AgentOutputDataScope? outputScope,
        string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseCreateFile(argumentsJson, out var path, out var content))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, CreateWorkspaceFileToolName, out var scope) ||
            !CanCreate(scope) || scope.WorkspaceContext is not { } snapshot || _sessionTools?.FileCreator is not { } creator)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        var exclusions = scope.Permissions!.Workspace.EffectiveExclusions.ToArray();
        if (!AgentWorkspacePaths.TryResolveInside(snapshot.WorkspaceFolder, path, exclusions, out _, out var relative,
                out var pathError, _sessionTools.PathProbe))
            return Refuse(MapProposalPathError(pathError));
        var native = _sessionTools.NativeChatTurnScopes?.Find(context.SessionId!.Value, context.TurnId!.Value);
        var intent = (CreateAuditIntent(principal, context, destination, CreateWorkspaceFileToolName, argumentsJson) with
        {
            Risk = AgentToolRisk.Write, Permission = AgentPermission.CreateWorkspaceFiles, ApprovalState = AgentAuditApprovalState.Pending,
            ApprovalId = Guid.NewGuid()
        }).Validate();
        if (!await TryAppendAuditAsync(intent).ConfigureAwait(false)) return AgentToolInvocationResult.Failure(PermissionDenied);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_executionTimeout);
        using var lease = _quota.TryEnter(context.SessionId!.Value, context.TurnId!.Value, null,
            trackTurn: principal.Origin != AgentPrincipalOrigin.External, out var busy,
            native is not null ? native.Permissions.MaximumToolCallsPerTurn : AgentToolInvocationQuota.LegacyMaximumPerTurn);
        var created = false;
        AgentToolInvocationResult result;
        try
        {
            if (lease is null) result = AgentToolInvocationResult.Failure(busy ? Busy : ToolCallLimitExceeded);
            else if (!await AuthorizeCommit(deadline.Token).ConfigureAwait(false)) result = AgentToolInvocationResult.Failure(PermissionDenied);
            else
            {
                // Await actual completion. A timeout never detaches a task that may publish a file later.
                var status = await creator.CreateAsync(snapshot.WorkspaceFolder!, relative, content!, exclusions,
                    AuthorizeCommit, deadline.Token).ConfigureAwait(false);
                created = status == AgentFileCreationStatus.Created;
                result = created ? AgentToolInvocationResult.Success(JsonSerializer.Serialize(new
                {
                    status = "created", path = relative, bytes = Encoding.UTF8.GetByteCount(content!)
                }, SerializerOptions)) : AgentToolInvocationResult.Failure(status switch
                {
                    AgentFileCreationStatus.AlreadyExists => "FileAlreadyExists",
                    AgentFileCreationStatus.ParentMissing => "ParentDirectoryNotFound",
                    AgentFileCreationStatus.PermissionDenied => PermissionDenied,
                    AgentFileCreationStatus.PathRejected => "InvalidPath",
                    _ => "FileCreationFailed"
                });
            }
        }
        catch (OperationCanceledException) { result = AgentToolInvocationResult.Failure("FileCreationCancelled"); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { result = AgentToolInvocationResult.Failure("FileCreationFailed"); }
        var terminal = ConfirmationTerminal(intent, AgentToolConfirmationDecision.ApprovedOnce, expired: false);
        if (terminal is null) return AgentToolInvocationResult.Failure(created ? "FileCreatedAuditIncomplete" : PermissionDenied);
        terminal = terminal with
        {
            Outcome = created ? AgentAuditOutcome.Succeeded : AgentAuditOutcome.Failed,
            DecisionReason = created ? AgentAuditDecisionReason.ApprovalGranted : AgentAuditDecisionReason.ExecutionFailed,
            ItemCount = created ? 1 : 0,
            OutputBytes = result.StructuredContentJson is { } json ? Encoding.UTF8.GetByteCount(json) : 0
        };
        if (!created && result.ErrorCode is PermissionDenied or Busy or ToolCallLimitExceeded or "FileAlreadyExists" or "ParentDirectoryNotFound" or "InvalidPath")
            terminal = terminal with
            {
                Outcome = AgentAuditOutcome.Denied, Decision = AgentAuditDecision.Denied,
                DecisionReason = result.ErrorCode is Busy or ToolCallLimitExceeded ? AgentAuditDecisionReason.LimitExceeded :
                    result.ErrorCode == PermissionDenied ? AgentAuditDecisionReason.PermissionMissing : AgentAuditDecisionReason.ValidationRejected
            };
        if (result.ErrorCode == "FileCreationCancelled")
            terminal = terminal with { Outcome = AgentAuditOutcome.Cancelled, DecisionReason = AgentAuditDecisionReason.Cancelled };
        if (!await TryAppendAuditAsync(terminal).ConfigureAwait(false))
            return AgentToolInvocationResult.Failure(created ? "FileCreatedAuditIncomplete" : PermissionDenied);
        return result;

        async Task<bool> AuthorizeCommit(CancellationToken token) =>
            await CheckPrincipalCurrentAsync(principal, token).ConfigureAwait(false) is null &&
            await RevalidateSessionReleaseAsync(principal, context, destination, outputScope, CreateWorkspaceFileToolName, token)
                .ConfigureAwait(false) is null && IsCurrentWorkspaceSnapshot(principal, context, snapshot) &&
            IsSessionCallBound(principal, context, destination, outputScope, CreateWorkspaceFileToolName, out var current) &&
            CanCreate(current) &&
            current.Permissions!.Workspace.EffectiveExclusions.SequenceEqual(exclusions, StringComparer.Ordinal) &&
            (principal.Origin != AgentPrincipalOrigin.External || current.Generation == scope.Generation) &&
            (principal.Origin != AgentPrincipalOrigin.Internal ||
                ReferenceEquals(_sessionTools.NativeChatTurnScopes?.Find(context.SessionId!.Value, context.TurnId!.Value), native));
    }

    private static bool CanCreate(AgentMcpSessionScope scope) => scope.Permissions is { IsWellFormed: true, NativeFileWrite: true, Workspace.UseFilesFolder: true } permissions &&
        IsConsentSatisfied(scope.ProviderId, permissions) &&
        scope.Permissions.Workspace.EffectiveExclusions.All(AgentWorkspaceExclusions.IsValidPattern) &&
        scope.ProviderId is AgentProviderIds.GitHubCopilotSubscription or AgentProviderIds.ClaudeCodeSubscription or "local" &&
        scope.Plan is { Mode: not AgentOperationMode.Planning } plan &&
        (plan.ConfirmationCategories & AgentConfirmationCategories.NativeFileWrite) != 0;

    private static bool TryParseCreateFile(string? json, out string? path, out string? content)
    {
        path = null; content = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(item.Name) || item.Value.ValueKind != JsonValueKind.String) return false;
                if (item.Name == "path") path = item.Value.GetString();
                else if (item.Name == "content") content = item.Value.GetString();
                else return false;
            }
            return path is { Length: > 0 and <= 1024 } && !Path.IsPathRooted(path) && !AgentWorkspacePaths.HasUnsafeSegment(path) &&
                content is { Length: <= 65536 } && !content.Contains('\0') &&
                new UTF8Encoding(false, true).GetByteCount(content) <= MaximumProposalTextBytes;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return false; }
    }
}
