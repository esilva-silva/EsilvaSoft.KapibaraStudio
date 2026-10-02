using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.ClaudeCode;

[TestFixture]
[Category("Unit")]
public sealed class ClaudeCodeSystemBoundaryTests
{
    private static readonly string[] AuthStatusSuffix = ["auth", "status"];
    private static readonly string[] ConfiguredModels = ["opus", "opusplan", "claude-opus-4-1-20250805", "sonnet", "haiku"];

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
    public async Task NonzeroVersionProbeExitCannotProduceAUsableInstallation()
    {
        var system = new MemoryClaudeCodeSystem { VersionExitCode = 9 };
        Assert.That((await Provider(system).DetectAsync()).State, Is.EqualTo(ClaudeCodeInstallationState.VersionUnreadable));
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
    [TestCase("""{"loggedIn":true}""", "AuthStatusUnreadable")]
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
    public async Task ProPlanStatusPreservesTheConfiguredAllowlistAndDefault()
    {
        var system = new MemoryClaudeCodeSystem
        {
            AuthOutput = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro"}""",
        };
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
        {
            AllowedModelIds = ["opus", "opusplan", "claude-opus-4-1-20250805", "sonnet", "haiku"],
            DefaultModel = "opus",
            DedicatedWorkingDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "empty"),
            AppDataDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "appdata"),
            DatabasePath = system.DefaultDatabasePath,
        }, system);

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.True);
            Assert.That(status.Models, Is.EqualTo(ConfiguredModels));
            Assert.That(status.DefaultModel, Is.EqualTo("opus"));
            Assert.That(status.UnavailableCode, Is.Null);
        });
    }

    [TestCase("max")]
    [TestCase("team")]
    [TestCase("enterprise")]
    public async Task NonProSubscriptionKeepsConfiguredModelAllowlist(string subscriptionType)
    {
        var system = new MemoryClaudeCodeSystem
        {
            AuthOutput = $$"""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"{{subscriptionType}}"}""",
        };
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
        {
            AllowedModelIds = ["opus", "sonnet", "haiku"],
            DefaultModel = "opus",
            DedicatedWorkingDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "empty"),
            AppDataDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "appdata"),
            DatabasePath = system.DefaultDatabasePath,
        }, system);

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.That(status.Models, Is.EqualTo(["opus", "sonnet", "haiku"]));
        Assert.That(status.DefaultModel, Is.EqualTo("opus"));
    }

    [Test]
    public async Task ProAndMaxDelegateOpusAvailabilityToTheOfficialCli()
    {
        var proSystem = new MemoryClaudeCodeSystem
        {
            AuthOutput = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro"}""",
        };
        var proProvider = Provider(proSystem);
        await using var proSession = await proProvider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id) { ModelId = "opus" }, CancellationToken.None);
        Assert.That(proSystem.Processes, Is.Empty);

        var maxSystem = new MemoryClaudeCodeSystem
        {
            AuthOutput = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"max"}""",
        };
        var maxProvider = Provider(maxSystem);
        await using var session = await maxProvider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id) { ModelId = "opus" }, CancellationToken.None);
        Assert.That(maxSystem.Processes, Is.Empty);
    }

    [TestCase(false, ClaudeCodeAccountCommandState.Completed)]
    [TestCase(true, ClaudeCodeAccountCommandState.CommandFailed)]
    [TestCase(true, ClaudeCodeAccountCommandState.StillRunning)]
    public async Task AccountCommandsDelegateExactOfficialArgumentsAndPreserveCommandAndAuthResults(
        bool logout, ClaudeCodeAccountCommandState commandState)
    {
        var system = new MemoryClaudeCodeSystem { AccountCommandState = commandState };
        var provider = Provider(system);

        var result = logout
            ? await provider.LogoutAsync(userConfirmedGlobalLogout: true)
            : await provider.LoginAsync();

        Assert.Multiple(() =>
        {
            Assert.That(system.AccountCalls, Is.EqualTo(1));
            Assert.That(system.AccountArguments.Single(), Is.EqualTo(logout
                ? ClaudeCodeCommandLine.LogoutArguments
                : ClaudeCodeCommandLine.LoginArguments));
            Assert.That(result.State, Is.EqualTo(commandState));
            Assert.That(result.AuthStatus?.Kind, Is.EqualTo(ClaudeCodeAuthKind.Subscription));
            Assert.That(system.Probes.Last().TakeLast(2), Is.EqualTo(AuthStatusSuffix),
                "A conta é reconsultada somente pela CLI oficial.");
            Assert.That(system.Processes, Is.Empty);
        });
    }

    [Test]
    public async Task MissingPersistedSessionFallsBackOnceAndPublishesConversationNoticeAndFreshSessionId()
    {
        var system = new MemoryClaudeCodeSystem { PersistedResumeMissingOnce = true };
        var provider = Provider(system);
        var conversationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var persistedSessionId = Guid.Parse("20000000-0000-0000-0000-000000000002").ToString("D");
        List<AgentProviderSessionUpdate> updates = [];
        await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id)
        {
            ConversationId = conversationId,
            ResumeProviderSessionId = persistedSessionId,
            ProviderSessionObserver = updates.Add,
        }, CancellationToken.None);

        var events = await RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(system.Processes, Has.Count.EqualTo(2));
            Assert.That(system.Processes[0].Arguments, Does.Contain("--resume"));
            Assert.That(system.Processes[0].Arguments, Does.Contain(persistedSessionId));
            Assert.That(system.Processes[1].Arguments, Does.Contain("--session-id"));
            var replacementId = system.Processes[1].Arguments[
                Array.IndexOf(system.Processes[1].Arguments, "--session-id") + 1];
            Assert.That(Guid.TryParseExact(replacementId, "D", out _), Is.True);
            Assert.That(replacementId, Is.Not.EqualTo(persistedSessionId));
            Assert.That(session.LastTurn!.ResumeFallback, Is.True);
            Assert.That(session.LastTurn.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(events.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(updates.Any(update => update.ConversationId == conversationId &&
                update.Change == AgentProviderSessionChange.ResumeFallback && update.NoticeCode is not null), Is.True);
            Assert.That(updates.Any(update => update.ConversationId == conversationId &&
                update.Change == AgentProviderSessionChange.Established && update.ProviderSessionId == replacementId), Is.True);
            Assert.That(system.Processes.All(process => process.Disposed), Is.True);
        });
    }

    [Test]
    public void DescriptorSeparatesAutomatedContractsFromUnavailableProductToolCalling()
    {
        var descriptor = Provider(new MemoryClaudeCodeSystem()).Describe();

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.AuthenticationMethods, Does.Contain(AgentAuthenticationMethod.OfficialCliDelegated));
            Assert.That(descriptor.Capabilities.Evidence, Is.EqualTo(AgentCapabilityEvidence.AutomatedContract));
            Assert.That(descriptor.Capabilities.Chat, Is.True);
            Assert.That(descriptor.Capabilities.Streaming, Is.True);
            Assert.That(descriptor.Capabilities.ToolCalling, Is.False);
            Assert.That(descriptor.Capabilities.NativeTools, Is.True);
        });
    }

    [TestCase("""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro"}""", ClaudeCodeAuthKind.Subscription)]
    [TestCase("""{"loggedIn":true,"authMethod":"claude.ai","apiKeySource":"ANTHROPIC_API_KEY","subscriptionType":"pro"}""", ClaudeCodeAuthKind.ApiKey)]
    [TestCase("""{"loggedIn":true,"authMethod":"api_key_helper","apiKeySource":"apiKeyHelper"}""", ClaudeCodeAuthKind.ApiKeyHelper)]
    [TestCase("""{"loggedIn":true,"authMethod":"oauth_token"}""", ClaudeCodeAuthKind.EnvironmentToken)]
    [TestCase("""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"bedrock","subscriptionType":"pro"}""", ClaudeCodeAuthKind.CloudProvider)]
    [TestCase("""{"loggedIn":true,"authMethod":"claude.ai"}""", ClaudeCodeAuthKind.UnsupportedMethod)]
    [TestCase("""{"loggedIn":false,"authMethod":"claude.ai","subscriptionType":"pro"}""", ClaudeCodeAuthKind.NotLoggedIn)]
    public void AuthenticationStatusClassifiesPublicCliFieldsWithoutChangingItsMethod(string output, ClaudeCodeAuthKind expected)
    {
        var status = ClaudeCodeAuthStatus.Parse(output);

        Assert.That(status.Kind, Is.EqualTo(expected));
        Assert.That(status.IsSubscription, Is.EqualTo(expected == ClaudeCodeAuthKind.Subscription));
    }

    [Test]
    public void AuthenticationStatusDropsUnapprovedIdentityAndCredentialFields()
    {
        var status = ClaudeCodeAuthStatus.Parse(
            """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro","email":"identity-canary","organizationId":"org-canary","token":"secret-canary","home":"path-canary"}""");

        Assert.Multiple(() =>
        {
            Assert.That(status.IsSubscription, Is.True);
            Assert.That(status.ToString(), Does.Not.Contain("identity-canary"));
            Assert.That(status.ToString(), Does.Not.Contain("org-canary"));
            Assert.That(status.ToString(), Does.Not.Contain("secret-canary"));
            Assert.That(status.ToString(), Does.Not.Contain("path-canary"));
        });
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

    [TestCase(0, ClaudeCodeErrorCodes.StreamIncomplete)]
    [TestCase(9, ClaudeCodeErrorCodes.ProcessFailed)]
    public async Task PrematureStdoutEndUsesTheProcessExitStatus(int exitCode, string expectedError)
    {
        var system = new MemoryClaudeCodeSystem { OmitResultFrame = true, ProcessExitCode = exitCode };
        await using var session = (ClaudeCodeAgentSession)await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);

        var events = await RunAsync(session);

        Assert.That(events.Single(e => e.Kind == AgentEventKind.AgentError).Text, Is.EqualTo(expectedError));
        Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        Assert.That(system.Processes.Single().Disposed, Is.True);
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
