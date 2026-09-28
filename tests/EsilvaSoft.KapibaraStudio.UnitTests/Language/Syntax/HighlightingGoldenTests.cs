using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

/// <summary>
/// Non-regression gate of the MongoLexer extraction: the golden was captured from the previous highlighter and is
/// compared exactly. Never regenerate it to hide a difference; a legitimate change needs explicit justification.
/// </summary>
[TestFixture]
public sealed class HighlightingGoldenTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Golden = new(() => Parse(File.ReadAllText(GoldenPath(), Encoding.UTF8)));

    public static IEnumerable<string> CaseIds() => HighlightingGoldenCorpus.Cases.Select(c => c.Id);

    [TestCaseSource(nameof(CaseIds))]
    public void HighlightingMatchesTheTokensCapturedBeforeTheLexerExtraction(string id)
    {
        var @case = HighlightingGoldenCorpus.Cases.Single(c => c.Id == id);
        Assert.That(Golden.Value.TryGetValue(id, out var expected), Is.True, "Caso ausente no golden: " + id);
        Assert.That(HighlightingGoldenCorpus.Render(new SyntaxHighlightingService(), @case), Is.EqualTo(expected));
    }

    [Test]
    public void GoldenCoversExactlyTheCorpus() => Assert.That(Golden.Value.Keys, Is.EquivalentTo(CaseIds()));

    [Test, Explicit("Captura o golden; usado uma vez antes da extração do MongoLexer. Requer SLOP_HIGHLIGHTING_GOLDEN_OUT."), Category("GoldenCapture")]
    public void CaptureGolden()
    {
        var path = Environment.GetEnvironmentVariable("SLOP_HIGHLIGHTING_GOLDEN_OUT");
        if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Defina SLOP_HIGHLIGHTING_GOLDEN_OUT.");
        var service = new SyntaxHighlightingService();
        var builder = new StringBuilder("# Golden de highlighting capturado antes da extração do MongoLexer (b082d4a). Não regenerar para esconder regressão.\n");
        foreach (var @case in HighlightingGoldenCorpus.Cases) builder.Append(HighlightingGoldenCorpus.Render(service, @case));
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static Dictionary<string, string> Parse(string text)
    {
        var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
        string? id = null; var current = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                if (id is not null) blocks.Add(id, current.ToString());
                id = line[3..]; current.Clear().Append(line).Append('\n');
            }
            else if (id is not null && line.Length > 0) current.Append(line).Append('\n');
        }
        if (id is not null) blocks.Add(id, current.ToString());
        return blocks;
    }

    private static string GoldenPath()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return Path.Combine(root!.FullName, "tests", "EsilvaSoft.KapibaraStudio.UnitTests", "Language", "Syntax", "Golden", "syntax-highlighting.v1.golden");
    }
}
