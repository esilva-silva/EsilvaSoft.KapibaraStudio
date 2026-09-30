using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal sealed class WorkspaceTestContext : IDisposable
{
    public MemoryWorkspaceRepository Repository { get; }
    public WorkspaceService Workspace { get; }
    public ControlledScripts Scripts { get; } = new();
    public MongoTestProxy Mongo { get; }
    public WorkspaceTestContext(IConsoleHistoryRepository? historyOverride = null, ITextFileService? textFiles = null,
        ITextExportFileService? textExports = null)
    {
        Repository = new MemoryWorkspaceRepository();
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
        Workspace = new WorkspaceService(Repository, Repository, Repository, Repository, Repository, mongo, Scripts, new MemoryTextFiles(), secrets, Repository, new ExplorerMetadataService(mongo), console, historyOverride ?? Repository, formatter: new MongoCodeFormatter(), validator: new MongoCodeValidator(), textFiles: textFiles, textExports: textExports);
    }
    public void Dispose() => Repository.Dispose();
}
