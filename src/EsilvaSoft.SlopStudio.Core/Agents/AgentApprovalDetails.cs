namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Trusted description of one pending approval, produced by the approval authority (registry/broker) from the frozen
/// proposal and bound to the approval ID it was queried with. It is never derived from model text.
/// <paramref name="ConnectionLabel"/> is the logical connection label shown locally; <paramref name="Target"/> and
/// <paramref name="Change"/> are sanitized, bounded renderings of the frozen filter/identity and diff.
/// <paramref name="ExpiresAtUtc"/> is for display; enforcement stays monotonic in the runtime.
/// </summary>
public sealed record AgentApprovalDetails(
    string ToolName,
    string ConnectionLabel,
    string Database,
    string Collection,
    string Target,
    string? Change,
    int? AffectedLimit,
    AgentToolRisk Risk,
    DateTimeOffset ExpiresAtUtc);
