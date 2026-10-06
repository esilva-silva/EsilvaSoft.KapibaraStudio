using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
public sealed class CopilotCompatibilityBoundaryTests
{
    private static readonly string[] ExpectedModels = ["fake-model"];
    private string _root = null!;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "KapibaraStudioCompatibilityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveRoot()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudioCompatibilityTests"));
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
    }

    [TestCase("OldProtocol", "CopilotCliProtocolIncompatible")]
    [TestCase("FutureProtocol", "CopilotCliProtocolIncompatible")]
    [TestCase("MissingProtocol", "CopilotCliProtocolIncompatible")]
    [TestCase("ConnectFailure", "CopilotProviderUnavailable")]
    [TestCase("MissingAuth", "CopilotProviderUnavailable")]
    [TestCase("MissingModels", "CopilotProviderUnavailable")]
    [CancelAfter(30_000)]
    public async Task IncompatibleOrIncompleteRuntimeDoesNotPublishChatOrSendPrompt(string scenario, string expectedCode)
    {
        using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry(),
            _ => CreateClient(scenario), () => true, Path.Combine(_root, "sessions"));
        await provider.CheckAccountAndModelsAsync(TestContext.CurrentContext.CancellationToken);
        var status = await provider.GetStatusAsync(TestContext.CurrentContext.CancellationToken);
        var methods = File.ReadAllLines(Path.Combine(_root, "methods.txt"));
        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.False);
            Assert.That(status.Capabilities.Chat, Is.False);
            Assert.That(status.UnavailableCode, Is.EqualTo(expectedCode));
            Assert.That(methods, Does.Not.Contain("session.create").And.Not.Contain("session.send"));
            if (scenario is "OldProtocol" or "FutureProtocol" or "MissingProtocol" or "ConnectFailure")
                Assert.That(methods, Does.Not.Contain("auth.getStatus").And.Not.Contain("models.list"));
        });
    }

    [Test, CancelAfter(30_000)]
    public async Task CompatibleRuntimePublishesAccountModelsWithoutOpeningSession()
    {
        using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry(),
            _ => CreateClient("Compatible"), () => true, Path.Combine(_root, "sessions"));
        await provider.CheckAccountAndModelsAsync(TestContext.CurrentContext.CancellationToken);
        var status = await provider.GetStatusAsync(TestContext.CurrentContext.CancellationToken);
        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.True);
            Assert.That(status.Models, Is.EqualTo(ExpectedModels));
            Assert.That(File.ReadAllLines(Path.Combine(_root, "methods.txt")),
                Does.Contain("connect").And.Contain("auth.getStatus").And.Contain("models.list")
                    .And.Not.Contain("session.create").And.Not.Contain("session.send"));
        });
    }

    [TestCase("OldProtocol")]
    [TestCase("FutureProtocol")]
    [TestCase("MissingProtocol")]
    [TestCase("ConnectFailure")]
    [CancelAfter(30_000)]
    public async Task SessionHandshakeFailureStopsBeforeAuthenticationOrPrompt(string scenario)
    {
        var sessionUpdates = new List<AgentProviderSessionUpdate>();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                ProviderSessionObserver = sessionUpdates.Add,
            }, CreateClient(scenario));
        var request = new AgentTurnRequest(AgentTurnId.New(), "prompt-must-not-escape-canary", "synthetic-tab", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, TestContext.CurrentContext.CancellationToken))
            events.Add(item);

        var methods = File.ReadAllLines(Path.Combine(_root, "methods.txt"));
        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(AgentEventKind.AgentError));
            Assert.That(events[0].Text, Is.EqualTo("CopilotRuntimeStartFailed"));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(events),
                Does.Not.Contain("secret-should-not-escape").And.Not.Contain("canary")
                    .And.Not.Contain("SDK protocol version mismatch"));
            Assert.That(methods.Count(method => method == "connect"), Is.EqualTo(1));
            Assert.That(methods, Does.Not.Contain("auth.getStatus").And.Not.Contain("models.list")
                .And.Not.Contain("session.create").And.Not.Contain("session.resume")
                .And.Not.Contain("session.options.update").And.Not.Contain("session.send"));
            Assert.That(sessionUpdates, Is.Empty);
            Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.NothingSent));
        });
    }

    [Test, CancelAfter(30_000)]
    public async Task MissingBuiltInAgentRestrictionMethodBlocksTurnBeforePrompt()
    {
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"), CreateClient("MissingRestrictions"));
        var request = new AgentTurnRequest(AgentTurnId.New(), "must-not-send", "synthetic-tab", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, TestContext.CurrentContext.CancellationToken)) events.Add(item);
        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.True);
            Assert.That(events.Any(item => item.Text?.Contains("secret-should-not-escape", StringComparison.Ordinal) == true), Is.False);
            Assert.That(File.ReadAllLines(Path.Combine(_root, "methods.txt")),
                Does.Contain("session.create").And.Contain("session.options.update").And.Not.Contain("session.send"));
        });
    }

    private CopilotClient CreateClient(string scenario)
    {
        var script = FindScript();
        var powershell = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : "pwsh";
        string[] arguments = OperatingSystem.IsWindows()
            ? ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script]
            : ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script];
        arguments = [.. arguments, "-Scenario", scenario, "-LogPath", Path.Combine(_root, "methods.txt")];
        return new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(powershell, arguments),
            Mode = CopilotClientMode.CopilotCli,
            UseLoggedInUser = false,
            BaseDirectory = _root,
        });
    }

    private static string FindScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Copilot", "FakeCopilotCompatibilityRuntime.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Compatibility runtime fixture was not found.");
    }

    private sealed class NoToolsRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Tools must not run in compatibility checks.");
    }
}
