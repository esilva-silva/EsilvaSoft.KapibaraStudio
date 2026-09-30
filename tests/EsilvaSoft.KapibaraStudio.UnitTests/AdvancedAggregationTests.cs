using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AdvancedAggregationTests
{
    [Test]
    public async Task ExplainKeepsSnapshotAndCancelsOnlyItsOwnTab()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var firstReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new List<AggregationQuery>();
        context.Mongo.Handler = (method, args) =>
        {
            Assert.That(method, Is.EqualTo("ExplainAggregationAsync"));
            var query = (AggregationQuery)args[1]!; captured.Add(query);
            return (query.Database == "first" ? firstReply.Task : secondReply.Task).WaitAsync((CancellationToken)args[2]!);
        };
        var first = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "first", Collection = "orders", Mode = "Agregação", Text = "[{ $match: {} }]", Limit = 7 };
        var second = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "second", Collection = "customers", Mode = "Agregação", Text = "[]" };
        var runFirst = first.ExplainAggregationCommand.ExecuteAsync(null);
        var runSecond = second.ExplainAggregationCommand.ExecuteAsync(null);
        first.Database = "changed"; first.Collection = "changed"; first.Text = "[]"; first.Limit = 1;
        secondReply.SetResult("{\"queryPlanner\":{\"namespace\":\"second.customers\"}}");
        await runSecond;
        Assert.That(second.Messages, Does.Contain("Dev › second › customers"));
        Assert.That(first.IsRunning, Is.True);
        first.CancelCommand.Execute(null); await runFirst;
        Assert.That(first.Status, Is.EqualTo("Cancelado"));
        Assert.That(second.Messages, Does.Contain("second.customers"));
        Assert.That(captured[0], Is.EqualTo(new AggregationQuery("first", "orders", "[{ $match: {} }]", 7)));
        Assert.That(first.Results, Does.Not.Contain("queryPlanner"));
    }

    [TestCase("[\n { $match: {} },\n { $group: { _id: '$customer', total: { $sum: '$price' } } }\n]", true)]
    [TestCase("db.orders.aggregate([{ $match: { id: ObjectId('507f1f77bcf86cd799439011') } }]);", false)]
    [TestCase("[{ $set: { id: UUID('00112233-4455-6677-8899-aabbccddeeff'), exact: NumberLong('9223372036854775807') } }]", true)]
    public async Task OfflineValidationPreservesBsonConstructors(string text, bool aggregation)
    {
        var result = await new MongoCodeValidator().ValidateAsync(text, aggregation);
        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public async Task OfflineValidationLocatesErrorsAndDoesNotExecuteCode()
    {
        const string text = "[\n { $match: {} },\n { $out: 'copy' }\n]";
        var result = await new MongoCodeValidator().ValidateAsync(text, true);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Offset, Is.EqualTo(text.IndexOf("$out", StringComparison.Ordinal)));
        Assert.That(result.Message, Does.Contain("Linha 3").And.Contain("estágio 2"));
        var syntax = await new MongoCodeValidator().ValidateAsync("db.orders.find({\n price: } )", false);
        Assert.That(syntax.IsValid, Is.False);
        Assert.That(syntax.Message, Does.Contain("linha 2"));
        Assert.That(syntax.Offset, Is.EqualTo("db.orders.find({\n price: ".Length));
        Assert.That((await new MongoCodeValidator().ValidateAsync("throw new Error('not executed')", false)).IsValid, Is.True);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => new MongoCodeValidator().ValidateAsync(text, true, cancellation.Token));
    }

    [TestCase("[{ $out: 'copy' }]", "$out")]
    [TestCase("[{ $merge: 'copy' }]", "$merge")]
    [TestCase("[{ $facet: { bad: [{ $out: 'copy' }] } }]", "$facet.bad")]
    [TestCase("[{ $lookup: { from: 'other', pipeline: [{ $merge: 'copy' }], as: 'rows' } }]", "$lookup.pipeline")]
    public void DirectAggregationRejectsWritesBeforeConnecting(string pipeline, string location)
    {
        var files = new RefusingMongoDatabaseExportFileAccess();
        var clients = new RefusingMongoClientPool();
        var service = new MongoWorkspaceService(files, clients: clients);
        var profile = ConnectionProfile.Create("Leitura", "mongodb://127.0.0.1:1", isReadOnly: true);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.AggregateAsync(profile, new("sample", "orders", pipeline)));
        Assert.That(error!.Message, Does.Contain(location).And.Contain("não foi enviado"));
        Assert.That(files.Calls, Is.Zero);
        Assert.That(clients.Requests, Is.Zero);
    }

    [TestCase("[{}]")]
    [TestCase("[5]")]
    [TestCase("[{ $match: {}, $limit: 2 }]")]
    [TestCase("[{ match: {} }]")]
    public void StructuralErrorsIdentifyStage(string pipeline)
    {
        var error = Assert.Throws<ArgumentException>(() => AggregationPipelineValidator.ValidateReadPipeline(BsonSerializer.Deserialize<BsonArray>(pipeline)));
        Assert.That(error!.Message, Does.Contain("estágio 1"));
    }

    [Test]
    public void LiteralOperatorNamesAndUnknownStagesRemainEditable()
    {
        var pipeline = BsonSerializer.Deserialize<BsonArray>("[{ $project: { value: { $literal: { $out: 'data' } } } }, { $futureStage: {} }]");
        Assert.DoesNotThrow(() => AggregationPipelineValidator.ValidateReadPipeline(pipeline));
    }

    private sealed class RefusingMongoClientPool : IMongoClientPool
    {
        public int Requests { get; private set; }

        public IMongoClient GetClient(MongoClientSettings settings)
        {
            Requests++;
            throw new AssertionException("Agregação recusada não pode solicitar um cliente MongoDB.");
        }
    }

    [TestCase("$match")]
    [TestCase("$group")]
    [TestCase("$project")]
    [TestCase("$lookup")]
    [TestCase("$unwind")]
    [TestCase("$facet")]
    [TestCase("$sort")]
    [TestCase("$limit")]
    [TestCase("$skip")]
    [TestCase("$set")]
    [TestCase("$unset")]
    [TestCase("$count")]
    public async Task AllPhaseTwoStagesAreSuggestedAtStagePosition(string stage)
    {
        Assert.That(await LabelsAsync("[{ " + stage), Does.Contain(stage));
    }

    /// <summary>Completion labels produced by the active engine for a cursor at the end of <paramref name="source"/>.</summary>
    private static async Task<string[]> LabelsAsync(string source, EditorDialects dialect = EditorDialects.AggregationJson)
    {
        var analysis = CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), source.Length, dialect, null));
        var service = new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()]));
        var list = await service.CompleteAsync(analysis.Context);
        return list.Items.Select(item => item.Label).ToArray();
    }
}
