using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

/// <summary>
/// A sobrecarga com <see cref="SyntaxTreeCache"/> está reservada a quem precisar dos nós da árvore; o caminho por
/// tecla usa o <see cref="TokenCache"/> (ver <see cref="CompletionContextEngineTokenTests"/>). Estes testes provam
/// que ela produz exatamente o mesmo contexto do caminho sem cache (lexificação completa do snapshot) e que a
/// árvore de um documento nunca é servida a outro.
/// </summary>
[TestFixture]
public sealed class CompletionContextEngineTreeTests
{
    private const string Document = """
        // pedidos do mês
        const c = db.Pedidos;
        db.Clientes.find({ "Nome": "João😀", ativo: /x/i });
        c.aggregate([{ $match: { ativo: true } }, { $group: { _id: "$clienteId", total: { $sum: 1 } } }]);
        """;

    /// <summary>Caracteres que mudam o estado léxico entre linhas: aspas, comentários, regex, template e CRLF.</summary>
    private static readonly string[] Inserts = ["x", "\r\n", "😀", "'", "\"", "}", "{", "[", "//", "/*", "*/", "/abc/i", "`", " ", ".", "$", ":", ",", ";"];

    [TestCase(25)]
    [TestCase(7341)]
    [TestCase(20260915)]
    [TestCase(1183)]
    public void SeededEditsKeepTheIncrementalContextIdenticalToTheFullParse(int seed)
    {
        var random = new Random(seed);
        var trees = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot(Document.ReplaceLineEndings("\r\n"));
        var schema = Schema;

        for (var edit = 0; edit < 40; edit++)
        {
            foreach (var caret in Carets(random, snapshot.Length))
            {
                var request = new ContextRequest(snapshot, caret, EditorDialects.Console, Scope) { InputSchema = schema };
                AssertSameAnalysis(CompletionContextEngine.Analyze(request), CompletionContextEngine.Analyze(request, trees),
                    $"seed={seed}, edit={edit}, caret={caret}");
            }
            // A árvore da versão precisa existir e ter vindo do caminho incremental a partir da segunda edição.
            var parsed = trees.GetOrParse(snapshot);
            Assert.That(parsed.IsReused, Is.True, $"seed={seed}, edit={edit}: a análise deve ter deixado a árvore da versão em cache.");
            var offset = random.Next(snapshot.Length + 1);
            var removed = random.Next(Math.Min(6, snapshot.Length - offset) + 1);
            snapshot = snapshot.Replace(offset, removed, Inserts[random.Next(Inserts.Length)]);
        }
    }

    [Test]
    public void EveryEditAfterTheFirstReachesTheContextThroughAnIncrementalParse()
    {
        var trees = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot("db.Clientes.find({ ");
        var kinds = new List<SyntaxTreeParseKind>();
        // A primeira análise da aba é a única completa; a partir daí cada tecla reaproveita a árvore anterior.
        Assert.That(trees.GetOrParse(snapshot).ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        foreach (var typed in "Nome")
        {
            snapshot = snapshot.Insert(snapshot.Length, typed.ToString());
            var before = trees.GetOrParse(snapshot).ParseKind;
            kinds.Add(before);
            var analysis = CompletionContextEngine.Analyze(new(snapshot, snapshot.Length, EditorDialects.Console, Scope), trees);
            Assert.That(analysis.Role, Is.EqualTo(CompletionCursorRole.PropertyKey));
        }
        Assert.That(kinds, Is.All.EqualTo(SyntaxTreeParseKind.Incremental));
    }

    [TestCase(EditorDialects.Console)]
    [TestCase(EditorDialects.MongoshScript)]
    [TestCase(EditorDialects.AggregationJson)]
    public void ReusedTreeDoesNotReadTheDocumentAgain(EditorDialects dialect)
    {
        var trees = new SyntaxTreeCache();
        var source = new StringTextSnapshot(Document);
        var cold = new CountingSnapshot(source);
        var warm = new CountingSnapshot(source);

        var first = CompletionContextEngine.Analyze(new(cold, 30, dialect, Scope), trees);
        var second = CompletionContextEngine.Analyze(new(warm, 30, dialect, Scope), trees);

        AssertSameAnalysis(first, second, dialect.ToString());
        // Frio: o motor lê o texto e o parser lê o dele. Quente: só o motor — a lexificação e a análise são reaproveitadas.
        Assert.That((cold.Reads, warm.Reads), Is.EqualTo((2, 1)));
    }

    [Test]
    public void TabsWithTheSameVersionNumbersNeverShareATree()
    {
        var trees = new SyntaxTreeCache();
        var pedidos = new StringTextSnapshot("db.Pedidos.find({ ");
        var clientes = new StringTextSnapshot("db.Clientes.find({ ");
        Assert.That(pedidos.Version.Sequence, Is.EqualTo(clientes.Version.Sequence));

        for (var round = 0; round < 3; round++)
        {
            foreach (var snapshot in new[] { pedidos, clientes })
            {
                var request = new ContextRequest(snapshot, snapshot.Length, EditorDialects.Console, Scope);
                AssertSameAnalysis(CompletionContextEngine.Analyze(request), CompletionContextEngine.Analyze(request, trees),
                    $"round={round}");
            }
        }

        var target = CompletionContextEngine.Analyze(new(pedidos, pedidos.Length, EditorDialects.Console, Scope), trees).Target;
        Assert.That(target.Collection, Is.EqualTo("Pedidos"));
        Assert.That(CompletionContextEngine.Analyze(new(clientes, clientes.Length, EditorDialects.Console, Scope), trees).Target.Collection,
            Is.EqualTo("Clientes"));
    }

    [Test]
    public void ClosingATabReleasesItsTreeAndTheNextDocumentStartsCold()
    {
        var trees = new SyntaxTreeCache();
        var source = new StringTextSnapshot("db.Clientes.find({ ");
        var first = new CountingSnapshot(source);
        var reopened = new CountingSnapshot(source);

        _ = CompletionContextEngine.Analyze(new(first, first.Length, EditorDialects.Console, Scope), trees);
        trees.RemoveDocument(source.Version.DocumentId);
        _ = CompletionContextEngine.Analyze(new(reopened, reopened.Length, EditorDialects.Console, Scope), trees);

        Assert.That((first.Reads, reopened.Reads), Is.EqualTo((2, 2)), "Após fechar a aba, nada da árvore anterior é reaproveitado.");
    }

    [Test]
    public void ASupersededSnapshotStillProducesItsOwnContextWithoutBorrowingAnotherVersion()
    {
        var trees = new SyntaxTreeCache();
        var older = new StringTextSnapshot("db.Pedidos.find({ ");
        var newer = older.Replace(3, 7, "Clientes");
        _ = CompletionContextEngine.Analyze(new(newer, newer.Length, EditorDialects.Console, Scope), trees);

        var late = CompletionContextEngine.Analyze(new(older, older.Length, EditorDialects.Console, Scope), trees);

        Assert.That(trees.GetOrParse(older).IsSuperseded, Is.True);
        AssertSameAnalysis(CompletionContextEngine.Analyze(new(older, older.Length, EditorDialects.Console, Scope)), late, "superseded");
        Assert.That(late.Target.Collection, Is.EqualTo("Pedidos"));
    }

    [Test]
    public void OneMegabyteDocumentIsAnalyzedWithoutExceptionAndWithoutGuessingATarget()
    {
        var text = string.Concat(Enumerable.Repeat("db.Clientes.find({ ativo: true });\r\n", 1024 * 1024 / 36));
        var snapshot = new StringTextSnapshot(text);
        var trees = new SyntaxTreeCache();
        var caret = text.Length - 20;

        var withTree = CompletionContextEngine.Analyze(new(snapshot, caret, EditorDialects.Console, Scope), trees);

        AssertSameAnalysis(CompletionContextEngine.Analyze(new(snapshot, caret, EditorDialects.Console, Scope)), withTree, "1MiB");
        // Acima de NamespaceTargetResolver.MaximumDocumentLength o alvo é Unknown: nenhum scan vira ilimitado.
        Assert.That(snapshot.Length, Is.GreaterThan(NamespaceTargetResolver.MaximumDocumentLength));
        Assert.That(withTree.Target, Is.EqualTo(NamespaceTarget.Unknown));
    }

    [Test]
    public void CancellationIsObservedBeforeTheTreeIsPublished()
    {
        var trees = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot(Document);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope), trees, cancellation.Token));
        AssertSameAnalysis(CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope)),
            CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope), trees),
            "a tentativa cancelada não envenena a próxima análise");
    }

    private static IEnumerable<int> Carets(Random random, int length)
    {
        yield return 0;
        yield return length;
        for (var index = 0; index < 6; index++) yield return random.Next(length + 1);
    }

    private static void AssertSameAnalysis(CompletionContextAnalysis expected, CompletionContextAnalysis actual, string because)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Role, Is.EqualTo(expected.Role), because);
            Assert.That(actual.InsertSpan, Is.EqualTo(expected.InsertSpan), because);
            Assert.That(actual.Quote, Is.EqualTo(expected.Quote), because);
            Assert.That(actual.ExistingProperty, Is.EqualTo(expected.ExistingProperty), because);
            Assert.That(actual.Target, Is.EqualTo(expected.Target), because);
            var (a, e) = (actual.Context, expected.Context);
            Assert.That((a.Version, a.Dialect, a.ExpectedKinds, a.Prefix, a.ReplaceSpan),
                Is.EqualTo((e.Version, e.Dialect, e.ExpectedKinds, e.Prefix, e.ReplaceSpan)), because);
            Assert.That((a.ParentPath, a.ShapeId, a.ValueType, a.Scope, a.CatalogAccess, a.RestrictFieldsToLocalSchemas),
                Is.EqualTo((e.ParentPath, e.ShapeId, e.ValueType, e.Scope, e.CatalogAccess, e.RestrictFieldsToLocalSchemas)), because);
            Assert.That(a.LocalSchemas.Select(schema => string.Join(',', schema.Paths())),
                Is.EqualTo(e.LocalSchemas.Select(schema => string.Join(',', schema.Paths()))), because);
        });
    }

    private static CatalogScope Scope { get; } =
        new(new ConnectionIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"), "localhost", "hash"), "Projetos", "Clientes");

    private static CollectionSchema Schema => new SchemaBuilder().AddJsonSchema("""
        { "bsonType": "object", "properties": {
          "_id": { "bsonType": "objectId" },
          "clienteId": { "bsonType": "string" },
          "ativo": { "bsonType": "bool" },
          "Nome": { "bsonType": "string" }
        } }
        """).Build();

    /// <summary>Conta leituras do documento: uma árvore reaproveitada não pode custar uma nova leitura do texto.</summary>
    private sealed class CountingSnapshot(ITextSnapshot inner) : ITextSnapshot
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public TextSnapshotVersion Version => inner.Version;
        public int Length => inner.Length;
        public char this[int index] => inner[index];
        public string GetText(int start, int length)
        {
            Interlocked.Increment(ref _reads);
            return inner.GetText(start, length);
        }
        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous) =>
            inner.GetChangesSince(previous is CountingSnapshot other ? other.Inner : previous);
        private ITextSnapshot Inner => inner;
    }
}
