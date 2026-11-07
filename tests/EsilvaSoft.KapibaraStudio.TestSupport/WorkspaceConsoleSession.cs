using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

internal sealed class WorkspaceConsoleSession(IMongoWorkspaceService mongo) : IConsoleDatabaseSessionFactory, IConsoleDatabaseSession
{
    private IReadOnlyList<ConnectionProfile> _profiles = [];
    private int _documentLimit;
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) => new WorkspaceConsoleSession(mongo) { _profiles = resolvedProfiles, _documentLimit = documentLimit };
    public async Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken)
    {
        using var args = System.Text.Json.JsonDocument.Parse(operation.ArgumentsJson);
        var root = args.RootElement;
        var profile = _profiles.Single(p => p.Id == operation.ProfileId);
        var page = operation.Method switch
        {
            "find" => await mongo.QueryAsync(profile, new MongoQuery(operation.Database, operation.Collection, root[0].GetRawText(), Limit: root[2].GetProperty("limit").GetInt32()), cancellationToken),
            "aggregate" => await mongo.AggregateAsync(profile, new AggregationQuery(operation.Database, operation.Collection, root[0].GetRawText(), _documentLimit), cancellationToken),
            _ => throw new NotSupportedException(operation.Method)
        };
        return "{\"value\":[" + string.Join(",", page.Documents) + "],\"truncated\":" + (page.IsTruncated ? "true" : "false") + "}";
    }
    public void Dispose() { }
}
