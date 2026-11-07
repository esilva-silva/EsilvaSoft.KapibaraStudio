using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Diagnóstico do servidor e suporte à recuperação de configurações anteriores do profiler.</summary>
internal static class MongoServerAdministrator
{
    public static async Task<string> GetServerStatusAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var status = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetTopologyAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var topology = await context.CreateClient()
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return topology.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetCurrentOperationsAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        var operations = await ReadCurrentOperationsAsync(context, cancellationToken).ConfigureAwait(false);
        return operations.ToJson(MongoJson.CanonicalSettings);
    }

    private static async Task<BsonDocument> ReadCurrentOperationsAsync(MongoOperationContext context, CancellationToken cancellationToken)
    {
        // $currentOp is collectionless. Include idle sessions holding transaction locks and
        // idle connections (index builds waiting for commit quorum), but bound one snapshot.
        var pipeline = PipelineDefinition<NoPipelineInput, BsonDocument>.Create(
        [
            new BsonDocument("$currentOp", new BsonDocument
            {
                ["allUsers"] = true,
                ["idleSessions"] = true,
                ["idleConnections"] = true,
                ["idleCursors"] = false
            }),
            new BsonDocument("$limit", 201)
        ]);
        var admin = context.CreateClient().GetDatabase("admin");
        try
        {
            using var cursor = await admin.AggregateAsync(pipeline,
                new AggregateOptions { BatchSize = 64, MaxTime = TimeSpan.FromSeconds(5) },
                cancellationToken).ConfigureAwait(false);
            var batch = new BsonArray();
            while (batch.Count < 201 && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var operation in cursor.Current)
                {
                    if (batch.Count == 201)
                        break;
                    batch.Add(operation);
                }
            }

            return new BsonDocument
            {
                ["inprog"] = batch,
                ["source"] = "$currentOp",
                ["truncated"] = batch.Count > 200
            };
        }
        catch (MongoCommandException exception) when (exception.Code is 40324 or 16872 or 59)
        {
            // Legacy servers may not support the aggregation stage. Never substitute own-ops
            // for a denied all-users request; that would silently change the scope.
            var legacy = await admin.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["currentOp"] = 1,
                ["$all"] = true
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var legacyOperations = legacy.GetValue("inprog", new BsonArray()).AsBsonArray;
            var truncated = legacyOperations.Count > 200;
            legacy["inprog"] = new BsonArray(legacyOperations.Take(201));
            legacy["truncated"] = truncated;
            legacy["source"] = "currentOp (legacy)";
            return legacy;
        }
    }

    public static async Task<string> GetProfilerStatusAsync(MongoOperationContext context, string database, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var status = await context.CreateClient()
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("profile", -1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return status.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<ProfilerConfigurationResult> ConfigureProfilerAsync(
        MongoOperationContext context,
        ConnectionProfile profile,
        ProfilerConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var client = context.CreateClient();
        var database = client.GetDatabase(request.Database);
        var currentStatus = await GetProfilerStatusAsync(context, request.Database, cancellationToken).ConfigureAwait(false);
        var currentTopology = await GetTopologyAsync(context, cancellationToken).ConfigureAwait(false);
        EnsureProfilerRecoveryStateMatches(currentStatus, currentTopology, request.ExpectedStatusJson, request.ExpectedTopologyJson);
        if (ProfilerSettings.IsMongos(currentTopology))
            throw new NotSupportedException("A configuração do profiler pelo mongos não está liberada neste fluxo; conecte-se a um mongod compatível.");

        var command = new BsonDocument("profile", request.Level);
        if (request.SlowMs is { } slow)
            command["slowms"] = slow;
        if (request.SampleRate is { } rate)
            command["sampleRate"] = (double)rate;
        var expectedFilter = request.FilterMode == ProfilerFilterMode.Set
            ? context.ParseDocument(request.FilterJson!, "filtro do profiler") : null;
        command["filter"] = expectedFilter is null ? "unset" : expectedFilter;

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        var observedStatus = await GetProfilerStatusAsync(context, request.Database, cancellationToken).ConfigureAwait(false);
        var observed = ProfilerSettings.Parse(observedStatus);
        var observedDocument = BsonDocument.Parse(observedStatus);
        var observedFilter = observedDocument.GetValue("filter", BsonNull.Value);
        var filterMatches = expectedFilter is null
            ? observedFilter.IsBsonNull
            : observedFilter.IsBsonDocument && observedFilter.AsBsonDocument.Equals(expectedFilter);
        if (observed.Level != request.Level
            || request.SlowMs is not null && observed.SlowMs != request.SlowMs
            || request.SampleRate is not null && observed.SampleRate != request.SampleRate
            || !filterMatches)
            throw new InvalidOperationException("O comando foi enviado, mas a configuração resultante não corresponde à solicitada. Releia o profiler antes de continuar.");

        return new ProfilerConfigurationResult(observed.Level, observed.SlowMs,
            observed.SampleRate, !observedFilter.IsBsonNull);
    }

    internal static void EnsureProfilerRecoveryStateMatches(string status, string topology, string expectedStatus, string expectedTopology)
    {
        if (ProfilerSettings.Parse(status) != ProfilerSettings.Parse(expectedStatus)
            || ProfilerCaptureTopology.GetNodeIdentity(topology) != ProfilerCaptureTopology.GetNodeIdentity(expectedTopology))
            throw new InvalidOperationException("O estado do profiler ou a topologia mudou após a prévia. Revise novamente.");
    }

}
