using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

/// <summary>
/// Sugestão automática determinística (fase 5.1): funciona sem modelo de IA, sem conexão MongoDB e sem I/O, e prefere
/// abster-se a propor um candidato falsamente único.
/// </summary>
[TestFixture]
public sealed class TraditionalPreemptiveCompletionTests
{
    private static readonly string[] ExpectedName = ["Name"];
    private static readonly string[] ExpectedConnectionPool = ["ConnectionPool"];

    private static CompletionContext Context(string prefix, int caret = 10) =>
        new(new(1, 1), EditorDialects.Console, SymbolKinds.Field, prefix, new(caret - prefix.Length, prefix.Length));

    private static async Task<CompletionList> CompleteAsync(IKnowledgeCatalog catalog, string prefix)
    {
        var provider = new TraditionalPreemptiveCompletionProvider(new CompletionService(catalog));
        var response = await provider.CompleteAsync(new(Context(prefix), 1, 0));
        return response.List;
    }

    [Test]
    public async Task UniqueStrictContinuationBecomesTheOnlySuggestion()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete,
            new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"),
            new CatalogSymbol("field/other", SymbolKind.Field, "Total", "int"));

        var list = await CompleteAsync(catalog, "Na");

        Assert.That(list.Items.Select(item => item.Label), Is.EqualTo(ExpectedName));
        Assert.That(list.IsIncomplete, Is.False);
        // Peek sempre: digitar jamais agenda carga de metadados, nem com o contexto pedindo outra coisa.
        Assert.That(catalog.LastAccess, Is.EqualTo(MetadataAccess.Peek));
    }

    [Test]
    public async Task ASaturatedCandidateBudgetAbstainsEvenWhenTheSourceReportsComplete()
    {
        // Escopo real que satura o teto de 200 candidatos sem que nenhuma fonte reporte truncamento: campos de um
        // schema local grande, onde MetadataCatalogSource devolve Complete porque tudo veio da memória. A maioria casa
        // "Us" só como substring ("status…"), e uma única continuação estrita sobra no topo — a forma exata que
        // convenceria o portão de confiança de uma unicidade que o catálogo nunca chegou a verificar.
        var builder = new SchemaBuilder(maximumNodes: 20_000);
        var names = Enumerable.Range(0, 5_000).Select(index => $"statusCampo{index:D5}").Append("Usuario").ToArray();
        builder.AddDocuments(["{" + string.Join(",", names.Select(name => $"\"{name}\":1")) + "}"]);
        var schema = builder.Build();
        var catalog = new KnowledgeCatalog([new MetadataCatalogSource(new MetadataCache(UnavailableMetadataSource.Instance))]);
        var context = new CompletionContext(new(1, 1), EditorDialects.Console, SymbolKinds.Field, "Us", new(0, 2))
        { LocalSchemas = [schema], RestrictFieldsToLocalSchemas = true };

        // Pré-condições que tornam o cenário exatamente o perigoso: a fonte diz Complete, a cota encheu, e o topo tem
        // uma única continuação estrita. Sem o corte por cota em IsIncomplete, isso vira um ghost afirmando unicidade.
        var direct = catalog.Query(new(SymbolKinds.Field, EditorDialects.Console, "Us")
        { MaximumCandidates = 200, LocalSchemas = [schema], RestrictFieldsToLocalSchemas = true });
        Assert.That(direct.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(direct.Candidates, Has.Count.EqualTo(200));

        var raw = await new CompletionService(catalog).CompleteAsync(context with { MaximumCandidates = 200, MaximumItems = 8 });
        Assert.That(raw.Items[0].Label, Is.EqualTo("Usuario"));
        Assert.That(raw.Items.Count(item => item.FilterText.StartsWith("Us", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(raw.IsIncomplete, Is.True, "Cota de candidatos esgotada é truncamento, mesmo com a fonte dizendo Complete.");

        var provider = new TraditionalPreemptiveCompletionProvider(new CompletionService(catalog));
        var response = await provider.CompleteAsync(new(context, 1, 0));

        Assert.That(response.List.Items, Is.Empty, "Sem ter visto o conjunto inteiro, o automático não afirma unicidade.");
        Assert.That(provider.LastAbstentionReason, Is.EqualTo("incomplete-catalog"));
    }

    [Test]
    public async Task AmbiguousCandidatesAbstainInsteadOfGuessing()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete,
            new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"),
            new CatalogSymbol("field/names", SymbolKind.Field, "Names", "array"));

        var list = await CompleteAsync(catalog, "Na");

        Assert.That(list.Items, Is.Empty, "Duas continuações possíveis não produzem sugestão automática.");
    }

    [TestCase(CatalogCompleteness.Partial)]
    [TestCase(CatalogCompleteness.Loading)]
    [TestCase(CatalogCompleteness.Unavailable)]
    public async Task TruncatedCatalogAbstainsEvenWithASingleCandidate(CatalogCompleteness completeness)
    {
        var catalog = new StaticCatalog(completeness, new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"));

        var list = await CompleteAsync(catalog, "Na");

        Assert.That(list.Items, Is.Empty, "Sem catálogo completo, candidato único não prova unicidade.");
    }

    [Test]
    public async Task EmptyPrefixAbstainsBecauseThereIsNoStrictContinuation()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete, new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"));

        var list = await CompleteAsync(catalog, "");

        Assert.That(list.Items, Is.Empty);
    }

    [Test]
    public async Task CaseDifferenceIsNotAContinuation()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete, new CatalogSymbol("field/name", SymbolKind.Field, "NAME", "string"));

        var list = await CompleteAsync(catalog, "Na");

        Assert.That(list.Items, Is.Empty, "O prefixo é estrito: caixa diferente exigiria reescrever o que foi digitado.");
    }

    [Test]
    public async Task SnippetWithPlaceholdersBelongsToTheExplicitList()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete,
            new CatalogSymbol("snippet/find", SymbolKind.Snippet, "findOne", "modelo") { Snippet = "findOne({ ${1:campo}: ${2:valor} })" });

        var list = await CompleteAsync(catalog, "find");

        Assert.That(list.Items, Is.Empty);
    }

    [Test]
    public async Task AlreadyCompleteWordAbstains()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete, new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"));

        var list = await CompleteAsync(catalog, "Name");

        Assert.That(list.Items, Is.Empty, "Não há o que inserir quando o símbolo já foi digitado inteiro.");
    }

    [Test]
    public async Task WorksWithoutAnyAiModelOrMongoConnectionAndNeverAsksForLoad()
    {
        // Catálogo real de linguagem, sem fonte de metadados: é o cenário de instalação nova, sem conexão aberta e
        // sem modelo instalado. A sugestão precisa sair mesmo assim.
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource()]);
        var provider = new TraditionalPreemptiveCompletionProvider(new CompletionService(catalog));
        var context = new CompletionContext(new(1, 1), EditorDialects.Console, SymbolKinds.DslRoot, "Conn", new(0, 4))
        {
            CatalogAccess = MetadataAccess.LoadIfNeeded
        };

        var response = await provider.CompleteAsync(new(context, 1, 0));

        Assert.That(response.List.Items.Select(item => item.Label), Is.EqualTo(ExpectedConnectionPool));
        Assert.That(response.IsFor(new(context, 1, 0)), Is.True);
    }

    [Test]
    public async Task CancellationIsObservedBeforeRanking()
    {
        var catalog = new StaticCatalog(CatalogCompleteness.Complete, new CatalogSymbol("field/name", SymbolKind.Field, "Name", "string"));
        var provider = new TraditionalPreemptiveCompletionProvider(new CompletionService(catalog));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.That(async () => await provider.CompleteAsync(new(Context("Na"), 1, 0), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(catalog.Queries, Is.Zero);
    }

}
