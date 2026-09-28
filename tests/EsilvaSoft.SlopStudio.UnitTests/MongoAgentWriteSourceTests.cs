using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

// Offline rules of the agent write source: everything here is decided before any connection is opened. The profile
// points to a closed local port, so a request that were sent would come back NotSent/OutcomeUnknown instead of the
// asserted pre-send status. Behaviour against a server is covered by ConsoleMongoIntegrationTests.AgentWrites.
public sealed class MongoAgentWriteSourceTests
{
    private const string Id = "{\"$numberLong\":\"7\"}";
    private static readonly Guid Generation = Guid.NewGuid();
    private static readonly ConnectionProfile Profile =
        ConnectionProfile.Create("Offline", "mongodb://127.0.0.1:1/?directConnection=true&serverSelectionTimeoutMS=200")
            with { SourceGenerationId = Generation };

    private static readonly string[] SparseSpecificationNames = ["key", "name", "sparse"];
    private static readonly string[] PreimageFilterNames = ["_id", "$expr"];

    private static AgentMongoWriteApproval Approval() => new(Guid.NewGuid(), new string('a', 64));

    private static MongoAgentWriteSource CreateSource() =>
        new(new SessionConnectionSecretStore(), null, new MongoClientPool());

    private static (string Ejson, string Hash) Preimage(BsonDocument document)
    {
        var ejson = MongoAgentWriteSource.ToCanonicalEjson(document);
        return (ejson, MongoAgentWriteSource.ComputeStateHash(ejson));
    }

    [Test]
    public async Task RequestsWithoutConsumedApprovalAreRejectedBeforeSending()
    {
        var source = CreateSource();
        var (preimage, hash) = Preimage(new BsonDocument { ["_id"] = 7L, ["n"] = 1 });
        var results = new[]
        {
            await source.InsertOneAsync(Profile, new("db", "c", "{\"_id\":1}", 1000) { SourceGenerationId = Generation }, default),
            await source.UpdateOneAsync(Profile, new("db", "c", Id, "{\"$set\":{\"n\":2}}", hash, 1000)
                { SourceGenerationId = Generation, ExpectedStateEjson = preimage }, default),
            await source.DeleteOneAsync(Profile, new("db", "c", Id, hash, 1000)
                { SourceGenerationId = Generation, ExpectedStateEjson = preimage }, default),
            await source.CreateIndexAsync(Profile, new("db", "c", "{\"a\":1}", null, false, false, 1000)
                { SourceGenerationId = Generation }, default),
            await source.DropIndexAsync(Profile, new("db", "c", "a_1", hash, 1000) { SourceGenerationId = Generation }, default)
        };
        Assert.That(results.Select(result => result.Status), Is.All.EqualTo(AgentMongoWriteStatus.InvalidRequest));
    }

    [TestCase("{\"n\":1}", TestName = "Insert sem _id fixado")]
    [TestCase("{\"_id\":[1,2]}", TestName = "Insert com _id array")]
    [TestCase("{\"_id\":1,\"$set\":{\"a\":1}}", TestName = "Insert com operador")]
    [TestCase("{\"_id\":1,\"a\":{\"$code\":\"function(){}\"}}", TestName = "Insert com JavaScript")]
    [TestCase("{\"_id\":1,\"a\":ENV.get(\"X\")}", TestName = "Insert com ENV")]
    [TestCase("{\"_id\":NumberLong(1)}", TestName = "Insert com construtor do shell")]
    [TestCase("{\"_id\":1,\"_id\":2}", TestName = "Insert com nome duplicado")]
    public async Task InvalidInsertDocumentsAreRejected(string document)
    {
        var result = await CreateSource().InsertOneAsync(Profile,
            new("db", "c", document, 1000) { SourceGenerationId = Generation, Approval = Approval() }, default);
        Assert.That(result.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
    }

    [TestCase("[{\"$set\":{\"a\":1}}]", TestName = "Update em pipeline")]
    [TestCase("{\"a\":1}", TestName = "Update de substituição")]
    [TestCase("{\"$setOnInsert\":{\"a\":1}}", TestName = "Update exclusivo de upsert")]
    [TestCase("{\"$set\":{\"_id\":2}}", TestName = "Update de _id")]
    [TestCase("{\"$set\":{\"arr.$[x]\":2}}", TestName = "Update posicional")]
    [TestCase("{\"$set\":{\"a\":{\"$where\":\"true\"}}}", TestName = "Update com código")]
    public async Task InvalidUpdateDocumentsAreRejected(string update)
    {
        var (preimage, hash) = Preimage(new BsonDocument { ["_id"] = 7L, ["a"] = 1 });
        var result = await CreateSource().UpdateOneAsync(Profile, new("db", "c", Id, update, hash, 1000)
        {
            SourceGenerationId = Generation, Approval = Approval(), ExpectedStateEjson = preimage
        }, default);
        Assert.That(result.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
    }

    [Test]
    public async Task PreimageMustBeTheApprovedCanonicalStateOfTheRequestedId()
    {
        var source = CreateSource();
        var document = new BsonDocument { ["_id"] = 7L, ["n"] = 1 };
        var (preimage, hash) = Preimage(document);
        var other = Preimage(new BsonDocument { ["_id"] = 8L, ["n"] = 1 });
        var relaxed = document.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });

        AgentMongoUpdateRequest Update(string? state, string expectedHash, string id = Id) =>
            new("db", "c", id, "{\"$set\":{\"n\":2}}", expectedHash, 1000)
            {
                SourceGenerationId = Generation, Approval = Approval(), ExpectedStateEjson = state
            };

        var statuses = new[]
        {
            (await source.UpdateOneAsync(Profile, Update(null, hash), default)).Status, // no pre-image
            (await source.UpdateOneAsync(Profile, Update(preimage, other.Hash), default)).Status, // hash of another state
            (await source.UpdateOneAsync(Profile, Update(other.Ejson, other.Hash), default)).Status, // ticket of another _id
            (await source.UpdateOneAsync(Profile, Update(relaxed, MongoAgentWriteSource.ComputeStateHash(relaxed)), default)).Status,
            (await source.UpdateOneAsync(Profile, Update(preimage, hash.ToUpperInvariant()), default)).Status,
            (await source.UpdateOneAsync(Profile, Update(preimage, hash, "{\"$numberInt\":\"7\"}"), default)).Status
        };
        Assert.That(statuses, Is.All.EqualTo(AgentMongoWriteStatus.InvalidRequest),
            "Pré-imagem ausente, de outro estado/_id, não canônica ou _id com outro tipo BSON não pode ser enviada.");
    }

    [Test]
    public async Task ReadOnlyGenerationAndDynamicTargetsAreCheckedBeforeSending()
    {
        var source = CreateSource();
        var insert = new AgentMongoInsertRequest("db", "c", "{\"_id\":1}", 1000)
        {
            SourceGenerationId = Generation, Approval = Approval()
        };
        var readOnly = Profile with { IsReadOnly = true };
        var otherGeneration = Profile with { SourceGenerationId = Guid.NewGuid() };
        var dynamic = Profile with { ConnectionString = "mongodb://${HOST}/" };

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await source.InsertOneAsync(readOnly, insert, default)).Status, Is.EqualTo(AgentMongoWriteStatus.Forbidden));
            Assert.That((await source.InsertOneAsync(otherGeneration, insert, default)).Status, Is.EqualTo(AgentMongoWriteStatus.NotSent));
            Assert.That((await source.InsertOneAsync(Profile, insert with { SourceGenerationId = Guid.Empty }, default)).Status,
                Is.EqualTo(AgentMongoWriteStatus.NotSent));
            Assert.That((await source.InsertOneAsync(dynamic, insert, default)).Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(30_001)]
    public async Task MaxTimeOutsideLimitsIsRejected(int maxTimeMs)
    {
        var result = await CreateSource().InsertOneAsync(Profile,
            new("db", "c", "{\"_id\":1}", maxTimeMs) { SourceGenerationId = Generation, Approval = Approval() }, default);
        Assert.That(result.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
    }

    [TestCase("admin", "c")]
    [TestCase("local", "c")]
    [TestCase("config", "c")]
    [TestCase("db", "system.users")]
    [TestCase("db", "a$b")]
    [TestCase("d.b", "c")]
    [TestCase("db", "")]
    public async Task ReservedOrAmbiguousNamespacesAreRejected(string database, string collection)
    {
        var result = await CreateSource().InsertOneAsync(Profile,
            new(database, collection, "{\"_id\":1}", 1000) { SourceGenerationId = Generation, Approval = Approval() }, default);
        Assert.That(result.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
    }

    [TestCase("_id_", AgentMongoWriteStatus.InvalidRequest)]
    [TestCase("*", AgentMongoWriteStatus.InvalidRequest)]
    [TestCase("tag_1", AgentMongoWriteStatus.PreconditionUnavailable)]
    public async Task DropIndexNeverSendsTheDrop(string name, AgentMongoWriteStatus expected)
    {
        // A closed port would turn any send attempt into NotSent; PreconditionUnavailable proves nothing was tried.
        var result = await CreateSource().DropIndexAsync(Profile, new("db", "c", name, new string('b', 64), 1000)
        {
            SourceGenerationId = Generation, Approval = Approval()
        }, default);
        Assert.That(result.Status, Is.EqualTo(expected));
    }

    [TestCase("{\"a\":1,\"b\":-1}", null, "a_1_b_-1")]
    [TestCase("{\"h\":\"hashed\"}", null, "h_hashed")]
    [TestCase("{\"g\":\"2dsphere\"}", "geo", "geo")]
    public void IndexSpecificationIsCanonical(string keys, string? name, string expectedName)
    {
        Assert.That(MongoAgentWriteSource.TryBuildIndexSpecification(keys, name, unique: false, sparse: true,
            out var specification, out var canonicalName), Is.True);
        Assert.That(canonicalName, Is.EqualTo(expectedName));
        Assert.That(specification.Names, Is.EqualTo(SparseSpecificationNames),
            "Opções falsas não entram na especificação; unique/sparse só quando verdadeiros.");
        Assert.That(specification["key"].AsBsonDocument.ToBson(), Is.EqualTo(BsonDocument.Parse(keys).ToBson()));
    }

    [TestCase("{\"_id\":1}", null)]
    [TestCase("{\"a\":2}", null)]
    [TestCase("{\"a\":1.0}", null)]
    [TestCase("{\"a\":\"text\"}", null)]
    [TestCase("{\"$**\":1}", null)]
    [TestCase("{\"a\":1}", "_id_")]
    [TestCase("{\"a\":1}", "*")]
    [TestCase("{\"a\":1}", "a$1")]
    [TestCase("{}", null)]
    public void UnsafeIndexSpecificationsAreRejected(string keys, string? name) =>
        Assert.That(MongoAgentWriteSource.TryBuildIndexSpecification(keys, name, false, false, out _, out _), Is.False);

    [Test]
    public void PreimageFilterGuardsValueEqualButDistinctBsonScalars()
    {
        var document = new BsonDocument
        {
            ["_id"] = 1,
            ["i"] = 5,
            ["dec"] = BsonDecimal128.Create("1.00"),
            ["dotted.name"] = "x",
            ["nested"] = new BsonDocument("arr", new BsonArray { 1L, 2.5 })
        };
        var filter = MongoAgentWriteSource.BuildPreimageFilter(document);
        Assert.That(filter, Is.Not.Null);
        var json = filter!.ToJson();
        Assert.Multiple(() =>
        {
            Assert.That(filter!.Names, Is.EqualTo(PreimageFilterNames));
            Assert.That(json, Does.Contain("\"$literal\" : \"dotted.name\""), "Nome com ponto lido literalmente.");
            Assert.That(json, Does.Contain("\"int\"").And.Contain("\"long\"").And.Contain("\"double\"")
                .And.Contain("\"decimal\"").And.Contain("\"string\"").And.Contain("\"1.00\""));
            Assert.That(json, Does.Contain("$bsonSize"));
        });
    }

    [Test]
    public void PreimageFilterIsUnavailableWhenTooManyScalarsNeedGuards()
    {
        var document = new BsonDocument
        {
            ["_id"] = 1,
            ["values"] = new BsonArray(Enumerable.Range(0, MongoAgentWriteSource.MaximumTypeGuards + 1))
        };
        Assert.That(MongoAgentWriteSource.BuildPreimageFilter(document), Is.Null);
    }
}
