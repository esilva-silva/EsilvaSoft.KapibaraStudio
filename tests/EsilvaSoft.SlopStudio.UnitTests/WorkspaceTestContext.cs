using System.Reflection;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

internal sealed class WorkspaceTestContext : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "slop-ui-" + Guid.NewGuid().ToString("N"));
    public LiteDbConnectionProfileRepository Repository { get; }
    public WorkspaceService Workspace { get; }
    public ControlledScripts Scripts { get; } = new();
    public MongoTestProxy Mongo { get; }
    public WorkspaceTestContext(IConsoleHistoryRepository? historyOverride = null, ITextFileService? textFiles = null,
        ISecretStore? profileSecrets = null)
    {
        Repository = new LiteDbConnectionProfileRepository(Path.Combine(_directory, "workspace.db"),
            profileSecrets ?? new InMemoryProfileSecretStore());
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        Mongo = (MongoTestProxy)mongo;
        Mongo.Handler = (name, _) => name switch
        {
            "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["loja", "auditoria"]),
            "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["clientes", "pedidos"]),
            _ => throw new NotSupportedException(name)
        };
        var secrets = new SessionConnectionSecretStore();
        var console = new ConsoleRuntime(Repository, Repository, secrets, new WorkspaceConsoleSession(mongo), Repository, Repository);
        Workspace = new WorkspaceService(Repository, Repository, Repository, Repository, Repository, mongo, Scripts, new LocalScriptFileService(), secrets, Repository, new ExplorerMetadataService(mongo), console, historyOverride ?? Repository, formatter: new MongoCodeFormatter(), validator: new MongoCodeValidator(), textFiles: textFiles);
    }
    public void Dispose() { Repository.Dispose(); Directory.Delete(_directory, true); }
}
