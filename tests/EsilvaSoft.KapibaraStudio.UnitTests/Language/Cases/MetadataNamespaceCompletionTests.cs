using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Cases;

[TestFixture]
public sealed class MetadataNamespaceCompletionTests
{
    [TestCase("ConnectionPool.", "relatorios", SymbolKinds.Connection, "relatorios", "ConnectionPool.relatorios")]
    [TestCase("getConnection(\"relatorios\").", "Vendas", SymbolKinds.Database, "Vendas", "getConnection(\"relatorios\").Vendas")]
    [TestCase("db.getCollection(\"\")", "pedidos-2025", SymbolKinds.Collection, "pedidos-2025", "db.getCollection(\"pedidos-2025\")")]
    public async Task CompletesDeterministicMetadataNamespacesAndAppliesTheirEdits(
        string source, string expectedLabel, SymbolKinds expectedKind, string expectedEdit, string expectedText)
    {
        var caret = source.EndsWith("\")", StringComparison.Ordinal) ? source.Length - 2 : source.Length;
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(
            new StringTextSnapshot(source), caret, EditorDialects.Console, TabScope));
        var catalog = new DeterministicNamespaceCatalog();
        var completion = await new CompletionService(catalog).CompleteAsync(analysis.Context);

        Assert.That(analysis.Context.ExpectedKinds & expectedKind, Is.EqualTo(expectedKind));
        Assert.That(catalog.LastQuery!.Kinds & expectedKind, Is.EqualTo(expectedKind));

        var item = completion.Items.Single(candidate => candidate.Label == expectedLabel);
        Assert.That(item.CatalogKind, Is.EqualTo(ToKind(expectedKind)));
        Assert.That(item.Edit.NewText, Is.EqualTo(expectedEdit));
        Assert.That(Apply(source, item.Edit), Is.EqualTo(expectedText));
    }

    private static readonly CatalogScope TabScope = new(
        new ConnectionIdentity(Guid.Parse("4B3C50BE-0CF8-4D1D-8527-1C2FA17B13E4"), "servidor-alfa", "fixture"),
        "Projetos", "Clientes");

    private static SymbolKind ToKind(SymbolKinds kind) => kind switch
    {
        SymbolKinds.Connection => SymbolKind.Connection,
        SymbolKinds.Database => SymbolKind.Database,
        SymbolKinds.Collection => SymbolKind.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Apply(string source, CompletionEdit edit) => source[..edit.ReplaceRange.Start]
        + edit.NewText
        + source[(edit.ReplaceRange.Start + edit.ReplaceRange.Length)..];

    private sealed class DeterministicNamespaceCatalog : IKnowledgeCatalog
    {
        private static readonly CatalogSymbol[] Symbols =
        [
            new("fixture/connection/relatorios", SymbolKind.Connection, "relatorios", "Perfil de teste"),
            new("fixture/database/Vendas", SymbolKind.Database, "Vendas", "Banco de teste"),
            new("fixture/collection/pedidos-2025", SymbolKind.Collection, "pedidos-2025", "Coleção de teste")
        ];

        public CatalogQuery? LastQuery { get; private set; }

        public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            var candidates = Symbols
                .Where(symbol => query.Kinds.HasFlag(ToKinds(symbol.Kind)))
                .Where(symbol => symbol.Name.StartsWith(query.Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(symbol => new CatalogCandidate(symbol, CatalogMatch.Prefix))
                .ToArray();
            return new CatalogResult(candidates, CatalogCompleteness.Complete);
        }

        private static SymbolKinds ToKinds(SymbolKind kind) => kind switch
        {
            SymbolKind.Connection => SymbolKinds.Connection,
            SymbolKind.Database => SymbolKinds.Database,
            SymbolKind.Collection => SymbolKinds.Collection,
            _ => SymbolKinds.None
        };
    }
}
