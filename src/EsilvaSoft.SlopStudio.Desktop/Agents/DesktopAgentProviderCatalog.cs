using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Production, provider-neutral implementation of <see cref="IAgentProviderCatalog"/> for the Desktop composition
/// root (P7-L06-WIRING). It only wraps the shared <see cref="AgentProviderCatalog"/> already composed by
/// <c>AddSlopStudioInfrastructure</c>: every field it publishes comes from the descriptor/status contracts, never
/// from a provider ID comparison, so a brand-new registered provider (AC-09) is presented the same way as the
/// built-in ones without touching this type.
/// </summary>
/// <remarks>
/// <see cref="List"/> only reads its own cached snapshot, honoring the port's contract that listing must not touch
/// the vault or the network: construction seeds the snapshot with <see cref="AgentProviderStatus.NotReported"/> for
/// every registered provider. <see cref="RefreshAsync"/> is the explicit action — nothing calls it automatically —
/// that awaits <see cref="AgentProviderCatalog.GetStatusAsync"/> for each provider and swaps the snapshot atomically.
/// A provider whose status check fails is reported unavailable with the safe code that call already returns; this
/// type never branches on a provider ID to decide what to publish.
/// </remarks>
public sealed class DesktopAgentProviderCatalog : IAgentProviderCatalog
{
    private readonly AgentProviderCatalog _catalog;
    private readonly IReadOnlyDictionary<string, string> _families;
    private readonly IReadOnlySet<string> _experimentalProviders;
    private readonly Lock _gate = new();
    private volatile IReadOnlyList<AgentProviderPresentation> _snapshot;
    private long _refreshGeneration;

    /// <param name="catalog">Shared catalog composed by the infrastructure.</param>
    /// <param name="families">
    /// Optional display data of the composition root (provider ID → family label for the mode chip, e.g. two modes of
    /// the same model family). It never decides behavior; a provider without an entry shows its display name.
    /// </param>
    /// <param name="experimentalProviders">Provider IDs whose UI must carry an explicit experimental label.</param>
    public DesktopAgentProviderCatalog(AgentProviderCatalog catalog, IReadOnlyDictionary<string, string>? families = null,
        IReadOnlySet<string>? experimentalProviders = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _families = families ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _experimentalProviders = experimentalProviders ?? new HashSet<string>(StringComparer.Ordinal);
        _snapshot = Present(catalog.List(), _ => AgentProviderStatus.NotReported);
    }

    public IReadOnlyList<AgentProviderPresentation> List() => _snapshot;

    /// <summary>
    /// Refreshes the cached snapshot from the current local configuration and vault presence of every registered
    /// provider. <see cref="AgentProviderCatalog.GetStatusAsync"/> already fails closed per provider, so one
    /// unavailable/failing provider never affects the others; only the caller's own cancellation propagates.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Two tabs may refresh concurrently: only the most recently started refresh publishes, so an older, slower
        // check never overwrites a newer result (e.g. a key saved between both checks).
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var entries = _catalog.List();
        var statuses = new Dictionary<string, AgentProviderStatus>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            statuses[entry.Descriptor.ProviderId] =
                await _catalog.GetStatusAsync(entry.Descriptor.ProviderId, cancellationToken).ConfigureAwait(false);
        }

        var refreshed = Present(entries, id => statuses[id]);
        lock (_gate)
        {
            if (generation == Interlocked.Read(ref _refreshGeneration))
            {
                _snapshot = refreshed;
            }
        }
    }

    /// <summary>
    /// Re-checks one provider only (settings "Test connection") and replaces just its entry in the snapshot; the
    /// other providers keep their last report and their vault slots are not read.
    /// </summary>
    public async Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var entry = _catalog.List().FirstOrDefault(e => string.Equals(e.Descriptor.ProviderId, providerId, StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        var status = await _catalog.GetStatusAsync(providerId, cancellationToken).ConfigureAwait(false);
        var updated = PresentOne(entry, status);
        lock (_gate)
        {
            _snapshot = [.. _snapshot.Select(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal) ? updated : item)];
        }
    }

    private IReadOnlyList<AgentProviderPresentation> Present(
        IReadOnlyList<AgentProviderEntry> entries, Func<string, AgentProviderStatus> statusOf) =>
        [.. entries.Select(entry => PresentOne(entry, statusOf(entry.Descriptor.ProviderId)))];

    private AgentProviderPresentation PresentOne(AgentProviderEntry entry, AgentProviderStatus status) =>
        AgentProviderPresentation.From(entry, status, status.IsAvailable ? null : status.UnavailableCode,
            _families.TryGetValue(entry.Descriptor.ProviderId, out var family) ? family : null) with
        {
            IsExperimental = _experimentalProviders.Contains(entry.Descriptor.ProviderId),
        };
}
