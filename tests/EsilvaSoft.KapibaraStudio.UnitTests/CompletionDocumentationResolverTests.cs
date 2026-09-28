using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CompletionDocumentationResolverTests
{
    [Test]
    public void ResolvesStructuredValueFreeDetailsOnlyWhenAsked()
    {
        var item = new CompletionItem("field/customer.name", "nome", "string · obrigatório", CompletionItemKind.Field,
            new(new TextSpan(0, 0), new TextSpan(0, 0), "nome"), "nome", 0, CompletionSource.Schema)
        {
            Tags = CompletionItemTags.Stale,
            Documentation = new("Campo", "String", ["nome"], "String", "7.0", EvidenceSources.Validator | EvidenceSources.Index)
        };

        var documentation = CompletionDocumentationResolver.Resolve(item);

        Assert.Multiple(() =>
        {
            Assert.That(documentation.Title, Is.EqualTo("nome"));
            Assert.That(documentation.Text, Does.Contain("Categoria: Campo").And.Contain("Forma esperada: String")
                .And.Contain("Parâmetros: nome").And.Contain("Evidência: validador, índice").And.Contain("Metadados em atualização"));
            Assert.That(documentation.Text, Does.Not.Contain("valor confidencial"));
        });
    }

    [Test]
    public void CancellationStopsResolutionBeforePresentation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var item = new CompletionItem("test", "teste", null, CompletionItemKind.Text,
            new(new TextSpan(0, 0), new TextSpan(0, 0), "teste"), "teste", 0, CompletionSource.Catalog);
        Assert.Throws<OperationCanceledException>(() => CompletionDocumentationResolver.Resolve(item, cancellation.Token));
    }
}
