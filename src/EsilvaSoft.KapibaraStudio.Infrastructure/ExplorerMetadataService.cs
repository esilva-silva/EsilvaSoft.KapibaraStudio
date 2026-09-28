using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed class ExplorerMetadataService(IMongoWorkspaceService mongo) : IExplorerMetadataService
{
    private static readonly JsonWriterSettings Json = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };
    private Func<string, string>? _localize;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;

    public async Task<TopologyInfo> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var info = ParseTopology(await mongo.GetTopologyAsync(profile, cancellationToken).ConfigureAwait(false));
        if (info.Server.Length > 0) return Localize(info);
        var endpoint = profile.TargetHost ?? profile.Endpoint;
        var instances = info.Instances;
        if (instances.Count == 0 && info.Kind == "Standalone" && !endpoint.Contains(',', StringComparison.Ordinal)
            && !endpoint.Contains("${", StringComparison.Ordinal) && !profile.ConnectionString.StartsWith("mongodb+srv", StringComparison.OrdinalIgnoreCase))
            instances = [new InstanceInfo(endpoint, "Standalone", true, "Conexão direta ao servidor informado pelo perfil.")];
        return Localize(info with { Server = endpoint, Instances = instances });
    }

    public static TopologyInfo ParseTopology(string json)
    {
        var doc = BsonDocument.Parse(json);
        var replicaSet = doc.GetValue("setName", BsonNull.Value).IsString ? doc["setName"].AsString : null;
        var mongos = doc.GetValue("msg", "").AsString == "isdbgrid";
        var loadBalanced = doc.Contains("serviceId");
        var primary = doc.GetValue("primary", "").AsString;
        var me = doc.GetValue("me", "").AsString;
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "hosts", "passives", "arbiters" })
            if (doc.TryGetValue(field, out var values) && values.IsBsonArray)
                foreach (var host in values.AsBsonArray.Where(v => v.IsString)) hosts.Add(host.AsString);
        if (me.Length > 0) hosts.Add(me);
        var arbiters = doc.GetValue("arbiters", new BsonArray()).AsBsonArray.Select(v => v.AsString).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var instances = hosts.Order().Select(host => new InstanceInfo(host,
            arbiters.Contains(host) ? "Arbiter" : host == primary ? "Primary" : replicaSet is not null ? "Secondary / membro" : mongos ? "Mongos" : "Standalone",
            !arbiters.Contains(host) && !loadBalanced && !mongos,
            arbiters.Contains(host) ? "Árbitros não armazenam documentos." : loadBalanced || mongos ? "Seleção automática do driver; roteamento direto indisponível nesta topologia." : "Conexão direta; leituras no membro escolhido, escritas dependem do papel do servidor.")).ToArray();
        return new(loadBalanced ? "Load Balanced" : mongos ? "Mongos / Sharded" : replicaSet is not null ? "Replica Set" : "Standalone", replicaSet, me, instances, json);
    }

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        (await mongo.GetIndexesAsync(profile, database, collection, cancellationToken).ConfigureAwait(false)).Select(ParseIndex).Select(Localize).ToArray();

    public static IndexInfo ParseIndex(string json)
    {
        var doc = BsonDocument.Parse(json);
        var options = doc.DeepClone().AsBsonDocument;
        options.Remove("key"); options.Remove("v"); options.Remove("ns"); options.Remove("buildUUID");
        return new(doc["name"].AsString, doc["key"].ToJson(Json), options.ToJson(Json), json,
            doc.GetValue("unique", false).ToBoolean(), doc.GetValue("sparse", false).ToBoolean(),
            doc.TryGetValue("expireAfterSeconds", out var ttl) ? ttl.ToJson(Json) : "Não definido",
            doc.TryGetValue("partialFilterExpression", out var partial) ? partial.ToJson(Json) : "Não definido");
    }

    public async Task<string> GetCollectionDetailsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var stats = await mongo.GetCollectionStatsAsync(profile, database, collection, cancellationToken).ConfigureAwait(false);
        var options = await mongo.GetCollectionDefinitionAsync(profile, database, collection, cancellationToken).ConfigureAwait(false);
        return stats + "\n\n" + L("collectionDefinitionHeader", "Definição e opções:") + "\n" + options;
    }

    private TopologyInfo Localize(TopologyInfo info) => info with
    {
        Kind = info.Kind switch
        {
            "Load Balanced" => L("topologyLoadBalanced", "Load Balanced"),
            "Mongos / Sharded" => L("topologyMongosSharded", "Mongos / Sharded"),
            "Replica Set" => L("topologyReplicaSet", "Replica Set"),
            _ => L("topologyStandalone", "Standalone")
        },
        Instances = info.Instances.Select(Localize).ToArray()
    };

    private InstanceInfo Localize(InstanceInfo instance) => instance with
    {
        Role = instance.Role switch
        {
            "Arbiter" => L("topologyArbiter", "Arbiter"),
            "Primary" => L("topologyPrimary", "Primary"),
            "Secondary / membro" => L("topologySecondary", "Secondary / membro"),
            "Mongos" => L("topologyMongos", "Mongos"),
            _ => L("topologyStandalone", "Standalone")
        },
        SelectionHint = instance.SelectionHint switch
        {
            "Árbitros não armazenam documentos." => L("topologyArbiterHint", "Árbitros não armazenam documentos."),
            "Seleção automática do driver; roteamento direto indisponível nesta topologia." => L("topologyDriverRoutingHint", "Seleção automática do driver; roteamento direto indisponível nesta topologia."),
            _ => L("topologyDirectHint", "Conexão direta; leituras no membro escolhido, escritas dependem do papel do servidor.")
        }
    };

    private IndexInfo Localize(IndexInfo index) => index with
    {
        Ttl = index.Ttl == "Não definido" ? L("notDefined", "Não definido") : index.Ttl,
        PartialFilter = index.PartialFilter == "Não definido" ? L("notDefined", "Não definido") : index.PartialFilter
    };
}
