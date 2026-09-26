using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// <c>approve</c>: the permission-prompt tool of the Claude Code CLI
/// (<c>--permission-prompt-tool mcp__slopstudio__approve</c>). Contract of the CLI: the input is
/// <c>{"tool_name": string, "input": object, "tool_use_id"?: string}</c>; the answer is a JSON object, returned as the
/// text content of the MCP result, either <c>{"behavior":"allow","updatedInput":&lt;input&gt;}</c> or
/// <c>{"behavior":"deny","message":string}</c>.
/// <para>
/// Enforced on the server, not trusted to the CLI: a card opens only for a tool of the current turn plan whose category
/// the plan confirms; the human decision is appended to the durable audit ledger (intent before the card, terminal after)
/// and a failed append denies. "Aprovar uma vez" of a product tool issues a one-shot ticket bound to (principal, registry
/// tool, SHA-256 of the canonical input) that expires after <see cref="ConfirmationTicketLifetime"/>; the registry
/// consumes it before executing that tool, and a product tool whose category requires confirmation never runs without
/// one. There is no "always" answer. Native CLI tools (Read/Glob/Grep) get the decision only (they run in the CLI).
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

    internal const string ApproveDeniedByUser = "O usuário rejeitou esta operação no Slop Studio.";
    internal const string ApproveTimedOut = "A confirmação expirou sem resposta do usuário; a operação não foi executada.";
    internal const string ApproveUnavailable = "Confirmação indisponível no Slop Studio; a operação foi negada.";
    internal const string ApproveNotInPlan = "Esta operação não está liberada para confirmação no turno atual.";
    internal const string ApproveInvalid = "Pedido de confirmação inválido; a operação foi negada.";

    /// <summary>Lifetime of an approval ticket: the CLI calls the approved tool right after the answer.</summary>
    internal static readonly TimeSpan ConfirmationTicketLifetime = TimeSpan.FromSeconds(60);
    private const int MaximumTicketsPerPrincipal = 16;
    private const string McpToolPrefix = "mcp__slopstudio__";

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
        var inputHash = registryTool is null ? null : CanonicalInputHash(inputJson);
        if (registryTool is not null && inputHash is null) return Deny(ApproveInvalid);
        if (await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is not null)
            return Deny(ApproveUnavailable);
        if (_sessionTools?.ConfirmationPrompt is not { } prompt) return Deny(ApproveUnavailable);

        // Durable intent before the human sees anything; without the ledger nothing is asked.
        var intent = CreateConfirmationIntent(principal, context!, destination, registryTool ?? NativeAuditName(toolName));
        if (intent is null || !await TryAppendAuditAsync(intent).ConfigureAwait(false)) return Deny(ApproveUnavailable);

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
        var stillPlanned = decision == AgentToolConfirmationDecision.ApprovedOnce &&
            await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is null &&
            SessionScopeOf(principal) is { Plan: { } current } && CategoryOfPlannedTool(current, toolName) is not null;
        var effective = stillPlanned ? AgentToolConfirmationDecision.ApprovedOnce : AgentToolConfirmationDecision.Rejected;
        if (!await TryAppendAuditAsync(ConfirmationTerminal(intent, effective,
                expired: expired || decision == AgentToolConfirmationDecision.ApprovedOnce && !stillPlanned))
                .ConfigureAwait(false))
            return Deny(ApproveUnavailable);
        if (effective != AgentToolConfirmationDecision.ApprovedOnce)
            return Deny(expired ? ApproveTimedOut : decision == AgentToolConfirmationDecision.ApprovedOnce
                ? ApproveNotInPlan : ApproveDeniedByUser);

        if (registryTool is not null) IssueConfirmationTicket(principal.Id, registryTool, inputHash!);
        var builder = new StringBuilder("{\"behavior\":\"allow\",\"updatedInput\":").Append(inputJson).Append('}');
        return AgentToolInvocationResult.Success(builder.ToString());
    }

    /// <summary>
    /// Server-side confirmation: when the plan of a session principal confirms the category of <paramref name="name"/>,
    /// a valid ticket for exactly these arguments must be consumed first (one shot). Null means allowed to proceed.
    /// </summary>
    private AgentToolInvocationResult? ConsumeRequiredConfirmation(AgentPrincipal? principal, string? name,
        string? argumentsJson)
    {
        if (principal is null || name is null || SessionScopeOf(principal) is not { Plan: { } plan }) return null;
        var category = AgentProductToolNames.CategoryOf(name);
        if (category == AgentConfirmationCategories.None || (plan.ConfirmationCategories & category) == 0) return null;
        return CanonicalInputHash(argumentsJson) is { } hash && TryConsumeConfirmationTicket(principal.Id, name, hash)
            ? null
            : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
    }

    private void IssueConfirmationTicket(Guid principalId, string toolName, string inputHash)
    {
        var tickets = _confirmationTickets.GetOrAdd(principalId, static _ => []);
        lock (tickets)
        {
            var now = DateTimeOffset.UtcNow;
            tickets.RemoveAll(ticket => ticket.ExpiresAt <= now);
            if (tickets.Count >= MaximumTicketsPerPrincipal) tickets.RemoveAt(0);
            tickets.Add(new ConfirmationTicket(toolName, inputHash, now + ConfirmationTicketLifetime));
        }
    }

    private bool TryConsumeConfirmationTicket(Guid principalId, string toolName, string inputHash)
    {
        if (!_confirmationTickets.TryGetValue(principalId, out var tickets)) return false;
        lock (tickets)
        {
            var now = DateTimeOffset.UtcNow;
            tickets.RemoveAll(ticket => ticket.ExpiresAt <= now);
            var index = tickets.FindIndex(ticket => ticket.ToolName == toolName &&
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
        AgentOutputDestination destination, string auditToolName)
    {
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            return new AgentAuditEvent(Guid.NewGuid(), AgentAuditEvent.CurrentSchemaVersion, startedAt, principal.Id,
                Guid.NewGuid(), context.SessionId, context.TurnId, AuditChannelOf(destination), destination.ProviderId,
                auditToolName, 1, AgentToolRisk.ReadOnly, AgentPermission.ReadMetadata, AgentAuditDecision.Requested,
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
        bool expired)
    {
        try
        {
            var completedAt = DateTimeOffset.UtcNow;
            if (completedAt < intent.StartedAtUtc) completedAt = intent.StartedAtUtc;
            var duration = ((completedAt - intent.StartedAtUtc).Ticks + TimeSpan.TicksPerMillisecond - 1) /
                TimeSpan.TicksPerMillisecond;
            var approved = decision == AgentToolConfirmationDecision.ApprovedOnce;
            return (intent with
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = completedAt,
                Decision = approved ? AgentAuditDecision.ApprovedOnce : expired ? AgentAuditDecision.Denied : AgentAuditDecision.Rejected,
                Outcome = approved ? AgentAuditOutcome.Succeeded : AgentAuditOutcome.Denied,
                DurationMilliseconds = duration,
                ItemCount = approved ? 1 : 0,
                DecisionReason = approved ? AgentAuditDecisionReason.ApprovalGranted :
                    expired ? AgentAuditDecisionReason.ApprovalExpired : AgentAuditDecisionReason.ApprovalRejected,
                ApprovalState = approved ? AgentAuditApprovalState.ApprovedOnce :
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

    private sealed record ConfirmationTicket(string ToolName, string InputHash, DateTimeOffset ExpiresAt);
}
