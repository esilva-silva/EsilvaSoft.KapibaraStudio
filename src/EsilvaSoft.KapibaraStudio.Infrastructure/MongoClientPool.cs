using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Application-lifetime clients, keyed by effective settings. Never evicts a client used by an active operation.</summary>
public sealed class MongoClientPool : IMongoClientPool, IDisposable
{
    internal static MongoClientPool Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<MongoClientSettings, MongoClient> _clients = [];
    private bool _disposed;
    public const int MaximumClients = 64;

    IMongoClient IMongoClientPool.GetClient(MongoClientSettings settings) => Get(settings);

    public MongoClient Get(MongoClientSettings settings)
    {
        var key = settings.FrozenCopy();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clients.TryGetValue(key, out var client)) return client;
            if (_clients.Count >= MaximumClients)
                throw new InvalidOperationException("Limite de 64 configurações MongoDB nesta sessão. Reinicie a IDE para liberar os clientes inativos.");
            client = new MongoClient(key);
            _clients.Add(key, client);
            return client;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var client in _clients.Values) client.Dispose();
            _clients.Clear();
        }
    }
}
