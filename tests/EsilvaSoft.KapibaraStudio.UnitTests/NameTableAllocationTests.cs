using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Guarda de alocação da busca de nomes. O orçamento de docs/auto-complite/performance.md é de 64 KB por tecla no
/// caminho sem IA, e a busca no catálogo é paga a cada tecla: uma regressão aqui não aparece como erro, só como
/// digitação engasgada, então é medida em teste e não apenas em benchmark.
/// </summary>
[TestFixture]
public sealed class NameTableAllocationTests
{
    private static NameTable<string> Build(int count)
    {
        var names = new string[count];
        for (var index = 0; index < count; index++) names[index] = $"campoCliente{index:D5}Nome";
        return new NameTable<string>(names, name => name);
    }

    private static long Measure(NameTable<string> table, string query, int repetitions)
    {
        var sink = new List<string>(128);
        // Aquecimento: a primeira consulta paga JIT e não representa o custo por tecla.
        for (var index = 0; index < 8; index++) { sink.Clear(); table.Collect(query, 100, null, (item, _) => sink.Add(item)); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < repetitions; index++) { sink.Clear(); table.Collect(query, 100, null, (item, _) => sink.Add(item)); }
        return (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
    }

    [Test]
    public void CollectStaysWithinThePerKeystrokeBudget()
    {
        var table = Build(10_000);
        var prefix = Measure(table, "campoCliente0001", 200);
        var humps = Measure(table, "cCN", 200);
        var substring = Measure(table, "zzznadacasa", 200);

        TestContext.Out.WriteLine($"prefixo={prefix} B, humps={humps} B, substring={substring} B");
        Assert.Multiple(() =>
        {
            Assert.That(prefix, Is.LessThan(64 * 1024), "consulta por prefixo");
            Assert.That(humps, Is.LessThan(64 * 1024), "consulta por camel humps");
            Assert.That(substring, Is.LessThan(64 * 1024), "consulta sem casamento, que varre o escopo inteiro");
        });
    }
    [Test]
    public void ACatalogQueryOverTheEmbeddedLanguageStaysWithinTheBudget()
    {
        // Onde a alocação por tecla realmente mora: a consulta materializa um CatalogCandidate por candidato,
        // e é isso, não a busca de nomes, que escala com MaximumCandidates.
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource()]);
        var query = new CatalogQuery(SymbolKinds.Operators | SymbolKinds.AggregationStage, EditorDialects.Console, "s")
        { MaximumCandidates = 200 };
        for (var index = 0; index < 8; index++) catalog.Query(query);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int repetitions = 100;
        for (var index = 0; index < repetitions; index++) catalog.Query(query);
        var perQuery = (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;

        TestContext.Out.WriteLine($"consulta de catalogo={perQuery} B");
        Assert.That(perQuery, Is.LessThan(64 * 1024));
    }
    [Test, Explicit("Mede uma violação conhecida de orçamento; ver pendência em docs/auto-complite/performance.md.")]
    public void FieldQueryOverALargeCollectionExceedsTheBudgetToday()
    {
        // Cenário que o catálogo de linguagem embarcado não cobre: campos vindos da fonte de metadados, onde
        // MetadataCatalogSource.Describe monta texto de apresentação por candidato, inclusive para os descartados.
        var builder = new SchemaBuilder(maximumNodes: 20_000);
        var names = new string[10_000];
        for (var index = 0; index < names.Length; index++) names[index] = $"campoCliente{index:D5}Nome";
        // Um documento com 10 000 campos: mesma escala do cenário de 10 000 campos do benchmark de catálogo.
        builder.AddDocuments([ "{" + string.Join(",", names.Select(name => $"\"{name}\":1")) + "}" ]);
        var schema = builder.Build();
        var query = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console, "campoCliente0")
        { MaximumCandidates = 200, LocalSchemas = [schema], RestrictFieldsToLocalSchemas = true };
        var catalog = new KnowledgeCatalog([new MetadataCatalogSource(new MetadataCache(UnavailableMetadataSource.Instance))]);
        for (var index = 0; index < 4; index++) catalog.Query(query);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int repetitions = 50;
        for (var index = 0; index < repetitions; index++) catalog.Query(query);
        var perQuery = (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
        TestContext.Out.WriteLine($"campos por metadados={perQuery} B ({perQuery / 1024.0:0.0} KB)");
        Assert.That(perQuery, Is.LessThan(64 * 1024));
    }
}
