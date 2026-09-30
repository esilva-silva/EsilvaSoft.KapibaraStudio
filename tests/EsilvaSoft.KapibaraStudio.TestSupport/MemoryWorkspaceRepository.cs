using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Stores synthetic repository responses in memory. It does not emulate LiteDB, migrations or OS secrets.</summary>
internal sealed class MemoryWorkspaceRepository : IConnectionProfileRepository, IQueryHistoryRepository,
    IScriptHistoryRepository, ISavedQueryRepository, IAuditRepository, IWorkspaceSessionRepository,
    IEnvironmentVaultRepository, IConsoleHistoryRepository, IConnectionProfileCredentialStatusProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ConnectionProfile> _profiles = [];
    private readonly Dictionary<Guid, QueryHistoryEntry> _queries = [];
    private readonly Dictionary<Guid, ScriptHistoryEntry> _scripts = [];
    private readonly Dictionary<Guid, SavedQuery> _saved = [];
    private readonly Dictionary<Guid, AuditEntry> _audit = [];
    private readonly Dictionary<Guid, ConsoleHistoryEntry> _console = [];
    private WorkspaceSession _session = new();
    private EnvironmentVault _environments = EnvironmentVault.CreateDefault();
    private bool _disposed;

    public Exception? SessionReadFailure { get; set; }
    public Exception? SessionWriteFailure { get; set; }
    public int SessionWriteAttempts { get; private set; }

    private Task<T> Read<T>(Func<T> read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Task.FromResult(read());
        }
    }
    private Task Write(Action write, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            write();
            return Task.CompletedTask;
        }
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<ConnectionProfile>>(() => _profiles.Values.OrderBy(profile => profile.Name, StringComparer.Ordinal).ToArray(), cancellationToken);
    public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => Write(() => _profiles[profile.Id] = profile, cancellationToken);
    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => Write(() => _profiles.Remove(profileId), cancellationToken);
    public Task<IReadOnlyList<QueryHistoryEntry>> GetRecentAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<QueryHistoryEntry>>(() => _queries.Values.Where(entry => profileId is null || entry.ProfileId == profileId)
            .OrderByDescending(entry => entry.ExecutedAt).Take(maximum).ToArray(), cancellationToken);
    public Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default) => Write(() => _queries[entry.Id] = entry, cancellationToken);
    public Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentAsync(int maximum = 50, CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<ScriptHistoryEntry>>(() => _scripts.Values.OrderByDescending(entry => entry.LastAccessedAt).Take(maximum).ToArray(), cancellationToken);
    public Task SaveAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default) => Write(() => _scripts[entry.Id] = entry, cancellationToken);
    public Task<IReadOnlyList<SavedQuery>> GetAllAsync(Guid? profileId, CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<SavedQuery>>(() => _saved.Values.Where(entry => profileId is null || entry.ProfileId == profileId).ToArray(), cancellationToken);
    public Task SaveAsync(SavedQuery query, CancellationToken cancellationToken = default) => Write(() => _saved[query.Id] = query, cancellationToken);
    public Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default) => Write(() => _saved.Remove(id), cancellationToken);
    public Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<AuditEntry>>(() => _audit.Values.OrderByDescending(entry => entry.OccurredAt).Take(maximum).ToArray(), cancellationToken);
    public Task SaveAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Write(() => _audit[entry.Id] = entry, cancellationToken);
    public Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default) => Read(() =>
    {
        if (SessionReadFailure is { } failure) throw failure;
        return Copy(_session);
    }, cancellationToken);
    public Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default) => Write(() =>
    {
        SessionWriteAttempts++;
        if (SessionWriteFailure is { } failure) throw failure;
        _session = Copy(session);
    }, cancellationToken);
    public EnvironmentVault LoadEnvironments() { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return Copy(_environments); } }
    public void SaveEnvironments(EnvironmentVault vault) { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _environments = Copy(vault); } }
    public Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default) => Write(() => _console[entry.Id] = Copy(entry), cancellationToken);
    public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
        Read<IReadOnlyList<ConsoleHistoryEntry>>(() => _console.Values.OrderByDescending(entry => entry.ExecutedAt).Take(maximum).Select(Copy).ToArray(), cancellationToken);
    public Task<bool> HasPendingCredentialCleanupAsync(Guid profileId, CancellationToken cancellationToken = default) => Read(() => false, cancellationToken);
    public Task<int> CountPendingCredentialRecoveryAsync(CancellationToken cancellationToken = default) => Read(() => 0, cancellationToken);
    public void Dispose() { lock (_gate) _disposed = true; }
}
