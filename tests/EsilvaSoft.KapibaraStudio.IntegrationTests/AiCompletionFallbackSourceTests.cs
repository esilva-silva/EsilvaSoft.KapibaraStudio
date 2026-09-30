using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class AiCompletionFallbackSourceTests
{
    /// <summary>
    /// Nenhuma decisão de fallback pode ser tomada por inspeção do <em>texto</em> de uma mensagem
    /// (DEC-R41-REASONS): motivo e comportamento saem do enum e do tipo da exceção. Guarda estrutural sobre o
    /// próprio código de produção, porque o defeito que ela previne é uma linha nova, não um tipo novo.
    /// </summary>
    [Test]
    public void NoFallbackDecisionIsTakenByInspectingMessageText()
    {
        string[] files =
        [
            @"src\EsilvaSoft.KapibaraStudio.Application\AiGenerationPipeline.cs",
            @"src\EsilvaSoft.KapibaraStudio.Application\AiCompletionProvider.cs",
            @"src\EsilvaSoft.KapibaraStudio.Application\CompletionOutputProcessor.cs",
            @"src\EsilvaSoft.KapibaraStudio.Desktop\AiCompletionFallbackMessages.cs",
            @"src\EsilvaSoft.KapibaraStudio.Desktop\WorkspaceTabView.Autocomplete.Ai.cs"
        ];
        // Comparar/procurar dentro de qualquer coisa chamada Message, Reason (texto do provider) ou Status.Message.
        var forbidden = new Regex(@"(Message|\.Reason)\s*(==|!=)\s*""|(Message|\.Reason)\.(Contains|StartsWith|EndsWith|IndexOf)\s*\(",
            RegexOptions.CultureInvariant);
        var root = RepositoryRoot();

        var offenders = files.SelectMany(relative =>
        {
            var path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Fonte varrido não encontrado: " + relative);
            return File.ReadAllLines(path).Select((line, index) => (relative, number: index + 1, line))
                .Where(entry => forbidden.IsMatch(entry.line));
        }).Select(entry => $"{entry.relative}:{entry.number}").ToArray();

        Assert.That(offenders, Is.Empty, "Decisão por texto de mensagem: " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return root!.FullName;
    }
}
