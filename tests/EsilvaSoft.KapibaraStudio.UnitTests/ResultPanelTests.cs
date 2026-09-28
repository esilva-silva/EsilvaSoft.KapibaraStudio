using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using static EsilvaSoft.KapibaraStudio.UnitTests.ResultPanelTestFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ResultPanelTests
{
    private static readonly string[] FieldNames = ["_id", "endereco.cidade", "nulo", "vazio", "itens", "profundo"];
    private static readonly string[] ItemNames = ["[0]", "[1]"];
    private static readonly string[] ProjectedNotices = ["Projeção parcial", "Resultado limitado"];

    [Test]
    public async Task SwitchingViewsKeepsDocumentsOriginAndSelectionAcrossResultSets()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (_, args) => ((MongoQuery)args[1]!).Collection == "clientes"
            ? Page(true, "{\"_id\":1,\"nome\":\"Ana\"}", "{\"_id\":2,\"nome\":\"Bruno\"}")
            : Page(false, "{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"total\":{\"$numberDecimal\":\"10.50\"}}");
        var tab = ConsoleTab(context, profile, "db.clientes.find({})\ndb.pedidos.find({})");
        await tab.ExecuteCommand.ExecuteAsync(null);

        Assert.That(tab.ResultTree, Has.Count.EqualTo(2));
        Assert.That(tab.ResultTree[0].IsExpanded, Is.True, "O primeiro conjunto com documentos abre por padrão.");
        Assert.That(tab.ResultTree[1].IsChildrenLoaded, Is.False, "Conjuntos recolhidos não criam linhas antes da expansão.");
        tab.ResultTree[1].IsExpanded = true;
        var orderNode = tab.ResultTree[1].Children.Single(node => node.IsDocument);
        tab.SelectedResultNode = orderNode;
        var order = tab.SelectedDocument!;
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.SameAs(orderNode.Document));
            Assert.That(tab.SelectedConsoleResult!.Collection, Is.EqualTo("pedidos"));
            Assert.That(tab.ResultDocuments.Single(), Is.SameAs(order));
            Assert.That(tab.Documents.Single(), Does.Contain("10.50"));
            Assert.That(order.OriginText, Does.Contain("[2]").And.Contain("find"));
            Assert.That(order.DestinationText, Is.EqualTo("Dev › loja › pedidos"));
        });

        tab.IsJsonResultView = true;
        Assert.That(tab.SelectedDocument, Is.SameAs(order));
        var segment = tab.ResultSegments.Single(s => ReferenceEquals(s.Document, order));
        Assert.That(tab.Results.Substring(segment.Start, segment.Length), Is.EqualTo(order.FormattedJson));
        tab.IsTreeResultView = true;
        Assert.Multiple(() =>
        {
            Assert.That(tab.ResultView, Is.EqualTo(ResultViewMode.Tree));
            Assert.That(tab.SelectedDocument, Is.SameAs(order));
            Assert.That(tab.SelectedResultNode, Is.SameAs(orderNode));
            Assert.That(tab.ResultTree, Has.Count.EqualTo(2));
            Assert.That(tab.Results, Does.Contain("// [1] Dev › loja › clientes · find · 2 documento(s) · limitado"));
            Assert.That(tab.Results, Does.Contain("\n  \"total\": {\"$numberDecimal\":\"10.50\"}\n"));
        });

        var ana = tab.ResultSegments[0].Document;
        tab.SelectResultDocument(ana);
        Assert.Multiple(() =>
        {
            Assert.That(tab.SelectedConsoleResult!.Collection, Is.EqualTo("clientes"));
            Assert.That(tab.SelectedResultNode!.Document, Is.SameAs(ana));
            Assert.That(tab.ResultDocuments, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task TreeExpandsObjectsAndArraysOnDemandWithNamesIndexesTypesAndValues()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var deep = string.Concat(Enumerable.Range(0, 40).Select(i => "{\"n" + i + "\":")) + "{\"folha\":{\"$numberLong\":\"9223372036854775807\"}}" + new string('}', 40);
        var stored = "{\"_id\":1,\"endereco.cidade\":\"Recife\",\"nulo\":null,\"vazio\":[],\"itens\":[{\"sku\":\"a\"},{\"sku\":\"b\",\"tags\":[\"x\"]}],\"profundo\":" + deep + "}";
        context.Mongo.Handler = (_, _) => Page(false, stored);
        var tab = ConsoleTab(context, profile, "db.clientes.find({})");
        await tab.ExecuteCommand.ExecuteAsync(null);

        var document = tab.ResultTree.Single().Children.Single(node => node.IsDocument);
        Assert.That(document.IsExpanded, Is.True, "Um documento único abre por padrão.");
        var fields = document.Children;
        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(f => f.Name), Is.EqualTo(FieldNames));
            Assert.That((fields[0].TypeName, fields[0].Value), Is.EqualTo(("Número", "1")));
            Assert.That((fields[1].TypeName, fields[1].Value), Is.EqualTo(("String", "\"Recife\"")));
            Assert.That((fields[2].TypeName, fields[2].Value), Is.EqualTo(("Null", "null")));
            Assert.That((fields[3].TypeName, fields[3].Value, fields[3].Children.Count), Is.EqualTo(("Array", "[]", 0)));
            Assert.That((fields[4].TypeName, fields[4].Value, fields[4].IsChildrenLoaded), Is.EqualTo(("Array", "2 itens", false)));
            Assert.That(fields[4].Children.Single().Name, Is.EqualTo("Carregando…"));
        });
        fields[4].IsExpanded = true;
        var items = fields[4].Children;
        Assert.That(items.Select(i => i.Name), Is.EqualTo(ItemNames));
        items[1].IsExpanded = true;
        items[1].Children[1].IsExpanded = true;
        Assert.That((items[1].Children[1].Children.Single().Name, items[1].Children[1].Children.Single().Value), Is.EqualTo(("[0]", "\"x\"")));

        var node = fields[5];
        for (var i = 0; i < 40; i++) { node.IsExpanded = true; node = node.Children.Single(); }
        node.IsExpanded = true;
        node = node.Children.Single();
        Assert.That((node.Name, node.TypeName, node.Value), Is.EqualTo(("folha", "Int64", "9223372036854775807")));
        fields[4].IsExpanded = false;
        Assert.That(fields[4].Children, Has.Count.EqualTo(2), "Recolher não descarta nem recarrega filhos.");
    }

    [Test]
    public async Task EmptyTruncatedProjectedAndInvalidResultsAreShownAsText()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (_, args) => ((MongoQuery)args[1]!).Collection == "vazio" ? Page(false) : Page(true, "{\"_id\":1,\"nome\":\"Ana\"}");
        var console = ConsoleTab(context, profile, "db.vazio.find({})\ndb.clientes.find({}, {nome: 1})");
        await console.ExecuteCommand.ExecuteAsync(null);
        var projected = console.ResultTree[1];
        projected.IsExpanded = true;
        Assert.Multiple(() =>
        {
            Assert.That(console.ResultTree[0].IsExpanded, Is.False);
            console.ResultTree[0].IsExpanded = true;
            Assert.That(console.ResultTree[0].Children.Single().Value, Is.EqualTo("Nenhum documento neste resultado."));
            Assert.That(console.Results, Does.Contain("// [1] Dev › loja › vazio · find · 0 documento(s)\n// Nenhum documento neste resultado."));
            Assert.That(projected.Value, Is.EqualTo("limitado · projeção parcial"));
            Assert.That(projected.Children.Where(n => n.IsNotice).Select(n => n.Name), Is.EqualTo(ProjectedNotices));
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(projected.Children.Single(n => n.IsDocument).Document!).Reason, Does.StartWith("Projeção parcial"));
        });

        var script = new WorkspaceTabViewModel(context.Workspace) { Mode = "Script", Profile = profile, Database = "loja", Text = "slop.results.emit({})", IsConnected = true };
        var run = script.ExecuteCommand.ExecuteAsync(null);
        context.Scripts.Calls[0].Completion.SetResult(new(0, ["{\"_id\":1}", "{inválido"], "", "", TimeSpan.Zero));
        await run;
        Assert.Multiple(() =>
        {
            Assert.That(script.ResultTree.Select(n => (n.Name, n.TypeName)), Is.EqualTo(new[] { ("Documento 1", "Documento"), ("Documento 2", "JSON inválido") }));
            Assert.That(script.Results, Does.Contain("// Documento 2: JSON inválido — "));
            Assert.That(script.Results, Does.Contain("{inválido"));
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(script.ResultDocuments[0]).Reason, Does.Contain("coleção de origem"));
            Assert.That(WorkspaceTabViewModel.GetEditAvailability(script.ResultDocuments[1]).Reason, Does.StartWith("JSON inválido"));
        });

        var none = ConsoleTab(context, profile, "const x = 1;");
        await none.ExecuteCommand.ExecuteAsync(null);
        Assert.That((none.HasResultTree, none.ResultTreeStatus), Is.EqualTo((false, "Nenhuma expressão retornou resultado. Consulte Mensagens.")));

        context.Mongo.Handler = (_, _) => Page(false);
        var query = new WorkspaceTabViewModel(context.Workspace) { Mode = "Consulta JSON", Profile = profile, Database = "loja", Collection = "clientes", Text = "{}", IsConnected = true };
        await query.ExecuteCommand.ExecuteAsync(null);
        Assert.That((query.Results, query.ResultTree.Single().Value), Is.EqualTo(("Nenhum documento encontrado.", "Nenhum documento neste resultado.")));
    }

    [Test]
    public async Task ReadOnlyViewUsesMemoryOnly()
    {
        using var context = new WorkspaceTestContext();
        var calls = 0;
        context.Mongo.Handler = (_, _) => { calls++; return Page(false, "{\"_id\":1,\"valor\":{\"$numberLong\":\"9223372036854775807\"}}"); };
        var tab = ConsoleTab(context, ConnectionProfile.Create("Dev", "mongodb://host"), "db.clientes.find({})");
        await tab.ExecuteCommand.ExecuteAsync(null);
        calls = 0;
        var document = tab.ResultDocuments.Single();
        var view = new DocumentJsonViewModel(document, 16);
        Assert.Multiple(() =>
        {
            Assert.That(view.Json, Is.EqualTo("{\n  \"_id\": 1,\n  \"valor\": {\"$numberLong\":\"9223372036854775807\"}\n}"));
            Assert.That(view.Destination, Is.EqualTo("Dev › loja › clientes"));
            Assert.That(view.Origin, Does.Contain("[1]"));
            Assert.That(view.Identity, Is.EqualTo("_id: 1"));
            Assert.That(view.CodeLineHeight, Is.EqualTo(24));
            Assert.That((calls, tab.IsRunning, tab.Status, context.Scripts.Calls.Count), Is.EqualTo((0, false, "Concluído", 0)));
        });
    }

    [Test]
    public async Task TabsKeepTheirOwnResultsViewAndSelectionWhenResponsesArriveOutOfOrder()
    {
        using var context = new WorkspaceTestContext();
        var started = new Dictionary<string, TaskCompletionSource> { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var responses = new Dictionary<string, TaskCompletionSource<QueryPage>> { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Mongo.Handler = (_, args) =>
        {
            var database = ((MongoQuery)args[1]!).Database;
            started[database].TrySetResult();
            return responses[database].Task.WaitAsync((CancellationToken)args[2]!);
        };
        var a = ConsoleTab(context, ConnectionProfile.Create("A", "mongodb://a"), "db.clientes.find({})", "a");
        var b = ConsoleTab(context, ConnectionProfile.Create("B", "mongodb://b"), "db.clientes.find({})", "b");
        var runA = a.ExecuteCommand.ExecuteAsync(null); var runB = b.ExecuteCommand.ExecuteAsync(null);
        await Task.WhenAll(started["a"].Task, started["b"].Task).WaitAsync(TimeSpan.FromSeconds(5));
        responses["b"].SetResult(new(["{\"_id\":1,\"nome\":\"Bruno\"}", "{\"_id\":2,\"nome\":\"Bia\"}"], TimeSpan.Zero, false)); await runB;
        b.IsTreeResultView = true;
        b.SelectedResultNode = b.ResultTree.Single().Children.Last(node => node.IsDocument);
        var selectedB = b.SelectedDocument;
        var treeB = b.ResultTree.Single();
        Assert.That((a.IsRunning, a.HasResultTree, a.Results), Is.EqualTo((true, false, "Executando…")));

        responses["a"].SetResult(new(["{\"_id\":9,\"nome\":\"Ana\"}"], TimeSpan.Zero, false)); await runA;
        Assert.Multiple(() =>
        {
            Assert.That(a.ResultView, Is.EqualTo(ResultViewMode.Json));
            Assert.That(a.Results, Does.Contain("Ana").And.Not.Contain("Bruno"));
            Assert.That(b.ResultTree.Single(), Is.SameAs(treeB));
            Assert.That(b.SelectedDocument, Is.SameAs(selectedB));
            Assert.That(b.Results, Does.Not.Contain("Ana"));
        });

        a.Database = "outro";
        Assert.Multiple(() =>
        {
            Assert.That((a.HasResultTree, a.SelectedDocument, a.ResultSegments.Count, a.ConsoleResults.Count), Is.EqualTo((false, (ResultDocumentViewModel?)null, 0, 0)));
            Assert.That(a.ResultTreeStatus, Is.EqualTo("Execute para visualizar os resultados em Extended JSON."));
            Assert.That(b.SelectedDocument, Is.SameAs(selectedB));
            Assert.That(b.ResultView, Is.EqualTo(ResultViewMode.Tree));
            Assert.That(JsonSerializer.Serialize(b.Snapshot()), Does.Not.Contain("Bruno").And.Not.Contain("Bia"), "Resultados não entram no snapshot.");
        });
    }

    [Test]
    public async Task UuidPreferenceChangeRendersAgainKeepingSelectionAndExpansion()
    {
        using var context = new WorkspaceTestContext();
        var legacy = Binary("33221100554477668899aabbccddeeff", "03");
        context.Mongo.Handler = (_, _) => Page(false, "{\"_id\":1}", "{\"_id\":2,\"cliente\":{\"id\":" + legacy + "}}");
        var tab = ConsoleTab(context, ConnectionProfile.Create("Dev", "mongodb://host"), "db.clientes.find({})");
        await tab.ExecuteCommand.ExecuteAsync(null);
        var second = tab.ResultTree.Single().Children.Last(node => node.IsDocument);
        tab.SelectedResultNode = second;
        second.IsExpanded = true;
        second.Children.Single(n => n.Name == "cliente").IsExpanded = true;
        Assert.That(second.Children.Single(n => n.Name == "cliente").Children.Single().TypeName, Is.EqualTo("Binary 03 · UUID legado"));

        tab.UuidPolicy = new UuidDisplayPolicy(UuidRepresentation.CSharpLegacy, null);
        var rendered = tab.ResultTree.Single().Children.Last(node => node.IsDocument);
        var client = rendered.Children.Single(n => n.Name == "cliente");
        Assert.Multiple(() =>
        {
            Assert.That(rendered, Is.Not.SameAs(second));
            Assert.That(tab.SelectedDocument!.Label, Is.EqualTo("Documento 2"));
            Assert.That(tab.SelectedResultNode, Is.SameAs(rendered));
            Assert.That((rendered.IsExpanded, client.IsExpanded), Is.EqualTo((true, true)));
            Assert.That(client.Children.Single().Value, Is.EqualTo("CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")"));
            Assert.That(tab.Results, Does.Contain("\"id\": CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")"));
            Assert.That(tab.SelectedDocument.Json, Does.Contain("\"subType\":\"03\""));
        });
    }
}
