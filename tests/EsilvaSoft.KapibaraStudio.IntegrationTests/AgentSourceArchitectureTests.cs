namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Read-only repository source checks; source file access belongs to integration.</summary>
[TestFixture, Category("Integration"), Category("ArchitectureSource")]
public sealed class AgentSourceArchitectureTests
{
    [Test]
    public void DesktopProductionSourceOnlyMentionsProviderSdksInsideAppCompositionFiles()
    {
        // Closes the gap reflection cannot see: a method-body-only call (e.g. calling an extension method inside a
        // type whose own signatures never name the SDK). Comment-only lines are skipped so this stays a check of
        // real syntax, not of prose that explains the composition root's job (as AgentChatPorts.cs does).
        string[] forbiddenTokens =
        [
            "Infrastructure.Agents", "OpenAiAgentProvider", "ClaudeAgentProvider",
            "CopilotSubscriptionAgentProvider", "GitHub.Copilot",
            "AddKapibaraStudioOpenAiAgentProvider", "AddKapibaraStudioClaudeAgentProvider",
            "using OpenAI", "using Anthropic", "using ModelContextProtocol",
        ];
        string[] appCompositionFiles =
        [
            "App.axaml.cs", "App.AgentAccountOperations.cs", "App.ClaudeCodeAccountHandler.cs",
            "App.CopilotAccountHandler.cs", "App.CodexAccountHandler.cs",
        ];
        var offenders = DesktopProductionSourceFiles()
            .Where(file => !appCompositionFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => !entry.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(entry => forbiddenTokens.Where(token => entry.line.Contains(token, StringComparison.Ordinal))
                .Select(token => Path.GetFileName(entry.file) + ":" + entry.number + " (" + token + ")"))
            .ToArray();

        Assert.That(offenders, Is.Empty, "Código fora do composition root mencionando SDK de provider: " + string.Join(", ", offenders));
    }

    private static string[] DesktopProductionSourceFiles()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        var directory = Path.Combine(root!.FullName, "src", "EsilvaSoft.KapibaraStudio.Desktop");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(separator + "obj" + separator, StringComparison.Ordinal)
                && !file.Contains(separator + "bin" + separator, StringComparison.Ordinal))
            .ToArray();
        Assert.That(files, Is.Not.Empty, "Nenhum fonte de produção do Desktop encontrado; o teste varreria o vazio.");
        return files;
    }
}
