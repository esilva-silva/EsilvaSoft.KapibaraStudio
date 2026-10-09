using System.Reflection;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoDatabaseExportDefinitionsTests
{
    private static readonly string[] ExpectedIndexNames = ["_id_", "title_1"];
    private static readonly string[] ExpectedDocumentReads = ["items"];
    private const string ExpectedServerVersion = "8.0.32";

    [Test]
    public async Task ExportCapturesCollectionOptionsValidationIndexesAndViewsSeparatelyFromDocuments()
    {
        var state = DatabaseState.Create();
        var files = new MemoryExportFiles();
        var service = CreateService(state, files);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        var result = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        using var manifest = JsonDocument.Parse(files.Text[Path.Combine(result.OutputDirectory, "manifest.json")]);
        var serverVersion = manifest.RootElement.GetProperty("Metadata").GetProperty("ServerVersion");
        var definitions = manifest.RootElement.GetProperty("Definitions");
        var collection = definitions.GetProperty("Collections").EnumerateArray().Single();
        using var options = JsonDocument.Parse(collection.GetProperty("OptionsJson").GetString()!);
        using var validator = JsonDocument.Parse(collection.GetProperty("Validation").GetProperty("ValidatorJson").GetString()!);
        var indexes = collection.GetProperty("Indexes").EnumerateArray().ToArray();
        var view = definitions.GetProperty("Views").EnumerateArray().Single();

        Assert.Multiple(() =>
        {
            Assert.That(manifest.RootElement.GetProperty("FormatVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Version").GetInt32(), Is.EqualTo(2));
            Assert.That(serverVersion.GetProperty("Status").GetString(), Is.EqualTo("Available"));
            Assert.That(serverVersion.GetProperty("Version").GetString(), Is.EqualTo(ExpectedServerVersion));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Topology").GetProperty("Status").GetString(), Is.EqualTo("Available"));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Topology").GetProperty("Kind").GetString(), Is.EqualTo("Standalone"));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("FeatureCompatibilityVersion").GetProperty("Status").GetString(), Is.EqualTo("Available"));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("FeatureCompatibilityVersion").GetProperty("Version").GetString(), Is.EqualTo("8.0"));
            Assert.That(collection.GetProperty("Name").GetString(), Is.EqualTo("items"));
            Assert.That(options.RootElement.GetProperty("capped").GetBoolean(), Is.True);
            Assert.That(options.RootElement.TryGetProperty("validator", out _), Is.False);
            Assert.That(validator.RootElement.GetProperty("$jsonSchema").GetProperty("bsonType").GetString(), Is.EqualTo("object"));
            Assert.That(collection.GetProperty("Validation").GetProperty("ValidationAction").GetInt32(), Is.EqualTo((int)CollectionValidationAction.Warn));
            Assert.That(indexes.Select(index => index.GetProperty("Name").GetString()), Is.EquivalentTo(ExpectedIndexNames));
            Assert.That(indexes.Single(index => index.GetProperty("Name").GetString() == "title_1")
                .GetProperty("OptionsJson").GetString(), Does.Contain("unique"));
            Assert.That(view.GetProperty("Name").GetString(), Is.EqualTo("active_items"));
            Assert.That(view.GetProperty("ViewOn").GetString(), Is.EqualTo("items"));
            Assert.That(view.GetProperty("PipelineJson").GetString(), Does.Contain("$match"));
            Assert.That(state.FindCalls, Is.EqualTo(ExpectedDocumentReads), "Metadata capture must not read documents beyond the normal export pass.");
            Assert.That(files.DirectoryCreateCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExportRecordsExplicitUnavailableServerVersionWithoutLeakingCommandError()
    {
        var state = DatabaseState.Create();
        state.FailBuildInfo = true;
        state.FailHello = true;
        state.FailFcv = true;
        var files = new MemoryExportFiles();
        var service = CreateService(state, files);
        var profile = ConnectionProfile.Create("Local", "mongodb://private-user:private-secret@private-host.example:27017/source");

        var result = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifestText = files.Text[Path.Combine(result.OutputDirectory, "manifest.json")];
        using var manifest = JsonDocument.Parse(manifestText);
        var serverVersion = manifest.RootElement.GetProperty("Metadata").GetProperty("ServerVersion");

        Assert.Multiple(() =>
        {
            Assert.That(serverVersion.GetProperty("Status").GetString(), Is.EqualTo("Unavailable"));
            Assert.That(serverVersion.GetProperty("Version").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Topology").GetProperty("Status").GetString(), Is.EqualTo("Unavailable"));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("FeatureCompatibilityVersion").GetProperty("Status").GetString(), Is.EqualTo("Unavailable"));
            Assert.That(manifestText, Does.Not.Contain("private-secret"));
            Assert.That(manifestText, Does.Not.Contain("private-host.example"));
            Assert.That(manifestText, Does.Not.Contain("Synthetic buildInfo error"));
            Assert.That(state.FindCalls, Is.EqualTo(ExpectedDocumentReads));
        });
    }

    [Test]
    public async Task ExportClassifiesMongosWithoutPersistingServerAddresses()
    {
        var state = DatabaseState.Create();
        state.HelloResponse = new BsonDocument
        {
            { "msg", "isdbgrid" },
            { "hosts", new BsonArray { "private-mongo-a:27017", "private-mongo-b:27017" } }
        };
        var files = new MemoryExportFiles();
        var service = CreateService(state, files);
        var profile = ConnectionProfile.Create("Local", "mongodb://private-user:private-secret@private-host.example:27017");

        var result = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifestText = files.Text[Path.Combine(result.OutputDirectory, "manifest.json")];
        using var manifest = JsonDocument.Parse(manifestText);
        var topology = manifest.RootElement.GetProperty("Metadata").GetProperty("Topology");

        Assert.Multiple(() =>
        {
            Assert.That(topology.GetProperty("Status").GetString(), Is.EqualTo("Available"));
            Assert.That(topology.GetProperty("Kind").GetString(), Is.EqualTo("Mongos"));
            Assert.That(manifestText, Does.Not.Contain("private-mongo-a"));
            Assert.That(manifestText, Does.Not.Contain("private-mongo-b"));
            Assert.That(manifestText, Does.Not.Contain("private-host.example"));
            Assert.That(manifestText, Does.Not.Contain("private-secret"));
        });
    }

    [Test]
    public void ExportFailsClosedWhenIndexMetadataCannotBeRead()
    {
        var state = DatabaseState.Create();
        state.FailIndexRead = true;
        var files = new MemoryExportFiles();
        var service = CreateService(state, files);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        var failure = Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source")));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("Permissão insuficiente"));
            Assert.That(failure.Message, Does.Contain("índices"));
            Assert.That(files.DirectoryCreateCount, Is.Zero, "A exportação aborta antes de criar pacote parcial.");
            Assert.That(files.Text, Is.Empty);
            Assert.That(state.FindCalls, Is.Empty, "Nenhuma leitura de documentos começa após falha de captura de metadados.");
        });
    }

    [Test]
    public void CreateIndexesCommandContainsOneDocumentSpecification()
    {
        var serviceType = typeof(MongoWorkspaceService).Assembly.GetType(
            "EsilvaSoft.KapibaraStudio.Infrastructure.MongoDatabaseExportImportService", throwOnError: true)!;
        var method = serviceType.GetMethod("BuildCreateIndexesCommand", BindingFlags.NonPublic | BindingFlags.Static)!;

        var command = (BsonDocument)method.Invoke(null,
            ["items", "title_1", new BsonDocument("title", 1), new BsonDocument("unique", true)])!;
        var indexes = command["indexes"].AsBsonArray;

        Assert.Multiple(() =>
        {
            Assert.That(indexes, Has.Count.EqualTo(1));
            Assert.That(indexes[0].AsBsonDocument["name"].AsString, Is.EqualTo("title_1"));
            Assert.That(indexes[0].AsBsonDocument["key"].AsBsonDocument, Is.EqualTo(new BsonDocument("title", 1)));
            Assert.That(indexes[0].AsBsonDocument["unique"].AsBoolean, Is.True);
        });
    }

    private static MongoWorkspaceService CreateService(DatabaseState state, MemoryExportFiles files) =>
        new(files, clients: new FakeClientPool(new FakeMongoDriver(state)));

    private sealed class MemoryExportFiles : IMongoDatabaseExportFileAccess
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public int DirectoryCreateCount { get; private set; }

        public string NormalizePath(string path) => path;

        public string CreateExportDirectory(string directoryName)
        {
            DirectoryCreateCount++;
            return Path.Combine("C:\\export", directoryName);
        }

        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => _files.ContainsKey(path) || Text.ContainsKey(path);

        public Stream CreateNewFile(string path) => new CommitStream(bytes => _files[path] = bytes);

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Text[path] = content;
            return Task.CompletedTask;
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Text[path]);

        private sealed class CommitStream(Action<byte[]> commit) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    commit(ToArray());
                }

                base.Dispose(disposing);
            }
        }
    }

    private sealed class FakeClientPool(FakeMongoDriver driver) : IMongoClientPool
    {
        public IMongoClient GetClient(MongoClientSettings settings) => driver.Client;
    }

    private sealed class FakeMongoDriver(DatabaseState state)
    {
        public IMongoClient Client { get; } = CreateProxy<IMongoClient>((method, args) => method.Name switch
        {
            "GetDatabase" => CreateDatabase(state),
            _ => throw new NotSupportedException(method.Name)
        });

        private static IMongoDatabase CreateDatabase(DatabaseState state) => CreateProxy<IMongoDatabase>((method, args) => method.Name switch
        {
            "ListCollectionNamesAsync" => Task.FromResult(CreateCursor(state.CollectionNames)),
            "ListCollectionsAsync" => Task.FromResult(CreateCursor(state.Definitions)),
            "RunCommandAsync" => ReadCommand(state, GetCommandDocument(args![0]!)),
            "GetCollection" => CreateCollection(state, (string)args![0]!),
            _ => throw new NotSupportedException(method.Name)
        });

        private static BsonDocument GetCommandDocument(object command) => command switch
        {
            BsonDocument document => document,
            Command<BsonDocument> typedCommand => typedCommand.Render(BsonSerializer.SerializerRegistry).Document,
            _ => throw new NotSupportedException($"Unexpected command wrapper: {command.GetType().Name}.")
        };

        private static Task<BsonDocument> ReadCommand(DatabaseState state, BsonDocument command)
        {
            if (command.Contains("buildInfo"))
            {
                if (state.FailBuildInfo)
                    throw new UnauthorizedAccessException("Synthetic buildInfo error containing private-secret.");
                return Task.FromResult(new BsonDocument("version", state.ServerVersion));
            }
            if (command.Contains("hello"))
            {
                if (state.FailHello)
                    throw new UnauthorizedAccessException("Synthetic hello error containing private-host.example.");
                return Task.FromResult(state.HelloResponse);
            }
            if (command.Contains("getParameter"))
            {
                if (state.FailFcv)
                    throw new UnauthorizedAccessException("Synthetic getParameter error containing private-secret.");
                return Task.FromResult(new BsonDocument("featureCompatibilityVersion", new BsonDocument("version", state.FeatureCompatibilityVersion)));
            }
            throw new NotSupportedException("Synthetic command is outside this test surface.");
        }

        private static IMongoCollection<BsonDocument> CreateCollection(DatabaseState state, string name) =>
            CreateProxy<IMongoCollection<BsonDocument>>((method, args) =>
            {
                if (method.Name == "get_Indexes")
                {
                    return CreateIndexManager(state);
                }

                if (method.Name == "FindAsync")
                {
                    state.FindCalls.Add(name);
                    return Task.FromResult(CreateCursor(Array.Empty<BsonDocument>()));
                }

                throw new NotSupportedException(method.Name);
            });

        private static IMongoIndexManager<BsonDocument> CreateIndexManager(DatabaseState state) =>
            CreateProxy<IMongoIndexManager<BsonDocument>>((method, _) =>
            {
                if (method.Name != "ListAsync")
                {
                    throw new NotSupportedException(method.Name);
                }

                if (state.FailIndexRead)
                {
                    throw new UnauthorizedAccessException("Synthetic permission failure.");
                }

                return Task.FromResult(CreateCursor(state.Indexes));
            });

        private static IAsyncCursor<T> CreateCursor<T>(IReadOnlyList<T> items)
        {
            var moved = false;
            return CreateProxy<IAsyncCursor<T>>((method, _) => method.Name switch
            {
                "MoveNext" or "MoveNextAsync" => MoveNext(method.Name == "MoveNextAsync"),
                "get_Current" => moved ? items : Array.Empty<T>(),
                "Dispose" => null,
                _ => throw new NotSupportedException(method.Name)
            });

            object MoveNext(bool async) {
                if (moved || items.Count == 0)
                {
                    return async ? Task.FromResult(false) : false;
                }

                moved = true;
                return async ? Task.FromResult(true) : true;
            }
        }

        private static T CreateProxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, TestDispatchProxy<T>>();
            ((TestDispatchProxy<T>)(object)proxy).Handler = handler;
            return proxy;
        }
    }

    private sealed class DatabaseState
    {
        public static DatabaseState Create() => new();

        public List<string> CollectionNames { get; } = ["items", "active_items"];
        public List<BsonDocument> Definitions { get; } =
        [
            new BsonDocument
            {
                { "name", "items" }, { "type", "collection" },
                { "options", new BsonDocument
                    {
                        { "capped", true }, { "size", 8192 },
                        { "validator", new BsonDocument("$jsonSchema", new BsonDocument("bsonType", "object")) },
                        { "validationLevel", "strict" }, { "validationAction", "warn" }
                    }
                }
            },
            new BsonDocument
            {
                { "name", "active_items" }, { "type", "view" },
                { "options", new BsonDocument
                    {
                        { "viewOn", "items" },
                        { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument("active", true)) } },
                        { "collation", new BsonDocument("locale", "en") }
                    }
                }
            }
        ];
        public List<BsonDocument> Indexes { get; } =
        [
            new BsonDocument { { "v", 2 }, { "key", new BsonDocument("_id", 1) }, { "name", "_id_" }, { "ns", "source.items" } },
            new BsonDocument { { "v", 2 }, { "key", new BsonDocument("title", 1) }, { "name", "title_1" }, { "unique", true }, { "ns", "source.items" } }
        ];
        public List<string> FindCalls { get; } = [];
        public bool FailIndexRead { get; set; }
        public bool FailBuildInfo { get; set; }
        public bool FailHello { get; set; }
        public bool FailFcv { get; set; }
        public BsonDocument HelloResponse { get; set; } = new("isWritablePrimary", true);
        public string ServerVersion { get; set; } = ExpectedServerVersion;
        public string FeatureCompatibilityVersion { get; set; } = "8.0";
    }

    public class TestDispatchProxy<T> : DispatchProxy where T : class
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod ?? throw new InvalidOperationException("Missing proxy method."), args ?? []);
    }
}
