using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.GoldenCapture;

[TestFixture]
[Category("Integration")]
public sealed class HighlightingGoldenCaptureTests
{
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

}
