using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class KnowledgeCatalogTests
{
    private static KnowledgeCatalogSnapshot.Snapshot Export(string dialect = "all") =>
        KnowledgeCatalogSnapshot.Export([new LanguageCatalogSource(), new RefusedMetadataSource()], dialect,
            new("mongodb-language.v1.json", new string('a', 64), "test", false));

    [Test]
    public void ExportMatchesEveryEditorKindAndIsDeterministicWithoutMetadata()
    {
        var first = Export();
        var second = Export();
        Assert.That(KnowledgeCatalogSnapshot.Serialize(second), Is.EqualTo(KnowledgeCatalogSnapshot.Serialize(first)));
        Assert.That(first.Counts.Symbols, Is.EqualTo(LanguageDefinition.Default.Symbols.Count));
        foreach (var group in LanguageDefinition.Default.Symbols.GroupBy(symbol => symbol.Kind))
            Assert.That(first.Counts.ByKind[group.Key.ToString()], Is.EqualTo(group.Count()));
        var json = KnowledgeCatalogSnapshot.Serialize(first);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("symbols").EnumerateArray()
            .All(symbol => !symbol.TryGetProperty("scope", out _) && !symbol.TryGetProperty("field", out _)), Is.True);
        Assert.That(KnowledgeCatalogSnapshot.Parse(json).SnapshotSha256, Is.EqualTo(first.SnapshotSha256));
        Assert.That(() => KnowledgeCatalogSnapshot.Parse(json.Replace("test", "altered", StringComparison.Ordinal)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CheckReportsAddedRemovedAndChangedIds()
    {
        var expected = Export();
        var symbols = expected.Symbols.ToArray();
        symbols[0] = symbols[0] with { Detail = "Alterado" };
        var actual = expected with { Symbols = symbols.SkipLast(1).Append(symbols[^1] with { Id = "new" }).ToArray() };
        Assert.That(KnowledgeCatalogSnapshot.Compare(expected, expected), Is.Empty);
        Assert.That(KnowledgeCatalogSnapshot.Compare(expected, actual), Is.EquivalentTo(new[]
            { "changed:" + symbols[0].Id, "removed:" + symbols[^1].Id, "added:new" }));
    }

    [TestCase("mql")]
    [TestCase("console")]
    [TestCase("dsl")]
    public void DialectFiltersStaticSymbols(string dialect)
    {
        var snapshot = Export(dialect);
        Assert.That(snapshot.Symbols.Length, Is.GreaterThan(0).And.LessThan(Export().Symbols.Length));
        if (dialect == "dsl") Assert.That(snapshot.Symbols.All(symbol => symbol.Category is "dsl" or "connection-pool"), Is.True);
        if (dialect == "mql") Assert.That(snapshot.Symbols.All(symbol => symbol.Dialects.Contains("Mql")), Is.True);
    }

    [Test]
    public async Task CliWritesAtomicSnapshotAndReturnsZeroOrSeven()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapilab-knowledge-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await KnowledgeCatalogCommand.ExportAsync("all", "data/lab/a.json", root);
            await KnowledgeCatalogCommand.ExportAsync("mql", "data/lab/b.json", root);
            Assert.That(await KnowledgeCatalogCommand.CheckAsync("data/lab/a.json", "data/lab/a.json", root), Is.Zero);
            Assert.That(await KnowledgeCatalogCommand.CheckAsync("data/lab/a.json", "data/lab/b.json", root), Is.EqualTo(7));
            Assert.That(Directory.GetFiles(Path.Combine(root, "data/lab"), "*.tmp"), Is.Empty);
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => KnowledgeCatalogCommand.ExportAsync("all", "outside.json", root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class RefusedMetadataSource : ICatalogSource
    {
        public SymbolKinds ProvidedKinds => SymbolKinds.Field | SymbolKinds.QueryOperator;
        public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken) =>
            throw new AssertionException("Fonte de metadados não pode ser consultada pelo export.");
    }
}
