using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class TraditionalCompletionIntegrationTests
{
    [Test]
    public async Task TabCapturesItsOwnSnapshotAndExposesCatalogSuggestions()
    {
        var context = new WorkspaceTestContext();
        var tab = new WorkspaceTabViewModel(context.Workspace)
        {
            Profile = ConnectionProfile.Create("Local", "mongodb://localhost"), Database = "shop", Text = "db."
        };
        tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));

        var result = await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(result.Items.Select(item => item.Label), Does.Contain("getCollection"));
        Assert.That(result.Items.All(item => item.Edit.ReplaceRange.Start >= 0), Is.True);
        Assert.That(result.IsIncomplete, Is.False);
    }

    [Test]
    public async Task DetachedTabHasNoTraditionalProviderAndDoesNotThrow()
    {
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { Text = "db." };
        Assert.That((await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None)).Items, Is.Empty);
    }

    [Test]
    public async Task WorkspaceResolvesTheCapturedProfileOnlyWhileQueryingMetadata()
    {
        using var context = new WorkspaceTestContext();
        var source = new RecordingCatalogSource();
        var catalog = new KnowledgeCatalog([source]);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, knowledgeCatalog: catalog);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        workspace.Profiles.Add(profile);
        var tab = new WorkspaceTabViewModel(context.Workspace) { TraditionalCompletion = workspace.TraditionalCompletion, Profile = profile, Database = "shop", Text = "db." };

        await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(source.LastQuery!.Connection, Is.SameAs(profile));
        // A lista tradicional da aba é uma invocação explícita (CompletionTrigger.Invoked), que pode agendar carga;
        // o acesso Peek continua sendo o de digitação, coberto por CompletionContextEngineTests.
        Assert.That(source.LastQuery.Access, Is.EqualTo(MetadataAccess.LoadIfNeeded));
    }

    [Test]
    public async Task ATriggerCharacterInvocationNeverSchedulesARemoteLoad()
    {
        using var context = new WorkspaceTestContext();
        var source = new RecordingCatalogSource();
        var catalog = new KnowledgeCatalog([source]);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, knowledgeCatalog: catalog);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        workspace.Profiles.Add(profile);
        var tab = new WorkspaceTabViewModel(context.Workspace) { TraditionalCompletion = workspace.TraditionalCompletion, Profile = profile, Database = "shop", Text = "db." };

        await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None, CompletionTrigger.TriggerCharacter);

        // Only Invoked (explicit Ctrl+./Ctrl+Espaço/button) may schedule a load; a character that opens the list by
        // itself never queries MongoDB while the user is still typing.
        Assert.That(source.LastQuery!.Access, Is.EqualTo(MetadataAccess.Peek));
    }

    [Test]
    public async Task PipelineInputSchemaReturnsTheSameInstanceWhileTheUnderlyingSchemaIsUnchanged()
    {
        using var context = new WorkspaceTestContext();
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("pedidos", CollectionKind.Collection, "{\"bsonType\":\"object\",\"properties\":{\"total\":{\"bsonType\":\"double\"}}}")]
        };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        cache.Connect(profile);
        var identity = ConnectionIdentity.From(profile);
        await cache.RefreshAsync(new MetadataKey(identity, MetadataScope.Definition, "loja", "pedidos"));
        await cache.SampleSchemaAsync(profile, "loja", "pedidos");

        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, metadata: cache);
        workspace.Profiles.Add(profile);
        workspace.NewTabCommand.Execute(null);
        var tab = workspace.ActiveTab!;
        tab.Profile = profile; tab.Database = "loja"; tab.Collection = "pedidos";
        var scope = new CatalogScope(identity, "loja", "pedidos");

        Assert.That(tab.PipelineInputSchema, Is.Not.Null, "A raiz de composição sempre liga PipelineInputSchema a cada aba registrada.");
        var first = tab.PipelineInputSchema!(scope);
        var second = tab.PipelineInputSchema!(scope);

        Assert.That(first, Is.Not.Null);
        // CompletionContextCache compara CollectionSchema por referência; uma instância nova a cada chamada
        // derrubaria o cache de contexto a cada tecla digitada.
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public async Task ConcurrentInvocationsOfTheSameTabDiscardTheStaleResponseEvenWhenTheProviderIgnoresCancellation()
    {
        using var context = new WorkspaceTestContext();
        var provider = new SequencedTraditionalProvider();
        var tab = new WorkspaceTabViewModel(context.Workspace) { Text = "db.", TraditionalCompletion = provider };

        var first = tab.GetTraditionalCompletionsAsync("db.", 3, CancellationToken.None);
        await provider.Entered.WaitAsync(TimeSpan.FromSeconds(3));
        var second = await tab.GetTraditionalCompletionsAsync("db.f", 4, CancellationToken.None);
        provider.Release();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await first,
            "A primeira resposta chega depois de ser substituída pela segunda invocação da mesma aba; deve ser descartada.");
        Assert.That(second.Items.Select(item => item.Label), Does.Contain("item2"));
    }

    [Test]
    public async Task CancellingOneTabsTraditionalCompletionNeverTouchesAnotherTabsCancellationTokenSource()
    {
        using var context = new WorkspaceTestContext();
        var providerA = new SequencedTraditionalProvider();
        var tabA = new WorkspaceTabViewModel(context.Workspace) { Text = "db.", TraditionalCompletion = providerA };
        var tabB = new WorkspaceTabViewModel(context.Workspace)
        {
            Text = "db.", TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])))
        };

        var pendingA = tabA.GetTraditionalCompletionsAsync("db.", 3, CancellationToken.None);
        await providerA.Entered.WaitAsync(TimeSpan.FromSeconds(3));

        tabA.CancelTraditionalCompletion();
        var resultB = await tabB.GetTraditionalCompletionsAsync("db.", 3, CancellationToken.None);

        Assert.That(resultB.Items.Select(item => item.Label), Does.Contain("getCollection"),
            "Cancelar a solicitação da aba A não pode atingir o CancellationTokenSource da aba B.");
        providerA.Release();
        try { await pendingA; } catch (OperationCanceledException) { }
    }

    private sealed class SequencedTraditionalProvider : ICompletionProvider
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();

        public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1) { _entered.TrySetResult(); await _release.Task; }
            var item = new CompletionItem($"item{call}", $"item{call}", "teste", CompletionItemKind.Text,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, $"item{call}"), $"item{call}", 0, CompletionSource.Catalog);
            return new CompletionResponse(request, new(request.Context.Version, [item], false));
        }
    }

    [Test]
    public async Task TabKeepsSuggestionsWhenTheCatalogIsStillIncomplete()
    {
        using var context = new WorkspaceTestContext();
        var source = new RecordingCatalogSource { Completeness = CatalogCompleteness.Loading };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Text = "db." };
        tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource(), source])));

        var result = await tab.GetTraditionalCompletionsAsync(tab.Text, tab.Text.Length, CancellationToken.None);

        Assert.That(result.Items, Is.Not.Empty);
        Assert.That(result.IsIncomplete, Is.True);
    }

    [Test]
    public void MetadataChangeRequestsARefreshOnlyForTheMatchingTabScope()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "shop", Collection = "orders" };
        var refreshes = 0;
        tab.TraditionalCompletionRefreshRequested += (_, _) => refreshes++;
        var identity = ConnectionIdentity.From(profile);

        tab.NotifyMetadataChanged(new(profile.Id, new MetadataKey(identity, MetadataScope.Definition, "shop", "orders")));
        tab.NotifyMetadataChanged(new(profile.Id, new MetadataKey(identity, MetadataScope.Definition, "other", "orders")));
        tab.NotifyMetadataChanged(new(Guid.NewGuid(), new MetadataKey(identity, MetadataScope.Definition, "shop", "orders")));

        Assert.That(refreshes, Is.EqualTo(1));
    }

    private sealed class RecordingCatalogSource : ICatalogSource
    {
        public SymbolKinds ProvidedKinds => SymbolKinds.Collection | SymbolKinds.DatabaseMethod;
        public CatalogCompleteness Completeness { get; init; } = CatalogCompleteness.Complete;
        public CatalogQuery? LastQuery { get; private set; }
        public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Completeness;
        }
    }
}
