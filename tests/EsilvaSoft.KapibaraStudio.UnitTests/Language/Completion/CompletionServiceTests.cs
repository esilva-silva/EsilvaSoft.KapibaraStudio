using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class CompletionServiceTests
{
    private static readonly string[] ExpectedLabels = ["Name"];
    [Test]
    public async Task RequestsOnlyKindsCapturedByTheContextAndUsesPeekByDefault()
    {
        var source = new RecordingCatalog();
        var service = new CompletionService(source);
        var context = new CompletionContext(new(7, 3), EditorDialects.Mql, SymbolKinds.Field, "Na", new(10, 2));

        var result = await service.CompleteAsync(context);

        Assert.That(source.LastQuery!.Kinds, Is.EqualTo(SymbolKinds.Field));
        Assert.That(source.LastQuery.Access, Is.EqualTo(MetadataAccess.Peek));
        Assert.That(result.Items.Select(x => x.Label), Is.EqualTo(ExpectedLabels));
        Assert.That(result.Version, Is.EqualTo(context.Version));
    }

    private sealed class RecordingCatalog : IKnowledgeCatalog
    {
        public CatalogQuery? LastQuery { get; private set; }

        public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return new([new(new("field/name", SymbolKind.Field, "Name", "string"), CatalogMatch.Prefix)], CatalogCompleteness.Complete);
        }
    }
}
