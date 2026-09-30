using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.ClaudeCode;

[TestFixture]
[Category("Unit")]
public sealed class ClaudeCodeSystemBoundaryTests
{
    private static ClaudeCodeAgentProvider Provider(MemoryClaudeCodeSystem system) => new(new ClaudeCodeAgentProviderOptions
    {
        DedicatedWorkingDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "empty"),
        AppDataDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "appdata"),
        DatabasePath = system.DefaultDatabasePath,
        DefaultModel = "haiku",
    }, system);

    private static AgentTurnRequest Request() => new(AgentTurnId.New(), "Responda ok", "tab-1", 1)
    {
        Plan = new AgentTurnPlan(AgentOperationMode.Agent, ["Read", "Glob", "Grep"], [], [], [],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
        SystemPrompt = "Agente de teste.",
    };

    private static async Task<List<AgentProviderEvent>> RunAsync(IAgentSession session, CancellationToken cancellationToken = default)
    {
        List<AgentProviderEvent> events = [];
        await foreach (var item in session.RunTurnAsync(Request(), cancellationToken)) events.Add(item);
        return events;
    }

    [Test]
    public void ConstructionRequiresAnExplicitResourceAdapter()
    {
        Assert.Throws<ArgumentNullException>(() => new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions(), null!));
        var system = new MemoryClaudeCodeSystem();
        _ = Provider(system).Describe();
        Assert.That(system.Probes, Is.Empty);
        Assert.That(system.CreatedDirectories, Is.Empty);
    }

    [TestCase(false, "ExecutableNotFound")]
    [TestCase(true, "UnsupportedExecutable")]
    public async Task MissingAndUnsupportedExecutablesDoNotProbeOrStart(bool unsupported, string reason)
    {
        var system = new MemoryClaudeCodeSystem { Missing = !unsupported, Unsupported = unsupported };
        var status = await Provider(system).GetStatusAsync(CancellationToken.None);
        Assert.That(status.UnavailableCode, Is.EqualTo(reason));
        Assert.That(system.Probes, Is.Empty);
        Assert.That(system.Processes, Is.Empty);
    }

    [TestCase("2.1.267", ClaudeCodeInstallationState.VersionTooLow)]
    [TestCase("sem versão", ClaudeCodeInstallationState.VersionUnreadable)]
    [TestCase("2.1.268", ClaudeCodeInstallationState.Found)]
    public async Task VersionPolicyUsesOnlyTheAdapterResult(string version, ClaudeCodeInstallationState expected)
    {
        var system = new MemoryClaudeCodeSystem { VersionOutput = version };
        Assert.That((await Provider(system).DetectAsync()).State, Is.EqualTo(expected));
    }

    [Test]
    public async Task VersionCacheInvalidatesWhenTheAdapterFingerprintChanges()
    {
        var system = new MemoryClaudeCodeSystem();
        var provider = Provider(system);
        await provider.DetectAsync();
        await provider.DetectAsync();
        system.Fingerprint = system.Fingerprint with { Length = 101 };
        await provider.DetectAsync();
        Assert.That(system.Probes.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ProbeTimeoutIsNotCachedAndCanRecover()
    {
        var system = new MemoryClaudeCodeSystem { VersionTimedOut = true };
        var provider = Provider(system);
        Assert.That((await provider.DetectAsync()).State, Is.EqualTo(ClaudeCodeInstallationState.ProbeTimedOut));
        system.VersionTimedOut = false;
        Assert.That((await provider.DetectAsync()).IsUsable, Is.True);
        Assert.That(system.Probes.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task MetadataFailureIsVisibleAsUnavailableAndDoesNotStartAProcess()
    {
        var system = new MemoryClaudeCodeSystem { FingerprintFailure = new IOException("private-path-canary") };
        var status = await Provider(system).GetStatusAsync(CancellationToken.None);
        Assert.That(status.IsAvailable, Is.False);
        Assert.That(status.UnavailableCode, Does.Not.Contain("private-path-canary"));
        Assert.That(system.Processes, Is.Empty);
    }

    [TestCase("ANTHROPIC_API_KEY")]
    [TestCase("CLAUDE_CODE_OAUTH_TOKEN")]
    [TestCase("ANTHROPIC_BASE_URL")]
    [TestCase("CLAUDECODE")]
    public async Task EnvironmentOverrideBlocksBeforeAnyCliCall(string variable)
    {
        var system = new MemoryClaudeCodeSystem { BlockingVariable = variable };
        var status = await Provider(system).GetStatusAsync(CancellationToken.None);
        Assert.That(status.UnavailableCode, Is.EqualTo("BlockedEnvironment"));
        Assert.That(system.Probes, Is.Empty);
        Assert.That(system.Processes, Is.Empty);
    }

    [TestCase("""{"loggedIn":false}""", "NotLoggedIn")]
    [TestCase("""{"loggedIn":true,"authMethod":"api_key","apiKeySource":"ANTHROPIC_API_KEY"}""", "NonSubscriptionAuthentication")]
    [TestCase("saída ilegível", "AuthStatusUnreadable")]
    public async Task AuthenticationFailureNeverFallsBackOrCreatesATurn(string output, string reason)
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = output };
        var provider = Provider(system);
        var status = await provider.GetStatusAsync(CancellationToken.None);
        Assert.That(status.UnavailableCode, Is.EqualTo(reason));
        Assert.ThrowsAsync<ClaudeCodeUnavailableException>(() => provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None));
        Assert.That(system.Processes, Is.Empty);
    }

    [Test]
    public void PreviewEvaluatesTheFinalLinkTargetAndHasNoDirectoryCreation()
    {
        var system = new MemoryClaudeCodeSystem();
        var alias = Path.Combine(MemoryClaudeCodeSystem.Root, "project-link");
        system.Directories.Add(alias);
        system.Links[alias] = Path.Combine(system.HomeDirectory, ".claude");
        var preview = Provider(system).PreviewWorkingDirectory(alias);
        Assert.That(preview.Rejection, Is.EqualTo(ClaudeCodeWorkspaceRejection.ProtectedArea));
        Assert.That(preview.ProtectedArea, Is.EqualTo(ClaudeCodeProtectedArea.ClaudeConfig));
        Assert.That(system.CreatedDirectories, Is.Empty);
    }

    [Test]
    public async Task SuccessfulTurnsStreamAndResumeThroughTheInjectedProcess()
    {
        var system = new MemoryClaudeCodeSystem();
        await using var session = (ClaudeCodeAgentSession)await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var first = await RunAsync(session);
        var second = await RunAsync(session);
        Assert.That(first.Where(e => e.Kind == AgentEventKind.AgentError).Select(e => e.Text), Is.Empty);
        Assert.That(string.Concat(first.Where(e => e.Kind == AgentEventKind.MessageDelta).Select(e => e.Text)), Is.EqualTo("ok"));
        Assert.That(second.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(system.Processes[0].Arguments, Does.Contain("--session-id"));
        Assert.That(system.Processes[1].Arguments, Does.Contain("--resume"));
        Assert.That(system.Processes.All(p => p.Disposed), Is.True);
    }

    [Test]
    public async Task UnexpectedRuntimeToolAbortsTheOwnedProcess()
    {
        var system = new MemoryClaudeCodeSystem { UnexpectedTool = "UnapprovedTool" };
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var events = await RunAsync(session);
        Assert.That(events.Single(e => e.Kind == AgentEventKind.AgentError).Text, Is.EqualTo(ClaudeCodeErrorCodes.InitMismatch));
        Assert.That(system.Processes.Single().WasKilled, Is.True);
    }

    [Test]
    public async Task CancellingOneSessionKillsOnlyItsProcessAndReportsUnknownOutcome()
    {
        var pendingSystem = new MemoryClaudeCodeSystem { HangTurn = true };
        var otherSystem = new MemoryClaudeCodeSystem { HangTurn = true };
        await using var pending = (ClaudeCodeAgentSession)await Provider(pendingSystem).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        await using var other = (ClaudeCodeAgentSession)await Provider(otherSystem).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pendingRun = RunAsync(pending, cancellation.Token);
        var otherRun = RunAsync(other);
        var process = await pendingSystem.ProcessStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var otherProcess = await otherSystem.ProcessStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await process.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await otherProcess.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await pendingRun.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(process.WasKilled, Is.True);
        Assert.That(process.Disposed, Is.True);
        Assert.That(pending.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        Assert.That(otherProcess.WasKilled, Is.False, "O outro turno ainda está pendente, antes de sua própria limpeza.");
        Assert.That(otherProcess.Disposed, Is.False);
        Assert.That(otherRun.IsCompleted, Is.False);
        otherProcess.ReleaseOutput();
        var events = await otherRun.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(other.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task ExecutableRevalidationPreventsSendingAPromptAfterDiscovery()
    {
        var system = new MemoryClaudeCodeSystem();
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        system.ExecutableValid = false;
        var events = await RunAsync(session);
        Assert.That(events.Single(e => e.Kind == AgentEventKind.AgentError).Text, Is.EqualTo(ClaudeCodeErrorCodes.ExecutableUnavailable));
        Assert.That(system.Processes, Is.Empty);
    }
}
