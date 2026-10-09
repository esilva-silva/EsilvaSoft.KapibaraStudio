using System.Globalization;
using System.Runtime.ExceptionServices;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoIndexBuildProgressIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";
    private const int SyntheticDocumentCount = 40_000;

    [Test]
    public async Task CurrentOperationsCanObserveAnActiveSyntheticIndexBuild()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "O teste não persiste credenciais nos artefatos.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "O teste não persiste credenciais nos artefatos.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste aceita somente MongoDB loopback local.");

        const string databaseName = "sample_mflix";
        var database = new MongoClient(connectionString).GetDatabase(databaseName);
        var marker = Guid.NewGuid().ToString("N");
        var collectionName = "f6_index_build_" + marker;
        var indexName = "f6_observed_" + marker;
        var initialNames = await database.ListCollectionNames().ToListAsync();
        if (initialNames.Contains(collectionName, StringComparer.Ordinal))
            Assert.Fail("A coleção GUID já existia; nenhuma escrita foi iniciada.");

        var creationAttempted = false;
        var observed = false;
        Task<string>? indexBuild = null;
        Exception? operationFailure = null;
        try
        {
            creationAttempted = true;
            await database.CreateCollectionAsync(collectionName);
            var collection = database.GetCollection<BsonDocument>(collectionName);
            const string payload = "index-build-synthetic-";
            for (var offset = 0; offset < SyntheticDocumentCount; offset += 1_000)
            {
                var batch = Enumerable.Range(offset, Math.Min(1_000, SyntheticDocumentCount - offset))
                    .Select(sequence => new BsonDocument
                    {
                        ["_id"] = sequence,
                        ["_f6Run"] = marker,
                        ["payload"] = payload + sequence.ToString("D8", CultureInfo.InvariantCulture) + new string('x', 256)
                    }).ToArray();
                await collection.InsertManyAsync(batch);
            }

            var profile = ConnectionProfile.Create("F6 synthetic index build observation", connectionString, databaseName);
            var workspace = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess(
                Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.IndexBuild", marker)));
            indexBuild = collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("payload"), new CreateIndexOptions { Name = indexName }));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!indexBuild.IsCompleted && DateTimeOffset.UtcNow < deadline)
            {
                var currentOperations = await workspace.GetCurrentOperationsAsync(profile);
                var builds = IndexBuildProgressParser.Parse(currentOperations, databaseName, collectionName);
                observed = builds.Any(build => build.IndexNames.Contains(indexName, StringComparer.Ordinal)
                    || (build.IndexNames.Count == 0 && build.Message?.Contains("Index Build", StringComparison.OrdinalIgnoreCase) == true));
                if (observed)
                {
                    var build = builds.First(item => item.IndexNames.Contains(indexName, StringComparer.Ordinal)
                        || (item.IndexNames.Count == 0 && item.Message?.Contains("Index Build", StringComparison.OrdinalIgnoreCase) == true));
                    TestContext.Progress.WriteLine($"currentOp observou build sintético; source=$currentOp, index={indexName}, phase={build.Message ?? "Index Build"}, done={build.Done ?? "indisponível"}, total={build.Total ?? "indisponível"}, commitQuorumSpecified={build.CommitQuorumWasSpecified}, commitQuorum={build.ObservedCommitQuorum ?? "indisponível"}.");
                    break;
                }
                await Task.Delay(2);
            }

            await indexBuild.WaitAsync(TimeSpan.FromSeconds(30));
            if (observed)
            {
                var indexNames = await collection.Indexes.List().ToListAsync();
                Assert.That(indexNames.Any(index => index.GetValue("name", "").AsString == indexName), Is.True,
                    "O índice sintético deve permanecer após o build observado terminar.");
            }
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        var cleanupFailures = new List<Exception>();
        if (creationAttempted)
        {
            try
            {
                var collection = database.GetCollection<BsonDocument>(collectionName);
                var names = await database.ListCollectionNames().ToListAsync();
                if (names.Contains(collectionName, StringComparer.Ordinal))
                {
                    if (indexBuild is { IsCompleted: false })
                        await indexBuild.WaitAsync(TimeSpan.FromSeconds(30));
                    var total = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
                    var owned = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("_f6Run", marker));
                    if (total > SyntheticDocumentCount || total != owned)
                        throw new InvalidOperationException("A coleção do build sintético mudou de conteúdo; limpeza recusada.");
                    await database.DropCollectionAsync(collectionName);
                }

                var finalNames = await database.ListCollectionNames().ToListAsync();
                if (!finalNames.ToHashSet(StringComparer.Ordinal).SetEquals(initialNames))
                    throw new InvalidOperationException("A limpeza não restaurou a lista de namespaces de sample_mflix.");
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        if (operationFailure is not null)
        {
            if (cleanupFailures.Count > 0)
                throw new AggregateException("O smoke do build falhou e sua limpeza também encontrou problemas.",
                    [operationFailure, .. cleanupFailures]);
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
        if (cleanupFailures.Count > 0)
            throw new AggregateException("A limpeza da fixture do build de índice não foi concluída com segurança.", cleanupFailures);
        if (!observed)
            Assert.Ignore("O build do índice sintético terminou antes de currentOp capturar sua fase ativa; nenhuma falha funcional é inferida.");
    }
}
