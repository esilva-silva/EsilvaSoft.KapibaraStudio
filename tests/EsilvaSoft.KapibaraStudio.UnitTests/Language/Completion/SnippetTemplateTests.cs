using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class SnippetTemplateTests
{
    private static readonly int[] ExpectedIndices = [1, 2, 3, 0];
    private static readonly string[] ExpectedChoices = ["one", "two"];
    private static readonly int[] ExpectedNestedIndices = [2, 1, 1, 1];
    [Test]
    public void ExpandExpandsTabStopsDefaultsChoicesFinalStopAndLiteralDollar()
    {
        var expansion = SnippetTemplate.Parse("{ ${1:field}: ${2|one,two|}, \\$literal: $3 }$0").Expand();

        Assert.Multiple(() =>
        {
            Assert.That(expansion.Text, Is.EqualTo("{ field: one, $literal:  }"));
            Assert.That(expansion.Placeholders.Select(x => x.Index), Is.EqualTo(ExpectedIndices));
            Assert.That(expansion.Placeholders[0].Span, Is.EqualTo(new TextSpan(2, 5)));
            Assert.That(expansion.Placeholders[1].Span, Is.EqualTo(new TextSpan(9, 3)));
            Assert.That(expansion.Placeholders[1].Choices, Is.EqualTo(ExpectedChoices));
            Assert.That(expansion.Placeholders[2].Span.Length, Is.Zero);
            Assert.That(expansion.Placeholders[3].Span, Is.EqualTo(new TextSpan(expansion.Text.Length, 0)));
        });
    }

    [Test]
    public void ExpandSupportsNestedPlaceholderDefaultsAndRepeatedTabStops()
    {
        var expansion = SnippetTemplate.Parse("${1:outer ${2:inner}}-$1-${1|x,y|}").Expand();

        Assert.Multiple(() =>
        {
            Assert.That(expansion.Text, Is.EqualTo("outer inner--x"));
            Assert.That(expansion.Placeholders.Select(x => x.Index), Is.EqualTo(ExpectedNestedIndices));
            Assert.That(expansion.Placeholders[0].Span, Is.EqualTo(new TextSpan(6, 5)));
            Assert.That(expansion.Placeholders[1].Span, Is.EqualTo(new TextSpan(0, 11)));
        });
    }

    [TestCase("$")]
    [TestCase("${:value}")]
    [TestCase("${1:value")]
    [TestCase("${1|a,b}")]
    [TestCase("${1|a,,b|}")]
    [TestCase("${01:value}")]
    [TestCase("\\x")]
    public void TryParseRejectsMalformedLspSyntax(string source)
    {
        var parsed = SnippetTemplate.TryParse(source, out var template, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(template, Is.Null);
            Assert.That(error, Does.StartWith("Snippet inválido"));
        });
    }

    [Test]
    public void TryParseAcceptsAllSupportedForms()
    {
        var parsed = SnippetTemplate.TryParse("$1 ${2:default} ${3|a\\,b,c\\|d|} $0 \\$", out var template, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(template!.Expand().Text, Is.EqualTo(" default a,b  $"));
        });
    }
}
