using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class CompletionProviderTests
{
    [Test]
    public async Task TraditionalProviderPreservesRequestStamp()
    {
        var service = new CompletionService(new EmptyCatalog());
        var request = new CompletionRequest(new(new(3, 4), EditorDialects.Mql, SymbolKinds.Keyword, "", new(0, 0)), 1, 2);

        var response = await new TraditionalCompletionProvider(service).CompleteAsync(request);

        Assert.That(response.IsFor(request), Is.True);
        Assert.That(response.List.Version, Is.EqualTo(request.Context.Version));
    }

    [Test]
    public void RequestRejectsNonPositiveIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionRequest(
            new(new(1, 1), EditorDialects.Mql, SymbolKinds.Keyword, "", new(0, 0)), 0, 0));
    }

    private sealed class EmptyCatalog : IKnowledgeCatalog
    {
        public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default) => new([], CatalogCompleteness.Complete);
    }
}
