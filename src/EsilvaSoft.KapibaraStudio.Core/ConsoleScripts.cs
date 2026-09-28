using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public static class ConsoleScripts
{
    public static string Find(string collection) => "db.getCollection(" + JsonSerializer.Serialize(collection) + ").find({}).limit(100)";
    public static string FromQuery(MongoQuery query)
    {
        var script = "db.getCollection(" + JsonSerializer.Serialize(query.Collection) + ").find(" + query.FilterJson + ")";
        if (!string.IsNullOrWhiteSpace(query.ProjectionJson)) script += ".project(" + query.ProjectionJson + ")";
        if (!string.IsNullOrWhiteSpace(query.SortJson)) script += ".sort(" + query.SortJson + ")";
        if (!string.IsNullOrWhiteSpace(query.HintJson)) script += ".hint(" + query.HintJson + ")";
        if (!string.IsNullOrWhiteSpace(query.CollationJson)) script += ".collation(" + query.CollationJson + ")";
        if (!string.IsNullOrWhiteSpace(query.Comment)) script += ".comment(" + JsonSerializer.Serialize(query.Comment) + ")";
        if (query.BatchSize is { } batch) script += $".batchSize({batch})";
        if (query.MaxTimeMs is { } time) script += $".maxTimeMS({time})";
        return script + $".skip({query.Skip}).limit({query.Limit})";
    }
}
