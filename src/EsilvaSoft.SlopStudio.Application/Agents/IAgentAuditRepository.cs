using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Append-only local ledger. A failed append must prevent a future agent operation from proceeding.</summary>
public interface IAgentAuditRepository
{
    Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default);

    /// <summary>Unresolved intents for broker-led reconciliation. Reading them never claims an outcome.</summary>
    Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default);
}
