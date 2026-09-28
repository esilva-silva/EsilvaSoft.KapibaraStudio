using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Facts;

/// <summary>
/// Janela sintática do cursor: fronteira por statement, teto de vizinhos, corte de statement grande, determinismo e a
/// garantia de que só a sintaxe digitada entra — nunca um valor de resultado de execução.
/// </summary>
[TestFixture]
public sealed class EditorWindowBuilderTests
{
    private const string Script = """
        db.antigo1.find({a:1});
        db.antigo2.find({b:2});
        db.antigo3.find({c:3});
        db.antigo4.find({d:4});
        db.atual.aggregate([{$match:{}}]);
        """;

    [Test]
    public void TheStatementUnderTheCaretIsTheCurrentOne()
    {
        var snapshot = new StringTextSnapshot(Script);
        var caret = Script.IndexOf("$match", StringComparison.Ordinal);

        var window = Build(snapshot, caret);

        Assert.That(window.Current, Is.Not.Null);
        Assert.That(window.Current!.Text, Is.EqualTo("db.atual.aggregate([{$match:{}}]);"));
        Assert.That(window.Current.Truncated, Is.False);
        Assert.That(window.CaretOffset, Is.EqualTo(caret));
    }

    [Test]
    public void ABoundaryIsStructuralAndNotALineCount()
    {
        // Um único statement quebrado em várias linhas continua sendo um statement; contar linhas o cortaria ao meio.
        const string multiline = "db.pedidos.aggregate([\n  {$match: {status: 1}},\n  {$group: {_id: \"$loja\"}}\n]);";
        var snapshot = new StringTextSnapshot(multiline);

        var window = Build(snapshot, multiline.IndexOf("$group", StringComparison.Ordinal));

        Assert.That(window.Current!.Text, Is.EqualTo(multiline));
        Assert.That(window.Preceding, Is.Empty);
    }

    [Test]
    public void OnlyThreeNeighboursComeAlongEvenWithFourBefore()
    {
        var snapshot = new StringTextSnapshot(Script);

        var window = Build(snapshot, Script.IndexOf("$match", StringComparison.Ordinal));

        Assert.That(EditorWindowLimits.Default.MaximumPrecedingStatements, Is.EqualTo(3));
        Assert.That(window.Preceding.Select(statement => statement.Text), Is.EqualTo(ThreeNeighbours));
        Assert.That(window.Render(), Does.Not.Contain("antigo1"));
    }

    [Test]
    public void TheNeighbourLimitIsConfigurableAndKeepsDocumentOrder()
    {
        var snapshot = new StringTextSnapshot(Script);
        var builder = new EditorWindowBuilder(new EditorWindowLimits(1, 1024, 256));

        var window = builder.Build(Context(Script.IndexOf("$match", StringComparison.Ordinal)), snapshot);

        Assert.That(window.Preceding.Select(statement => statement.Text), Is.EqualTo(OneNeighbour));
        Assert.That(window.Statements.Last(), Is.EqualTo(window.Current));
    }

    [Test]
    public void ACaretOnNoStatementStillCarriesTheNeighbours()
    {
        // Linha em branco depois do último ';': o usuário vai começar um comando novo. Não há statement atual, e o que
        // resta de contexto são os vizinhos — exatamente o caso do console.
        var text = Script + "\n";
        var snapshot = new StringTextSnapshot(text);

        var window = Build(snapshot, text.Length);

        Assert.That(window.Current, Is.Null);
        Assert.That(window.Preceding.Select(statement => statement.Text), Is.EqualTo(TailNeighbours));
        Assert.That(window.IsEmpty, Is.False);
    }

    [Test]
    public void ACaretRightOnTheClosingSemicolonBelongsToThatStatement()
    {
        var snapshot = new StringTextSnapshot(Script);

        var window = Build(snapshot, Script.Length);

        Assert.That(window.Current!.Text, Is.EqualTo("db.atual.aggregate([{$match:{}}]);"));
        Assert.That(window.Preceding, Has.Count.EqualTo(3));
    }

    [Test]
    public void AHugeCurrentStatementIsCutAtTheEditorContextWindow()
    {
        var huge = "db.pedidos.find({campo: [" + string.Join(",", Enumerable.Range(0, 900)
            .Select(index => index.ToString(CultureInfo.InvariantCulture))) + "]});";
        var snapshot = new StringTextSnapshot(huge);
        var caret = huge.Length - 3;

        var window = Build(snapshot, caret);

        Assert.That(huge, Has.Length.GreaterThan(EditorWindowLimits.DefaultMaximumCurrentLength));
        Assert.That(window.Current!.Truncated, Is.True);
        Assert.That(window.Current.Text, Has.Length.EqualTo(EditorWindowLimits.DefaultMaximumCurrentLength));
        // O corte mantém o trecho encostado no cursor, porque é ali que o modelo continua escrevendo.
        Assert.That(window.Current.Span.End, Is.EqualTo(caret));
        Assert.That(huge.EndsWith(window.Current.Text + huge[caret..], StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void AHugeNeighbourIsCutAtTheRecentCommandLimit()
    {
        var huge = "db.pedidos.find({campo: [" + string.Join(",", Enumerable.Range(0, 900)
            .Select(index => index.ToString(CultureInfo.InvariantCulture))) + "]});";
        var text = huge + "\ndb.atual.find({});";
        var snapshot = new StringTextSnapshot(text);

        var window = Build(snapshot, text.Length);

        var neighbour = window.Preceding.Single();
        Assert.That(neighbour.Truncated, Is.True);
        Assert.That(neighbour.Text, Has.Length.EqualTo(EditorWindowLimits.DefaultMaximumPrecedingLength));
        Assert.That(neighbour.Text, Is.EqualTo(huge[..EditorWindowLimits.DefaultMaximumPrecedingLength]));
    }

    [Test]
    public void NoExecutionResultValueReachesTheWindow()
    {
        // A sentinela entra por todos os caminhos que carregam dados de execução no contexto: formas locais (resultados
        // já carregados na aba) e símbolos locais. A janela só lê o snapshot, então nada disso pode aparecer.
        const string sentinel = "SENTINELA-VALOR-SECRETO";
        var snapshot = new StringTextSnapshot(Script);
        var schema = new SchemaBuilder().AddDocuments(["{\"campo\":\"" + sentinel + "\"}"]).Build();
        var context = Context(Script.IndexOf("$match", StringComparison.Ordinal)) with
        {
            LocalSchemas = [schema],
            LocalSymbols = [new CatalogSymbol(sentinel, SymbolKind.LocalVariable, sentinel, sentinel)],
            ParentPath = sentinel,
            ValueType = sentinel
        };

        var window = new EditorWindowBuilder().Build(context, snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(window.Render(), Does.Not.Contain(sentinel));
            foreach (var statement in window.Statements) Assert.That(statement.Text, Does.Not.Contain(sentinel));
        });
    }

    [Test]
    public void TheSameSnapshotAndCaretProduceTheSameWindow()
    {
        var snapshot = new StringTextSnapshot(Script);
        var caret = Script.IndexOf("$match", StringComparison.Ordinal);
        var builder = new EditorWindowBuilder();

        var first = builder.Build(Context(caret), snapshot);
        var second = builder.Build(Context(caret), snapshot);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second.Render(), Is.EqualTo(first.Render()));
        Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
    }

    [Test]
    public void AReadyTreeOfTheSameVersionProducesTheSameWindowAsParsingAgain()
    {
        var snapshot = new StringTextSnapshot(Script);
        var tree = new TolerantParser().Parse(snapshot);
        var caret = Script.IndexOf("$match", StringComparison.Ordinal);
        var builder = new EditorWindowBuilder();

        Assert.That(builder.Build(Context(caret), snapshot, tree), Is.EqualTo(builder.Build(Context(caret), snapshot)));
    }

    [Test]
    public void AStaleTreeIsIgnoredInsteadOfShiftingEveryCut()
    {
        var older = new StringTextSnapshot(Script);
        var stale = new TolerantParser().Parse(older);
        var snapshot = older.Insert(0, "db.novo.find({});\n");
        var caret = snapshot.Text.IndexOf("$match", StringComparison.Ordinal);

        var window = new EditorWindowBuilder().Build(Context(caret), snapshot, stale);

        Assert.That(window.Current!.Text, Is.EqualTo("db.atual.aggregate([{$match:{}}]);"));
        Assert.That(window.Preceding.Select(statement => statement.Text), Does.Contain("db.antigo4.find({d:4});"));
    }

    [Test]
    public void AnEmptyDocumentProducesTheEmptyWindow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Build(new StringTextSnapshot(""), 0), Is.EqualTo(EditorWindow.Empty));
            Assert.That(Build(new StringTextSnapshot("   \n  "), 3).IsEmpty, Is.True);
            Assert.That(EditorWindow.Empty.Render(), Is.Empty);
        });
    }

    [Test]
    public void TheBuilderRejectsMissingInputsAndInvalidLimits()
    {
        var builder = new EditorWindowBuilder();
        Assert.Multiple(() =>
        {
            Assert.That(() => builder.Build(null!, new StringTextSnapshot("")), Throws.ArgumentNullException);
            Assert.That(() => builder.Build(Context(0), null!), Throws.ArgumentNullException);
            Assert.That(() => new EditorWindowLimits(-1, 10, 10), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EditorWindowLimits(1, 0, 10), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EditorWindow(null, [null!]), Throws.ArgumentException);
            Assert.That(() => new EditorWindow(null, [], -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ACancelledRequestStopsInsteadOfReturningAPartialWindow()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.That(() => new EditorWindowBuilder().Build(Context(0), new StringTextSnapshot(Script), tree: null, source.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static readonly string[] ThreeNeighbours =
        ["db.antigo2.find({b:2});", "db.antigo3.find({c:3});", "db.antigo4.find({d:4});"];
    private static readonly string[] OneNeighbour = ["db.antigo4.find({d:4});"];
    private static readonly string[] TailNeighbours =
        ["db.antigo3.find({c:3});", "db.antigo4.find({d:4});", "db.atual.aggregate([{$match:{}}]);"];

    private static EditorWindow Build(StringTextSnapshot snapshot, int caret) =>
        new EditorWindowBuilder().Build(Context(caret), snapshot);

    private static CompletionContext Context(int caret) =>
        new(new(1, 1), EditorDialects.MongoshScript, SymbolKinds.Field, "", new(caret, 0));
}
