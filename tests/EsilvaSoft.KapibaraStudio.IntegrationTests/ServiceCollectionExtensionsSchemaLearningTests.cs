using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Composition tests of the real container (the same two calls <c>App.axaml.cs</c> makes), covering the L15
/// precondition of DEC-L15-DEDUP: the learned source must be collected after the live-evidence source, and the
/// two opt-out flags must actually reach <see cref="ILearnedSchemaOptOut"/>.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ServiceCollectionExtensionsSchemaLearningTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "kapibarastudio-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* Um arquivo ainda preso não deve falhar o teste que já terminou. */ }
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(_root, "workspace.db"));
        services.AddKapibaraStudioLocalAiInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CatalogQuery FieldQuery(ConnectionProfile profile) =>
        new(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "Loja", Collection = "Pedidos" };

    [Test]
    public async Task LearnedSourceIsRegisteredAfterTheLiveMetadataSource()
    {
        await using var provider = BuildProvider();

        var sources = provider.GetServices<ICatalogSource>().ToArray();
        var metadata = Array.FindIndex(sources, source => source is MetadataCatalogSource);
        var learned = Array.FindIndex(sources, source => source is LearnedSchemaCatalogSource);

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.GreaterThanOrEqualTo(0), "A fonte de evidência viva continua registrada.");
            Assert.That(learned, Is.GreaterThan(metadata),
                "DEC-L15-DEDUP: a fonte aprendida precisa varrer um sink onde o símbolo vivo já está.");
            Assert.That(sources.Count(source => source is LearnedSchemaCatalogSource), Is.EqualTo(1),
                "Uma única fonte aprendida; duas dobrariam a cota por fonte.");
        });
    }

    [Test]
    public async Task KnowledgeCatalogSeesTheLearnedSourceInTheSameOrder()
    {
        await using var provider = BuildProvider();

        var catalog = provider.GetRequiredService<IKnowledgeCatalog>();
        var sources = provider.GetServices<ICatalogSource>().ToArray();

        Assert.That(catalog, Is.Not.Null);
        Assert.That(sources.Last(), Is.InstanceOf<LearnedSchemaCatalogSource>(),
            "A composição do catálogo termina na fonte aprendida; qualquer fonte nova depois dela precisa rever DEC-L15-DEDUP.");
    }

    [Test]
    public async Task DisabledGeneralFlagStopsServingBeforeAnyRepositoryRead()
    {
        await using var provider = BuildProvider();
        var autocomplete = provider.GetRequiredService<IAutocompleteService>();
        await autocomplete.ConfigureAsync(new AutocompleteSettings { LearnedSchemaEnabled = false });
        var source = provider.GetServices<ICatalogSource>().OfType<LearnedSchemaCatalogSource>().Single();
        var profile = ConnectionProfile.Create("aprendida", "mongodb://host-aprendido");

        var sink = new List<CatalogCandidate>();
        var completeness = source.Collect(FieldQuery(profile), sink, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<ILearnedSchemaOptOut>().IsServingAllowed(profile.Id), Is.False);
            Assert.That(sink, Is.Empty, "Desligado não serve nenhum campo aprendido.");
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete),
                "Desligado responde completo na hora: nem hidratação (Loading) nem leitura do repositório.");
        });
    }

    [Test]
    public async Task EnabledFlagStartsHydrationForTheSameQuery()
    {
        await using var provider = BuildProvider();
        var autocomplete = provider.GetRequiredService<IAutocompleteService>();
        await autocomplete.ConfigureAsync(new AutocompleteSettings());
        var source = provider.GetServices<ICatalogSource>().OfType<LearnedSchemaCatalogSource>().Single();
        var profile = ConnectionProfile.Create("aprendida", "mongodb://host-aprendido");

        var sink = new List<CatalogCandidate>();
        var completeness = source.Collect(FieldQuery(profile), sink, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<ILearnedSchemaOptOut>().IsServingAllowed(profile.Id), Is.True,
                "Ligado por padrão: LearnedSchemaEnabled é true em AutocompleteSettings.");
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Loading),
                "Ligado hidrata em segundo plano — a diferença observável em relação ao opt-out desligado.");
        });
    }

    [Test]
    public async Task PerConnectionExclusionComesFromWorkspacePreferences()
    {
        await using var provider = BuildProvider();
        await provider.GetRequiredService<IAutocompleteService>().ConfigureAsync(new AutocompleteSettings());
        var optOut = provider.GetRequiredService<LearnedSchemaOptOut>();
        var excluded = Guid.NewGuid();
        var kept = Guid.NewGuid();

        optOut.ApplyPreferences(new WorkspacePreferences { LearnedSchemaExcludedProfileIds = [excluded] });

        Assert.Multiple(() =>
        {
            Assert.That(optOut.IsServingAllowed(excluded), Is.False);
            Assert.That(optOut.IsServingAllowed(kept), Is.True, "A exclusão é por conexão, nunca global.");
            Assert.That(ReferenceEquals(optOut, provider.GetRequiredService<ILearnedSchemaOptOut>()), Is.True,
                "A fonte aprendida e quem aplica as preferências precisam ver o mesmo estado.");
        });

        optOut.SetServingExcluded(excluded, excluded: false);
        Assert.That(optOut.IsServingAllowed(excluded), Is.True, "Restaurar uma conexão volta a servir o que já foi aprendido.");
    }

    [Test]
    public async Task LearnedSchemaRepositoryAndOwnerAreTheSameLiteDbInstance()
    {
        await using var provider = BuildProvider();

        Assert.That(provider.GetRequiredService<ILearnedSchemaRepository>(),
            Is.SameAs(provider.GetRequiredService<LiteDbConnectionProfileRepository>()),
            "Nunca uma segunda LiteDatabase para o mesmo arquivo.");
    }

    [Test]
    public async Task ProducerSideResolvesAsLongLivedSingletons()
    {
        await using var provider = BuildProvider();

        var host = provider.GetRequiredService<SchemaLearningHost>();
        var coordinator = host.Coordinator;
        var service = provider.GetRequiredService<SchemaLearningService>();

        Assert.Multiple(() =>
        {
            Assert.That(coordinator, Is.SameAs(provider.GetRequiredService<SchemaLearningHost>().Coordinator),
                "Um único coordenador: a fila e o worker vivem pelo tempo da aplicação.");
            Assert.That(service, Is.SameAs(host.Service), "O serviço registrado é o do host, não um segundo por cima de outra fila.");
            Assert.That(service, Is.SameAs(provider.GetRequiredService<SchemaLearningService>()));
            Assert.That(provider.GetRequiredService<BackgroundSchemaAnalyzer>(),
                Is.SameAs(provider.GetRequiredService<BackgroundSchemaAnalyzer>()));
        });
    }

    [Test]
    public void SynchronousContainerDisposeStillWorksAfterResolvingSchemaLearning()
    {
        var provider = BuildProvider();
        _ = provider.GetRequiredService<SchemaLearningService>();

        // App.axaml.cs descarta o provider de forma síncrona no evento Exit; um singleton só-IAsyncDisposable
        // faria esse descarte lançar. Este teste guarda esse caminho real de encerramento.
        Assert.DoesNotThrow(provider.Dispose);
    }
}
