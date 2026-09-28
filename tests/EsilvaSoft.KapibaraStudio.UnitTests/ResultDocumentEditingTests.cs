using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using MongoDB.Bson;
using static EsilvaSoft.KapibaraStudio.UnitTests.ResultPanelTestFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Document edit availability and the read/mutate flow triggered from the result panel (see also <see cref="ResultPanelTests"/> for rendering).</summary>
[TestFixture]
public sealed class ResultDocumentEditingTests
{
    private static readonly string[] RereadThenReplace = ["QueryAsync", "ReplaceAsync"];

    [Test]
    public async Task EditAvailabilityRequiresIdentityAndCompleteCollectionDocuments()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (method, _) => method is "QueryAsync" or "AggregateAsync" ? Page(false, "{\"_id\":1,\"nome\":\"Ana\"}", "{\"nome\":\"Sem identidade\"}") : throw new InvalidOperationException(method);
        var console = ConsoleTab(context, profile, "db.clientes.find({})");
        await console.ExecuteCommand.ExecuteAsync(null);
        var aggregation = new WorkspaceTabViewModel(context.Workspace) { Mode = "Agregação", Profile = profile, Database = "loja", Collection = "clientes", Text = "[]", IsConnected = true };
        await aggregation.ExecuteCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(console.ResultDocuments[0]), Is.EqualTo(new ResultEditAvailability(true, "")));
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(console.ResultDocuments[1]).Reason, Does.StartWith("Documento sem _id"));
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(aggregation.ResultDocuments[0]).Reason, Does.StartWith("Resultado de agregação"));
            Assert.That(aggregation.ResultTree.First().Name, Is.EqualTo("Agregação"));
            Assert.Throws<InvalidOperationException>(() => console.CreateResultDocumentEditor(console.ResultDocuments[1]));
        });
    }

    [TestCase("unchanged")]
    [TestCase("changed")]
    [TestCase("removed")]
    public async Task EditorOpensTheCopyWithoutIoAndRereadsTheDocumentBeforeWriting(string scenario)
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        const string stored = "{\"_id\":1,\"nome\":\"Ana\",\"2024\":true,\"id\":" + "{\"$binary\":{\"base64\":\"MyIRAFVEd2aImaq7zN3u/w==\",\"subType\":\"03\"}}}";
        var calls = new List<string>();
        context.Mongo.Handler = (method, _) => { calls.Add(method); return Page(false, stored); };
        var tab = ConsoleTab(context, profile, "db.clientes.find({})");
        tab.UuidPolicy = new UuidDisplayPolicy(UuidRepresentation.CSharpLegacy, null);
        await tab.ExecuteCommand.ExecuteAsync(null);
        calls.Clear();
        var document = tab.ResultDocuments.Single();
        Assert.That(document.Json, Is.EqualTo(stored), "A resposta do driver conserva a ordem dos campos.");

        using (var cancelled = tab.CreateResultDocumentEditor(document)) cancelled.Text = "{\"_id\":1}";
        using var editor = tab.CreateResultDocumentEditor(document);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Empty, "Abrir ou cancelar a edição não consulta nem grava.");
            Assert.That(editor.Text, Is.EqualTo(document.FormattedJson));
            Assert.That(editor.Text, Does.Contain("\"id\": CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")"));
            Assert.That(editor.Policy, Does.Contain("nada foi lido ou gravado ao abrir"));
            Assert.That((editor.CanApply, editor.ApplyLabel), Is.EqualTo((true, "Salvar…")));
            Assert.That(tab.IsRunning, Is.False);
        });

        MongoQuery? reread = null; string? filter = null, replacement = null;
        context.Mongo.Handler = (method, args) =>
        {
            calls.Add(method);
            if (method == "QueryAsync")
            {
                reread = (MongoQuery)args[1]!;
                return scenario switch
                {
                    "removed" => Page(false),
                    "changed" => Page(false, stored.Replace("\"Ana\"", "\"Outra\"", StringComparison.Ordinal)),
                    _ => Page(false, "{\n  \"_id\" : 1,\n  \"nome\" : \"Ana\",\n  \"2024\" : true,\n  \"id\" : {\"$binary\" : {\"base64\" : \"MyIRAFVEd2aImaq7zN3u/w==\", \"subType\" : \"03\"}}\n}")
                };
            }
            if (method == "ReplaceAsync") { filter = (string)args[3]!; replacement = (string)args[4]!; return Task.FromResult(new DocumentMutationResult(1, 1)); }
            throw new InvalidOperationException(method);
        };
        editor.Text = editor.Text.Replace("\"Ana\"", "\"Ana Maria\"", StringComparison.Ordinal);
        await editor.ExecuteConfirmedAsync();
        Assert.That((reread!.Database, reread.Collection, reread.FilterJson, reread.Limit), Is.EqualTo(("loja", "clientes", "{\"_id\":1}", 1)));
        switch (scenario)
        {
            case "unchanged":
                Assert.Multiple(() =>
                {
                    Assert.That(calls, Is.EqualTo(RereadThenReplace));
                    Assert.That((editor.Succeeded, editor.Conflict), Is.EqualTo((true, DocumentWriteConflict.None)));
                    Assert.That(BsonDocument.Parse(filter!)["$and"][1]["$expr"]["$eq"][1]["$literal"], Is.EqualTo(BsonDocument.Parse(stored)));
                    Assert.That(BsonDocument.Parse(UuidCodec.RewriteConstructors(replacement!)), Is.EqualTo(BsonDocument.Parse(stored.Replace("\"Ana\"", "\"Ana Maria\"", StringComparison.Ordinal))));
                });
                break;
            case "changed":
                Assert.That((calls.Count, editor.Succeeded, editor.Conflict), Is.EqualTo((1, false, DocumentWriteConflict.Changed)));
                Assert.That(editor.Status, Does.Contain("alterado no servidor").And.Contain("Nada foi gravado"));
                break;
            default:
                Assert.That((calls.Count, editor.Succeeded, editor.Conflict), Is.EqualTo((1, false, DocumentWriteConflict.Removed)));
                Assert.That(editor.Status, Does.Contain("removido").And.Contain("Nada foi gravado"));
                break;
        }
    }

    [Test]
    public async Task ReadOnlyOrClosedConnectionOpensTheCopyButBlocksSaving()
    {
        using var context = new WorkspaceTestContext();
        var calls = 0;
        context.Mongo.Handler = (_, _) => { calls++; return Page(false, "{\"_id\":1}"); };
        var readOnly = ConsoleTab(context, ConnectionProfile.Create("Produção", "mongodb://host", isReadOnly: true), "db.clientes.find({})");
        await readOnly.ExecuteCommand.ExecuteAsync(null);
        var closed = ConsoleTab(context, ConnectionProfile.Create("Dev", "mongodb://host"), "db.clientes.find({})");
        await closed.ExecuteCommand.ExecuteAsync(null);
        closed.IsConnected = false;
        calls = 0;

        using var blocked = readOnly.CreateResultDocumentEditor(readOnly.ResultDocuments.Single());
        using var disconnected = closed.CreateResultDocumentEditor(closed.ResultDocuments.Single());
        Assert.Multiple(() =>
        {
            Assert.That((blocked.CanApply, blocked.Status), Is.EqualTo((false, "Conexão somente leitura: gravação bloqueada.")));
            Assert.That(blocked.Policy, Does.StartWith("Somente leitura"));
            Assert.CatchAsync(blocked.ExecuteConfirmedAsync);
            Assert.That(closed.ResultDocuments, Has.Count.EqualTo(1), "Fechar a conexão não descarta o resultado.");
            Assert.That(disconnected.CanApply, Is.False);
            Assert.That(disconnected.WriteBlockReason, Does.StartWith("Conexão desta aba fechada"));
        });
        await disconnected.ExecuteConfirmedAsync();
        Assert.That((disconnected.Status, calls), Is.EqualTo((disconnected.WriteBlockReason, 0)));
    }
}
