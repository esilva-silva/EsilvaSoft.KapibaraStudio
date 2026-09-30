using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

internal sealed class WorkspaceConsoleSession(IMongoWorkspaceService mongo) : IConsoleDatabaseSessionFactory, IConsoleDatabaseSession
{
    private IReadOnlyList<ConnectionProfile> _profiles = [];
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) => new WorkspaceConsoleSession(mongo) { _profiles = resolvedProfiles };
    public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
    {
        using var args = System.Text.Json.JsonDocument.Parse(operation.ArgumentsJson);
        var root = args.RootElement;
        if (operation.Method != "find") throw new NotSupportedException(operation.Method);
        var query = new MongoQuery(operation.Database, operation.Collection, root[0].GetRawText(), Limit: root[2].GetProperty("limit").GetInt32());
        var page = await mongo.QueryAsync(_profiles.Single(p => p.Id == operation.ProfileId), query, cancellationToken);
        return "{\"value\":[" + string.Join(",", page.Documents) + "],\"truncated\":" + (page.IsTruncated ? "true" : "false") + "}";
    }
    public void Dispose() { }
}
