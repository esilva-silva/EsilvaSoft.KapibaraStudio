using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Durable schema sampling consents owned by the workspace database. These are the ports a future UI uses to list,
/// grant and revoke consent; the runtime query path used by the tool registry is
/// <see cref="IAgentSchemaSamplingConsentProvider"/>, evaluated separately from this port so that reading it can
/// never itself widen access. Missing consent denies sampling; this repository never grants access implicitly.
/// </summary>
public interface IAgentSchemaSamplingConsentRepository
{
    /// <summary>Current consents for one principal, or null when none were ever granted.</summary>
    /// <exception cref="InvalidDataException">The stored consents are corrupt or have an unsupported schema.</exception>
    Task<AgentSchemaSamplingConsentSnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the full set of consents at the expected revision (zero for a principal that never granted any),
    /// assigning the next revision. A future UI computes the new list from a fresh <see cref="LoadAsync"/> — granting
    /// adds or replaces the entry for one namespace/generation pair, revoking removes matching entries — so two
    /// concurrent writers reading the same revision cannot silently overwrite each other: only one wins, and the
    /// other must reload before retrying.
    /// </summary>
    /// <exception cref="AgentSchemaSamplingConsentConcurrencyException">The stored revision changed.</exception>
    /// <exception cref="InvalidDataException">The stored consents are corrupt or have an unsupported schema.</exception>
    Task<AgentSchemaSamplingConsentSnapshot> SaveAsync(
        Guid principalId,
        IReadOnlyList<AgentSchemaSamplingConsentGrant> consents,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
