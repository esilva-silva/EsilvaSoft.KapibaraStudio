using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Dedicated query planner explain. Only a structural allowlist leaves the adapter.</summary>
public sealed class MongoAgentExplainSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    MongoClientPool clients) : IAgentMongoExplainSource
{
    private const int MaximumRawBytes = 256 * 1024;

    public async Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile,
        AgentMongoFindQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaxTimeMs is < 1 or > 30_000 || query.Limit is < 1 or > 100 ||
            query.Skip is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
            profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
            profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
            profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Dynamic targets are not supported for agent explain.");

        var filter = MongoAgentFindSource.ParseLiteral(query.FilterEjson);
        var projection = query.ProjectionEjson is null ? null : MongoAgentFindSource.ParseLiteral(query.ProjectionEjson);
        var sort = query.SortEjson is null ? null : MongoAgentFindSource.ParseLiteral(query.SortEjson);
        var command = BuildExplainCommand(query, filter, projection, sort);
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            cancellationToken).ConfigureAwait(false);
        var target = context.CreateClient().GetDatabase(query.Database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (originalUuid is null) return new("{}", false, false);

        var response = await target.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var rawTooLarge = response.ToBson().Length > MaximumRawBytes;
        string? ejson = null;
        if (!rawTooLarge)
        {
            if (!response.TryGetValue("queryPlanner", out var plannerValue) ||
                plannerValue is not BsonDocument planner ||
                !planner.TryGetValue("winningPlan", out var planValue) || planValue is not BsonDocument plan)
                return new("{}", false, false);
            var sanitized = SanitizeServerPlan(plan);
            ejson = sanitized.ToJson(MongoJson.CanonicalSettings);
            rawTooLarge = System.Text.Encoding.UTF8.GetByteCount(ejson) > MaximumRawBytes;
        }
        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new("{}", false, false);
        return rawTooLarge ? new("{}", true, true) : new(ejson!, true, false);
    }

    internal static BsonDocument BuildExplainCommand(AgentMongoFindQuery query, BsonDocument filter,
        BsonDocument? projection, BsonDocument? sort)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filter);
        var find = new BsonDocument
        {
            ["find"] = query.Collection,
            ["filter"] = filter,
            ["limit"] = query.Limit,
            ["skip"] = query.Skip,
            ["maxTimeMS"] = query.MaxTimeMs
        };
        if (projection is not null) find["projection"] = projection;
        if (sort is not null) find["sort"] = sort;
        return new BsonDocument
        {
            ["explain"] = find,
            ["verbosity"] = "queryPlanner",
            ["maxTimeMS"] = query.MaxTimeMs
        };
    }

    internal static BsonDocument SanitizeServerPlan(BsonDocument plan)
    {
        try
        {
            var nodes = 0;
            return SanitizePlan(plan, ref nodes);
        }
        catch (FormatException)
        {
            // An unsupported server plan is not a malformed caller argument. Do not echo it.
            throw new InvalidDataException("Unsupported query plan returned by the server.");
        }
    }

    internal static BsonDocument SanitizePlan(BsonDocument source, ref int nodes)
    {
        if (++nodes > 200) throw new FormatException("Query plan too deep.");
        var result = new BsonDocument();
        foreach (var element in source.Elements)
        {
            switch (element.Name)
            {
                case "stage" or "indexName" when element.Value.IsString &&
                    element.Value.AsString.Length is > 0 and <= 1024:
                    result[element.Name] = element.Value.AsString;
                    break;
                case "direction" when element.Value.IsString &&
                    element.Value.AsString is "forward" or "backward":
                    result[element.Name] = element.Value.AsString;
                    break;
                case "isMultiKey" when element.Value.IsBoolean:
                    result[element.Name] = element.Value.AsBoolean;
                    break;
                case "inputStage" or "outerStage" or "innerStage" or "queryPlan" when
                    element.Value is BsonDocument child:
                    result[element.Name] = SanitizePlan(child, ref nodes);
                    break;
                case "inputStages" when element.Value is BsonArray children:
                    var projected = new BsonArray();
                    foreach (var item in children)
                    {
                        if (item is not BsonDocument nested) throw new FormatException("Invalid query plan.");
                        projected.Add(SanitizePlan(nested, ref nodes));
                    }
                    result[element.Name] = projected;
                    break;
            }
        }
        if (result.ElementCount == 0) throw new FormatException("Unsupported query plan.");
        return result;
    }
}
