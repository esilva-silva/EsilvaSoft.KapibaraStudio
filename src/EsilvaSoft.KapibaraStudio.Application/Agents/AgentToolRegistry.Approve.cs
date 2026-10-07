using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// <c>approve</c>: the permission-prompt tool of the Claude Code CLI
/// (<c>--permission-prompt-tool mcp__kapibarastudio__approve</c>). Contract of the CLI: the input is
/// <c>{"tool_name": string, "input": object, "tool_use_id"?: string}</c>; the answer is a JSON object, returned as the
/// text content of the MCP result, either <c>{"behavior":"allow","updatedInput":&lt;input&gt;}</c> or
/// <c>{"behavior":"deny","message":string}</c>.
/// <para>
/// Enforced on the server, not trusted to the CLI: a card opens only for a tool of the current turn plan whose category
/// the plan confirms; the human decision is appended to the durable audit ledger (intent before the card, terminal after)
/// and a failed append denies. Product tool tickets are one-shot and bound to (principal, turn generation, registry
/// tool, SHA-256 of the canonical input) that expires after <see cref="ConfirmationTicketLifetime"/>; the registry
/// consumes it before executing that tool, and a product tool whose category requires confirmation never runs without
/// one. Read-only calls may receive an exact-argument grant held only for this in-memory conversation session.
/// </para>
/// </summary>
public sealed partial class AgentToolRegistry
{
    private const string ApproveInputSchema = """
        {"type":"object","additionalProperties":false,"required":["tool_name","input"],"properties":{"tool_name":{"type":"string","minLength":1,"maxLength":256},"input":{"type":"object"},"tool_use_id":{"type":"string","maxLength":256}}}
        """;

    private const string ApproveOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["behavior"],"properties":{"behavior":{"type":"string","enum":["allow","deny"]},"updatedInput":{"type":"object"},"message":{"type":"string","maxLength":512}}}
        """;

    internal const string ApproveDeniedByUser = "O usuário rejeitou esta operação no Kapibara Studio.";
    internal const string ApproveTimedOut = "A confirmação expirou sem resposta do usuário; a operação não foi executada.";
    internal const string ApproveUnavailable = "Confirmação indisponível no Kapibara Studio; a operação foi negada.";
    internal const string ApproveNotInPlan = "Esta operação não está liberada para confirmação no turno atual.";
    internal const string ApproveInvalid = "Pedido de confirmação inválido; a operação foi negada.";

    /// <summary>Lifetime of an approval ticket: the CLI calls the approved tool right after the answer.</summary>
    internal static readonly TimeSpan ConfirmationTicketLifetime = TimeSpan.FromSeconds(60);
    private const int MaximumTicketsPerPrincipal = 16;
    private const string McpToolPrefix = "mcp__kapibarastudio__";

    private readonly ConcurrentDictionary<Guid, List<ConfirmationTicket>> _confirmationTickets = new();

    private async Task<AgentToolInvocationResult> InvokeApproveAsync(AgentPrincipal? principal,
        AgentInvocationContext? context, AgentOutputDestination? destination, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || SessionScopeOf(principal) is not { } scope ||
            !scope.Exposes(ApproveToolName) || scope.Plan is not { } plan)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        if (!TryParseApproveArguments(argumentsJson, out var toolName, out var inputJson, out var toolUseId))
            return Deny(ApproveInvalid);
        // Defense in depth: a card opens only for a planned tool whose category this plan confirms. A direct call of
        // approve by the model for anything else is denied without bothering the user.
        var category = CategoryOfPlannedTool(plan, toolName!);
        if (category is null || (plan.ConfirmationCategories & category.Value) == 0) return Deny(ApproveNotInPlan);
        var registryTool = toolName!.StartsWith(McpToolPrefix, StringComparison.Ordinal) ? toolName[McpToolPrefix.Length..] : null;
        var inputHash = CanonicalInputHash(inputJson);
        if (inputHash is null) return Deny(ApproveInvalid);
        if (await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is not null)
            return Deny(ApproveUnavailable);

        var sessionApprovalKey = SessionApprovalKey(toolName!, inputHash);
        var auditName = registryTool ?? NativeAuditName(toolName);
        var intent = CreateConfirmationIntent(principal, context!, destination, auditName, category.Value);
        if (intent is null || !await TryAppendAuditAsync(intent).ConfigureAwait(false)) return Deny(ApproveUnavailable);
        if (scope.SessionApprovals?.ContainsKey(sessionApprovalKey) == true)
        {
            if (!await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.ApprovedOnce, expired: false))
                    .ConfigureAwait(false))
                return Deny(ApproveUnavailable);
            lock (scope.ApprovalGate ?? scope)
            {
                if (!ReferenceEquals(SessionScopeOf(principal), scope) || scope.Closed ||
                    scope.SessionApprovals?.ContainsKey(sessionApprovalKey) != true) return Deny(ApproveUnavailable);
                if (registryTool is not null) IssueConfirmationTicket(principal.Id, scope.Generation, registryTool, inputHash);
            }
            return Allow(inputJson!);
        }

        if (_sessionTools?.ConfirmationPrompt is not { } prompt) return Deny(ApproveUnavailable);

        // Durable intent before the human sees anything; without the ledger nothing is asked.
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(_sessionTools.ApprovalTimeout);
        AgentToolConfirmationDecision decision;
        var expired = false;
        try
        {
            decision = await prompt.ConfirmAsync(new AgentToolConfirmationRequest(scope.ConversationId, scope.ProviderId,
                toolName, category.Value, inputJson!, toolUseId), window.Token).WaitAsync(window.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.Rejected, expired: true))
                .ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            decision = AgentToolConfirmationDecision.Rejected;
            expired = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            decision = AgentToolConfirmationDecision.Rejected;
            expired = true;
        }

        // A decision that arrives after revocation or a plan change does not allow anything.
        var wasApproved = decision is AgentToolConfirmationDecision.ApprovedOnce or AgentToolConfirmationDecision.ApprovedThisSession;
        var stillPlanned = false;
        if (wasApproved)
        {
            try
            {
                var principalCurrent = await CheckPrincipalCurrentAsync(principal, window.Token).ConfigureAwait(false) is null;
                stillPlanned = principalCurrent && !window.IsCancellationRequested &&
                    SessionScopeOf(principal) is { Plan: { } current } currentScope && ReferenceEquals(currentScope, scope) &&
                    CategoryOfPlannedTool(current, toolName) is { } currentCategory &&
                    (current.ConfirmationCategories & currentCategory) != 0 &&
                    (decision != AgentToolConfirmationDecision.ApprovedThisSession || CanApproveForSession(currentCategory));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.Rejected, expired: true))
                    .ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested)
            {
                expired = true;
            }
        }
        var effective = stillPlanned ? decision : AgentToolConfirmationDecision.Rejected;
        if (!await TryAppendAuditAsync(ConfirmationTerminal(intent, effective,
                expired: expired || wasApproved && !stillPlanned))
                .ConfigureAwait(false))
            return Deny(ApproveUnavailable);
        if (effective is not (AgentToolConfirmationDecision.ApprovedOnce or AgentToolConfirmationDecision.ApprovedThisSession))
            return Deny(expired ? ApproveTimedOut : wasApproved
                ? ApproveNotInPlan : ApproveDeniedByUser);

        lock (scope.ApprovalGate ?? scope)
        {
            if (!ReferenceEquals(SessionScopeOf(principal), scope) || scope.Closed) return Deny(ApproveUnavailable);
            if (effective == AgentToolConfirmationDecision.ApprovedThisSession)
                scope.SessionApprovals?[sessionApprovalKey] = 0;
            if (registryTool is not null) IssueConfirmationTicket(principal.Id, scope.Generation, registryTool, inputHash!);
        }
        return Allow(inputJson!);
    }

    /// <summary>
    /// Prompts for the specific internal Copilot registry invocation before it can read data. This path intentionally
    /// does not create a session grant: only an explicit ApprovedOnce response is accepted.
    /// </summary>
    private async Task<AgentToolInvocationResult?> ConfirmInternalCopilotToolAsync(AgentPrincipal? principal,
        AgentInvocationContext? context, AgentOutputDestination? destination, string? toolName, string? argumentsJson,
        CancellationToken cancellationToken)
    {
        if (principal is not { Origin: AgentPrincipalOrigin.Internal } || context is null || destination is null ||
            destination.Kind != AgentOutputDestinationKind.ProviderExternal ||
            destination.ProviderId != AgentProviderIds.GitHubCopilotSubscription ||
            !string.Equals(context.ProviderId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal) ||
            context.SessionId is not { } sessionId || context.TurnId is not { } turnId ||
            _sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId) is not { } nativeScope ||
            !string.Equals(nativeScope.ProviderId, AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal) ||
            toolName is null || toolName == ProposeFileEditToolName)
            return null;

        var plan = nativeScope.Plan;
        var category = plan.ProductTools.Contains(toolName, StringComparer.Ordinal)
            ? AgentProductToolNames.CategoryOf(toolName) : AgentConfirmationCategories.None;
        if (category == AgentConfirmationCategories.None || category == AgentConfirmationCategories.EditProposal ||
            (plan.ConfirmationCategories & category) == 0)
            return null;

        // Do not prompt for invalid/non-object payloads, and never echo a different argument set after approval.
        var inputHash = CanonicalInputHash(argumentsJson);
        if (inputHash is null) return AgentToolInvocationResult.Failure(InvalidArguments);
        var auditToolName = toolName.StartsWith(McpToolPrefix, StringComparison.Ordinal)
            ? toolName[McpToolPrefix.Length..] : toolName;
        var intent = CreateConfirmationIntent(principal, context, destination, auditToolName, category);
        if (intent is null || !await TryAppendAuditAsync(intent).ConfigureAwait(false))
            return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyUnavailable);

        var prompt = _sessionTools?.ConfirmationPrompt;
        if (prompt is null)
        {
            await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.Rejected, expired: false,
                    unavailable: true))
                .ConfigureAwait(false);
            return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyUnavailable);
        }

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(_sessionTools!.ApprovalTimeout);
        var expired = false;
        var unavailable = false;
        AgentToolConfirmationDecision decision;
        try
        {
            decision = await prompt.ConfirmAsync(new AgentToolConfirmationRequest(nativeScope.ConversationId, nativeScope.ProviderId,
                toolName, category, argumentsJson!, null), window.Token).WaitAsync(window.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.Rejected, expired: true))
                .ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
            decision = AgentToolConfirmationDecision.Rejected;
            expired = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            decision = AgentToolConfirmationDecision.Rejected;
            unavailable = true;
        }

        // Copilot can approve one invocation only. ApprovedThisSession is treated as a rejection here.
        var approved = false;
        if (decision == AgentToolConfirmationDecision.ApprovedOnce)
        {
            try
            {
                approved = await CheckPrincipalCurrentAsync(principal, window.Token).ConfigureAwait(false) is null &&
                    !window.IsCancellationRequested &&
                    ReferenceEquals(_sessionTools?.NativeChatTurnScopes?.Find(sessionId, turnId), nativeScope) &&
                    !nativeScope.Plan.IsBlocked && nativeScope.Plan.ProductTools.Contains(toolName, StringComparer.Ordinal) &&
                    AgentProductToolNames.CategoryOf(toolName) == category &&
                    (nativeScope.Plan.ConfirmationCategories & category) != 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TryAppendAuditAsync(ConfirmationTerminal(intent, AgentToolConfirmationDecision.Rejected, expired: true))
                    .ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested)
            {
                expired = true;
            }
        }
        var effective = approved ? AgentToolConfirmationDecision.ApprovedOnce : AgentToolConfirmationDecision.Rejected;
        if (!await TryAppendAuditAsync(ConfirmationTerminal(intent, effective, expired: expired ||
                decision == AgentToolConfirmationDecision.ApprovedOnce && !approved, unavailable: unavailable)).ConfigureAwait(false))
            return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyUnavailable);
        if (!approved)
        {
            if (unavailable)
                return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyUnavailable);
            if (expired)
                return AgentToolInvocationResult.Failure(ConfirmationExpired, AgentAuditDecisionReason.ApprovalExpired);
            if (decision is AgentToolConfirmationDecision.Rejected or AgentToolConfirmationDecision.ApprovedThisSession)
                return AgentToolInvocationResult.Failure(ConfirmationRejected, AgentAuditDecisionReason.ApprovalRejected);
            return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyRevisionMismatch);
        }

        // The durable human decision has its own audit budget and may finish after the approval window.
        // Recording ApprovedOnce does not renew that window or authorize dispatch after it has expired.
        cancellationToken.ThrowIfCancellationRequested();
        if (window.IsCancellationRequested)
            return AgentToolInvocationResult.Failure(ConfirmationExpired, AgentAuditDecisionReason.ApprovalExpired);
        if (CanonicalInputHash(argumentsJson) != inputHash)
            return AgentToolInvocationResult.Failure(ConfirmationUnavailable, AgentAuditDecisionReason.PolicyRevisionMismatch);
        return null;
    }

    /// <summary>
    /// Server-side confirmation: when the plan of a session principal confirms the category of <paramref name="name"/>,
    /// a valid ticket for exactly these arguments must be consumed first (one shot). Null means allowed to proceed.
    /// </summary>
    private AgentToolInvocationResult? ConsumeRequiredConfirmation(AgentPrincipal? principal, string? name,
        string? argumentsJson)
    {
        if (principal is not { Origin: AgentPrincipalOrigin.External } || name is null ||
            SessionScopeOf(principal) is not { Plan: { } plan } scope) return null;
        var category = AgentProductToolNames.CategoryOf(name);
        // Edit proposals already carry their own review/apply state machine; a second permission card would be redundant.
        if (category is AgentConfirmationCategories.None or AgentConfirmationCategories.EditProposal ||
            (plan.ConfirmationCategories & category) == 0) return null;
        return CanonicalInputHash(argumentsJson) is { } hash && TryConsumeConfirmationTicket(principal.Id, scope.Generation, name, hash)
            ? null
            : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
    }

    private void IssueConfirmationTicket(Guid principalId, long generation, string toolName, string inputHash)
    {
        var tickets = _confirmationTickets.GetOrAdd(principalId, static _ => []);
        lock (tickets)
        {
            var now = DateTimeOffset.UtcNow;
            tickets.RemoveAll(ticket => ticket.ExpiresAt <= now);
            if (tickets.Count >= MaximumTicketsPerPrincipal) tickets.RemoveAt(0);
            tickets.Add(new ConfirmationTicket(generation, toolName, inputHash, now + ConfirmationTicketLifetime));
        }
    }

    private bool TryConsumeConfirmationTicket(Guid principalId, long generation, string toolName, string inputHash)
    {
        if (!_confirmationTickets.TryGetValue(principalId, out var tickets)) return false;
        lock (tickets)
        {
            var now = DateTimeOffset.UtcNow;
            tickets.RemoveAll(ticket => ticket.ExpiresAt <= now);
            var index = tickets.FindIndex(ticket => ticket.Generation == generation && ticket.ToolName == toolName &&
                CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ticket.InputHash), Encoding.ASCII.GetBytes(inputHash)));
            if (index < 0) return false;
            tickets.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// SHA-256 (hex) of the canonical form of a JSON object: properties sorted by ordinal name, no whitespace, strings
    /// re-escaped, numbers by their literal token. Duplicate names or non-object input yield null (deny).
    /// </summary>
    internal static string? CanonicalInputHash(string? json)
    {
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                if (!WriteCanonical(writer, document.RootElement)) return null;
            }
            return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        }
        catch (JsonException) { return null; }

        static bool WriteCanonical(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = element.EnumerateObject().ToArray();
                    if (properties.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                        return false;
                    writer.WriteStartObject();
                    foreach (var property in properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        if (!WriteCanonical(writer, property.Value)) return false;
                    }
                    writer.WriteEndObject();
                    return true;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                        if (!WriteCanonical(writer, item)) return false;
                    writer.WriteEndArray();
                    return true;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    return true;
                case JsonValueKind.Number:
                    writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                    return true;
                case JsonValueKind.True or JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    return true;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    return true;
                default:
                    return false;
            }
        }
    }

    private static AgentAuditEvent? CreateConfirmationIntent(AgentPrincipal principal, AgentInvocationContext context,
        AgentOutputDestination destination, string auditToolName, AgentConfirmationCategories category)
    {
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var (risk, permission) = category switch
            {
                AgentConfirmationCategories.MongoMetadataRead or AgentConfirmationCategories.WorkspaceContextRead or
                    AgentConfirmationCategories.NativeFileRead => (AgentToolRisk.ReadOnly, (AgentPermission?)AgentPermission.ReadMetadata),
                AgentConfirmationCategories.MongoDocumentRead => (AgentToolRisk.ReadOnly, (AgentPermission?)AgentPermission.ReadDocuments),
                AgentConfirmationCategories.EditProposal or AgentConfirmationCategories.NativeFileWrite =>
                    (AgentToolRisk.Write, (AgentPermission?)null),
                _ => (AgentToolRisk.Administrative, (AgentPermission?)null),
            };
            return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion, startedAt, principal.Id,
                Guid.NewGuid(), context.SessionId, context.TurnId, AuditChannelOf(destination), destination.ProviderId,
                auditToolName, 1, risk, permission, AgentAuditDecision.Requested,
                AgentAuditOutcome.Intent, principal.PolicyRevision, null, 0, 0, 0)
            {
                NamespaceKind = AgentAuditNamespaceKind.None,
                DecisionReason = AgentAuditDecisionReason.NotEvaluated,
                ApprovalState = AgentAuditApprovalState.Pending,
                ApprovalId = Guid.NewGuid(),
                StartedAtUtc = startedAt
            }.Validate();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AgentAuditEvent? ConfirmationTerminal(AgentAuditEvent intent, AgentToolConfirmationDecision decision,
        bool expired, bool unavailable = false)
    {
        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            if (completedAt < intent.StartedAtUtc) completedAt = intent.StartedAtUtc;
            var duration = ((completedAt - intent.StartedAtUtc).Ticks + TimeSpan.TicksPerMillisecond - 1) /
                TimeSpan.TicksPerMillisecond;
            var approved = decision is AgentToolConfirmationDecision.ApprovedOnce or AgentToolConfirmationDecision.ApprovedThisSession;
            return (intent with
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = completedAt,
                Decision = approved ? AgentAuditDecision.ApprovedOnce : expired || unavailable ? AgentAuditDecision.Denied : AgentAuditDecision.Rejected,
                Outcome = approved ? AgentAuditOutcome.Succeeded : AgentAuditOutcome.Denied,
                DurationMilliseconds = duration,
                ItemCount = approved ? 1 : 0,
                DecisionReason = approved ? AgentAuditDecisionReason.ApprovalGranted :
                    unavailable ? AgentAuditDecisionReason.PolicyUnavailable :
                    expired ? AgentAuditDecisionReason.ApprovalExpired : AgentAuditDecisionReason.ApprovalRejected,
                ApprovalState = approved ? AgentAuditApprovalState.ApprovedOnce :
                    unavailable ? AgentAuditApprovalState.Pending :
                    expired ? AgentAuditApprovalState.Expired : AgentAuditApprovalState.Rejected,
                ApprovedAtUtc = approved ? completedAt : null,
                CompletedAtUtc = completedAt
            }).Validate();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<bool> TryAppendAuditAsync(AgentAuditEvent? auditEvent)
    {
        if (auditEvent is null) return false;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _audit.AppendAsync(auditEvent, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    // Audit identifiers are lowercase snake case: native CLI tools are recorded as native_<name>.
    private static string NativeAuditName(string toolName) =>
        "native_" + new string(toolName.ToLowerInvariant().Where(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)).ToArray());

    private static AgentToolInvocationResult Deny(string message) =>
        AgentToolInvocationResult.Success(JsonSerializer.Serialize(new { behavior = "deny", message }));

    private static AgentToolInvocationResult Allow(string inputJson) =>
        AgentToolInvocationResult.Success("{\"behavior\":\"allow\",\"updatedInput\":" + inputJson + "}");

    private static bool CanApproveForSession(AgentConfirmationCategories category) =>
        category is AgentConfirmationCategories.MongoMetadataRead or AgentConfirmationCategories.WorkspaceContextRead or
            AgentConfirmationCategories.NativeFileRead;

    private static string SessionApprovalKey(string toolName, string inputHash) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(toolName + "\0" + inputHash)));

    /// <summary>Confirmation category of a tool the plan exposes, by the name the CLI uses; null when not planned.</summary>
    internal static AgentConfirmationCategories? CategoryOfPlannedTool(AgentTurnPlan plan, string toolName)
    {
        if (toolName.StartsWith(McpToolPrefix, StringComparison.Ordinal))
        {
            var registryName = toolName[McpToolPrefix.Length..];
            return plan.ProductTools.Contains(registryName, StringComparer.Ordinal) &&
                   AgentProductToolNames.CategoryOf(registryName) is var product && product != AgentConfirmationCategories.None
                ? product : null;
        }
        return plan.NativeTools.Contains(toolName, StringComparer.Ordinal) &&
               AgentProductToolNames.CategoryOf(toolName) is var native && native != AgentConfirmationCategories.None
            ? native : null;
    }

    /// <summary>
    /// Reads the known fields. Extra top-level fields the CLI may add are ignored and never echoed; <c>input</c> is kept
    /// as its exact raw JSON so the approved call is exactly the one shown.
    /// </summary>
    internal static bool TryParseApproveArguments(string? json, out string? toolName, out string? inputJson,
        out string? toolUseId)
    {
        toolName = null;
        inputJson = null;
        toolUseId = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                switch (property.Name)
                {
                    case "tool_name":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            property.Value.GetString() is not { Length: > 0 and <= 256 } name ||
                            name.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c)))
                            return false;
                        toolName = name;
                        break;
                    case "input":
                        if (property.Value.ValueKind != JsonValueKind.Object) return false;
                        inputJson = property.Value.GetRawText();
                        break;
                    case "tool_use_id":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            property.Value.GetString() is not { Length: <= 256 } id || id.Any(char.IsControl))
                            return false;
                        toolUseId = id;
                        break;
                }
            }
            return toolName is not null && inputJson is not null;
        }
        catch (JsonException) { return false; }
    }

    private sealed record ConfirmationTicket(long Generation, string ToolName, string InputHash, DateTimeOffset ExpiresAt);
}
