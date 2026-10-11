using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Observes actual driver commands from the production query/index services, without retaining payloads.</summary>
internal sealed class FixtureMongoAccess : IMongoClientPool, IDisposable
{
    private readonly MongoClientPool _pool = new();
    private readonly Action<ClusterBuilder> _configure;
    private readonly ConcurrentQueue<Command> _commands = new();
    private int _toolExecutionActive;
    internal bool ToolExecutionActive
    {
        get => Volatile.Read(ref _toolExecutionActive) != 0;
        set => Volatile.Write(ref _toolExecutionActive, value ? 1 : 0);
    }
    internal Command[] Commands => _commands.ToArray();
    internal sealed record Command(string Name, string Database, bool Prohibited);

    internal FixtureMongoAccess()
    {
        _configure = builder => builder.Subscribe<CommandStartedEvent>(evt =>
        {
            if (!ToolExecutionActive) return;
            // Connection handshakes do not access a database namespace or documents.
            if (evt.CommandName is "isMaster" or "ismaster" or "hello") return;
            var allowed = evt.CommandName is "listCollections" or "listIndexes" or "getMore" or "killCursors" ||
                evt.CommandName == "aggregate" && evt.Command.TryGetValue("pipeline", out var pipeline) &&
                pipeline.IsBsonArray && pipeline.AsBsonArray.Count == 1 && pipeline.AsBsonArray[0].IsBsonDocument &&
                pipeline.AsBsonArray[0].AsBsonDocument.Contains("$listSearchIndexes");
            _commands.Enqueue(new(evt.CommandName, evt.DatabaseNamespace.DatabaseName, !allowed));
        });
    }
    public IMongoClient GetClient(MongoClientSettings settings)
    {
        if (settings.DirectConnection != true || settings.Servers.Any(server =>
            !System.Net.IPAddress.TryParse(server.Host.Trim('[', ']'), out var address) || !System.Net.IPAddress.IsLoopback(address)))
            throw new InvalidDataException("Client de fixture exige conexão direta loopback.");
        var observed = settings.Clone();
        observed.ClusterConfigurator = _configure;
        return _pool.Get(observed);
    }
    internal void BeginTools()
    {
        _commands.Clear();
        ToolExecutionActive = true;
    }
    internal Command[] EndTools()
    {
        ToolExecutionActive = false;
        return Commands;
    }
    public void Dispose() => _pool.Dispose();
}
