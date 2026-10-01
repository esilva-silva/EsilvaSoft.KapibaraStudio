using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotPublishedRuntimeTests
{
    private static string ReleaseDirectory() => Environment.GetEnvironmentVariable("KAPIBARA_RELEASE_TEST_DIRECTORY")
        ?? throw new InvalidOperationException("Defina KAPIBARA_RELEASE_TEST_DIRECTORY para o publish a validar.");

    [Test, Explicit("Smoke do publish real: exige KAPIBARA_RELEASE_TEST_DIRECTORY; sem login, rede de modelos ou inferência.")]
    public async Task PublishedHeadlessRuntimeStartsWithoutPathOrUserCredentials()
    {
        using var directory = new SyntheticDirectory();
        var options = CopilotRuntimeSettings.AccountClientOptions();
        options.Connection = RuntimeConnection.ForStdio(path: CopilotRuntimeSettings.BundledExecutablePath(ReleaseDirectory(), interactive: false));
        options.Mode = CopilotClientMode.Empty;
        options.UseLoggedInUser = false;
        options.BaseDirectory = directory.Path;
        options.WorkingDirectory = directory.Path;
        options.Environment = new Dictionary<string, string>(options.Environment!) { ["PATH"] = string.Empty };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = new CopilotClient(options);
        await client.StartAsync(timeout.Token);
        Assert.That((await client.GetAuthStatusAsync(timeout.Token)).IsAuthenticated, Is.False);
    }

    [Test, Explicit("Homologação Windows do publish com conta oficial já autenticada; sem inferência nem leitura de credenciais.")]
    public async Task PublishedAccountAndSessionRecognizeOfficialLoginWithoutInstalledCli()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True);
        var root = ReleaseDirectory();
        Assert.That(File.Exists(CopilotRuntimeSettings.BundledExecutablePath(root, interactive: true)), Is.True);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        foreach (var options in new[] { CopilotRuntimeSettings.AccountClientOptions(), CopilotRuntimeSettings.SessionClientOptions() })
        {
            options.Connection = RuntimeConnection.ForStdio(path: CopilotRuntimeSettings.BundledExecutablePath(root, interactive: false));
            options.Environment = new Dictionary<string, string>(options.Environment!) { ["PATH"] = string.Empty };
            await using var client = new CopilotClient(options);
            await client.StartAsync(timeout.Token);
            var auth = await client.GetAuthStatusAsync(timeout.Token);
            Assert.That(auth.IsAuthenticated && auth.AuthType == "user", Is.True,
                "O runtime publicado precisa reconhecer a conta oficial do usuário.");
            Assert.That(await client.ListModelsAsync(timeout.Token), Is.Not.Empty);
        }
    }
}
