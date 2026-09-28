using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Único proprietário da conexão LiteDB local (arquivo de workspace). Implementa todos os repositórios locais
/// (perfis de conexão, históricos, consultas salvas, auditoria, sessão e cofre de ambientes) como facetas do
/// mesmo arquivo físico — nunca abra uma segunda conexão ao mesmo banco. Cada faceta vive em seu próprio
/// arquivo parcial, mas compartilha <see cref="_database"/>, o cadeado <see cref="_gate"/> e os utilitários de
/// execução assíncrona definidos aqui.
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository :
    IConnectionProfileRepository,
    IConnectionProfileCredentialStatusProvider,
    IQueryHistoryRepository,
    IScriptHistoryRepository,
    ISavedQueryRepository,
    IAuditRepository,
    IWorkspaceSessionRepository,
    IEnvironmentVaultRepository,
    ILegacyCredentialInventoryRepository,
    IConsoleHistoryRepository,
    IAgentAuthorizationPolicyRepository,
    IAgentAuditRepository,
    IAgentSchemaSamplingConsentProvider,
    IAgentSchemaSamplingConsentRepository,
    IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ISecretStore? _profileSecrets;
    private readonly object _gate = new();
    private bool _disposed;

    public LiteDbConnectionProfileRepository(string databasePath, ISecretStore? profileSecrets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _profileSecrets = profileSecrets;
        var directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _database = new LiteDatabase($"Filename={databasePath};Connection=direct");
        try { _environmentJson = _database.GetCollection("environmentVault").FindById("current")?["json"].AsString; }
        catch (Exception ex) { _environmentReadError = ex; }
        lock (_gate)
        {
            var collection = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
            collection.EnsureIndex(profile => profile.Name, unique: true);
            _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName)
                .EnsureIndex(entry => entry.ExecutedAtUtc);
            _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName)
                .EnsureIndex(entry => entry.LastAccessedUtcTicks);
            _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName)
                .EnsureIndex(entry => entry.UpdatedAtUtcTicks);
            _database.GetCollection<AuditEntryDocument>(AuditCollectionName)
                .EnsureIndex(entry => entry.OccurredAtUtcTicks);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _database.Dispose();
            _disposed = true;
        }
    }

    private Task RunAsync(Action action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                action();
            }
        }, cancellationToken);

    private Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                return action();
            }
        }, cancellationToken);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
