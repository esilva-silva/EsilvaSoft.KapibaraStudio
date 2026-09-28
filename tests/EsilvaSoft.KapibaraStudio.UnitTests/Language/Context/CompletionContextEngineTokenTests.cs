using System.Reflection;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

/// <summary>
/// O caminho por tecla do contexto passa pelo <see cref="TokenCache"/>: uma tecla custa uma lexificação, a mesma
/// versão analisada de novo custa zero e nenhum nó de árvore é construído. Estes testes provam a equivalência com
/// o caminho sem cache, o isolamento por aba e a liberação ao fechar a aba.
/// </summary>
[TestFixture]
public sealed class CompletionContextEngineTokenTests
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
    public void SeededEditsKeepTheCachedContextIdenticalToTheFullLexing(int seed)
    {
        var random = new Random(seed);
        var tokens = new TokenCache();
        var snapshot = new StringTextSnapshot(Document.ReplaceLineEndings("\r\n"));
        var schema = Schema;

        for (var edit = 0; edit < 40; edit++)
        {
            foreach (var caret in Carets(random, snapshot.Length))
            {
                var request = new ContextRequest(snapshot, caret, EditorDialects.Console, Scope) { InputSchema = schema };
                AssertSameAnalysis(CompletionContextEngine.Analyze(request), CompletionContextEngine.Analyze(request, tokens),
                    $"seed={seed}, edit={edit}, caret={caret}");
            }
            // A versão analisada continua em cache: a próxima análise dela não lexifica de novo.
            Assert.That(tokens.GetOrLex(snapshot).LexKind, Is.EqualTo(TokenCacheLexKind.Reused), $"seed={seed}, edit={edit}");
            var offset = random.Next(snapshot.Length + 1);
            var removed = random.Next(Math.Min(6, snapshot.Length - offset) + 1);
            snapshot = snapshot.Replace(offset, removed, Inserts[random.Next(Inserts.Length)]);
        }
    }

    [Test]
    public void EachKeystrokeLexesOnceAndEveryRepeatedAnalysisOfTheSameVersionLexesNothing()
    {
        var tokens = new TokenCache();
        var snapshot = new StringTextSnapshot("db.Clientes.find({ ");
        var kinds = new List<TokenCacheLexKind>();

        foreach (var typed in "Nome")
        {
            snapshot = snapshot.Insert(snapshot.Length, typed.ToString());
            var analysis = CompletionContextEngine.Analyze(new(snapshot, snapshot.Length, EditorDialects.Console, Scope), tokens);
            Assert.That(analysis.Role, Is.EqualTo(CompletionCursorRole.PropertyKey));
            // Refiltro com a lista aberta: mesma versão, nenhuma lexificação nova.
            _ = CompletionContextEngine.Analyze(new(snapshot, snapshot.Length, EditorDialects.Console, Scope), tokens);
            kinds.Add(tokens.GetOrLex(snapshot).LexKind);
        }

        Assert.That(kinds, Is.All.EqualTo(TokenCacheLexKind.Reused));
    }

    [TestCase(EditorDialects.Console)]
    [TestCase(EditorDialects.MongoshScript)]
    [TestCase(EditorDialects.AggregationJson)]
    public void ReusedTokensDoNotReadTheDocumentAgain(EditorDialects dialect)
    {
        var tokens = new TokenCache();
        var source = new StringTextSnapshot(Document);
        var cold = new CountingSnapshot(source);
        var warm = new CountingSnapshot(source);

        var first = CompletionContextEngine.Analyze(new(cold, 30, dialect, Scope), tokens);
        var second = CompletionContextEngine.Analyze(new(warm, 30, dialect, Scope), tokens);

        AssertSameAnalysis(first, second, dialect.ToString());
        // Frio: uma única materialização do texto. Quente: nenhuma — texto e tokens vêm do cache da versão.
        Assert.That((cold.Reads, warm.Reads), Is.EqualTo((1, 0)));
    }

    [Test]
    public void TabsWithTheSameVersionNumbersNeverShareTokens()
    {
        var tokens = new TokenCache();
        var pedidos = new StringTextSnapshot("db.Pedidos.find({ ");
        var clientes = new StringTextSnapshot("db.Clientes.find({ ");
        Assert.That(pedidos.Version.Sequence, Is.EqualTo(clientes.Version.Sequence));

        for (var round = 0; round < 3; round++)
        {
            foreach (var snapshot in new[] { pedidos, clientes })
            {
                var request = new ContextRequest(snapshot, snapshot.Length, EditorDialects.Console, Scope);
                AssertSameAnalysis(CompletionContextEngine.Analyze(request), CompletionContextEngine.Analyze(request, tokens),
                    $"round={round}");
            }
        }

        Assert.That(CompletionContextEngine.Analyze(new(pedidos, pedidos.Length, EditorDialects.Console, Scope), tokens).Target.Collection,
            Is.EqualTo("Pedidos"));
        Assert.That(CompletionContextEngine.Analyze(new(clientes, clientes.Length, EditorDialects.Console, Scope), tokens).Target.Collection,
            Is.EqualTo("Clientes"));
    }

    [Test]
    public void ClosingATabReleasesItsTokensAndTheNextDocumentStartsCold()
    {
        var cache = new CompletionContextCache();
        var source = new StringTextSnapshot("db.Clientes.find({ ");
        var first = new CountingSnapshot(source);
        var reopened = new CountingSnapshot(source);

        _ = cache.Analyze(new(first, first.Length, EditorDialects.Console, Scope));
        cache.ClearDocument(source.Version.DocumentId);
        _ = cache.Analyze(new(reopened, reopened.Length, EditorDialects.Console, Scope));

        Assert.That((first.Reads, reopened.Reads), Is.EqualTo((1, 1)), "Após fechar a aba, nada do documento anterior é reaproveitado.");
    }

    [Test]
    public void ASupersededSnapshotStillProducesItsOwnContextWithoutBorrowingAnotherVersion()
    {
        var tokens = new TokenCache();
        var older = new StringTextSnapshot("db.Pedidos.find({ ");
        var newer = older.Replace(3, 7, "Clientes");
        _ = CompletionContextEngine.Analyze(new(newer, newer.Length, EditorDialects.Console, Scope), tokens);

        var late = CompletionContextEngine.Analyze(new(older, older.Length, EditorDialects.Console, Scope), tokens);

        Assert.That(tokens.GetOrLex(older).LexKind, Is.EqualTo(TokenCacheLexKind.Superseded));
        AssertSameAnalysis(CompletionContextEngine.Analyze(new(older, older.Length, EditorDialects.Console, Scope)), late, "superseded");
        Assert.That(late.Target.Collection, Is.EqualTo("Pedidos"));
        // A versão superada não repovoa o cache: a versão corrente continua reaproveitada.
        Assert.That(tokens.GetOrLex(newer).LexKind, Is.EqualTo(TokenCacheLexKind.Reused));
    }

    [Test]
    public void CancellationIsObservedBeforeTheTokensArePublished()
    {
        var tokens = new TokenCache();
        var snapshot = new StringTextSnapshot(Document);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope), tokens, cancellation.Token));
        AssertSameAnalysis(CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope)),
            CompletionContextEngine.Analyze(new(snapshot, 10, EditorDialects.Console, Scope), tokens),
            "a tentativa cancelada não envenena a próxima análise");
    }

    [Test]
    public void TheTypingPathDoesNotBuildASyntaxTree()
    {
        var held = typeof(CompletionContextCache)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.That(held, Has.Member(typeof(TokenCache)));
        Assert.That(held, Has.No.Member(typeof(SyntaxTreeCache)),
            "Digitar não pode construir árvore: o contexto lê tokens, e ninguém no caminho ativo lê nós.");
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

    /// <summary>Conta materializações do documento: tokens reaproveitados não podem custar uma nova leitura do texto.</summary>
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
