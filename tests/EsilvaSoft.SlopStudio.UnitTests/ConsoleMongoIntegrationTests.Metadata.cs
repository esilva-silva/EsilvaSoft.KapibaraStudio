using System.Text.Json;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

public sealed partial class ConsoleMongoIntegrationTests
{
    [Test, Category("MongoReal")]
    public async Task MetadataSourceListsKindsValidatorsIndexesAndSamplesWithoutValues()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ?? (Directory.Exists(binaries) ? Directory.EnumerateFiles(binaries, "mongod.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente; defina SLOP_CONSOLE_MONGOD para homologação real.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "metadata-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
        using var server = await StartServer(executable!, directory);
        using var pool = new MongoClientPool();
        using var client = new MongoClient(server.Uri);
        var database = client.GetDatabase("meta");
        await database.CreateCollectionAsync("clientes", new CreateCollectionOptions<BsonDocument>
        {
            Validator = new BsonDocumentFilterDefinition<BsonDocument>(BsonDocument.Parse("""{ "$jsonSchema": { "required": ["Nome"], "properties": { "Nome": { "bsonType": "string" } } } }"""))
        });
        var clientes = database.GetCollection<BsonDocument>("clientes");
        await clientes.InsertManyAsync(
        [
            BsonDocument.Parse("""{ "Nome": "valor-privado", "Cliente": { "Id": 1 }, "Itens": [ { "Sku": "A" } ] }"""),
            BsonDocument.Parse("""{ "Nome": "outro", "Extra": true }""")
        ]);
        await clientes.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("Cliente.Id")));
        await database.RunCommandAsync<BsonDocument>(BsonDocument.Parse("""{ "create": "resumo", "viewOn": "clientes", "pipeline": [] }"""));

        var profile = ConnectionProfile.Create("Meta", server.Uri, "meta");
        var source = new MongoMetadataSource(clients: pool);
        var clientesDefinition = await source.GetCollectionDefinitionAsync(profile, "meta", "clientes", CancellationToken.None);
        var resumoDefinition = await source.GetCollectionDefinitionAsync(profile, "meta", "resumo", CancellationToken.None);
        var sample = await source.SampleSchemaAsync(profile, "meta", "clientes", new SchemaSampleOptions(Size: 10), CancellationToken.None);
        var schema = new SchemaBuilder().AddSample(sample).Build();
        Assert.Multiple(async () =>
        {
            Assert.That(await source.ListDatabaseNamesAsync(profile, CancellationToken.None), Does.Contain("meta"));
            Assert.That(resumoDefinition!.Kind, Is.EqualTo(CollectionKind.View));
            Assert.That(clientesDefinition!.ValidatorJson, Does.Contain("Nome"));
            Assert.That(await source.GetCollectionDefinitionAsync(profile, "meta", "inexistente", CancellationToken.None), Is.Null);
            Assert.That((await source.ListCollectionNamesAsync(profile, "meta", CancellationToken.None)).Select(entry => entry.Name), Is.SupersetOf(Expect.Words("clientes resumo")));
            Assert.That((await source.ListIndexesAsync(profile, "meta", "clientes", CancellationToken.None)).Select(index => index.Name), Does.Contain("Cliente.Id_1"));
            Assert.That(schema.Find("Cliente.Id")!.PrimaryType, Is.EqualTo("int"));
            Assert.That(schema.Find("Itens.Sku"), Is.Not.Null);
            Assert.That(schema.Find("Nome")!.Occurrence, Is.EqualTo(1));
            Assert.That(schema.Find("Extra")!.Occurrence, Is.EqualTo(.5));
            Assert.That(JsonSerializer.Serialize(sample), Does.Not.Contain("valor-privado"), "The server-side pipeline never returns document values.");
        });
        TestContext.Out.WriteLine($"MongoDB {server.Version}: listagens, validator, índice, view e amostra de nomes/tipos verificados.");
        completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }
}
