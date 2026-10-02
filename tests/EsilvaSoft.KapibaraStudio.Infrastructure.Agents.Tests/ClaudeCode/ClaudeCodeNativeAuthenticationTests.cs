using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.ClaudeCode;

[TestFixture]
[Category("Unit")]
public sealed class ClaudeCodeNativeAuthenticationTests
{
    private const string Subscription = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","subscriptionType":"pro"}""";
    private const string ApiKey = """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","apiKeySource":"ANTHROPIC_API_KEY"}""";
    private const string Helper = """{"loggedIn":true,"authMethod":"api_key_helper","apiProvider":"firstParty","apiKeySource":"apiKeyHelper"}""";
    private const string Token = """{"loggedIn":true,"authMethod":"oauth_token","apiProvider":"firstParty"}""";
    private const string Cloud = """{"loggedIn":true,"authMethod":"bedrock","apiProvider":"bedrock"}""";
    private const string Console = """{"loggedIn":true,"authMethod":"console","apiProvider":"firstParty"}""";

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

    private static async Task<List<AgentProviderEvent>> RunAsync(IAgentSession session, AgentTurnRequest request)
    {
        List<AgentProviderEvent> events = [];
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }

    [TestCase(Subscription, "none", null)]
    [TestCase(ApiKey, "ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY")]
    [TestCase(Helper, "apiKeyHelper", null)]
    [TestCase(Token, "none", "CLAUDE_CODE_OAUTH_TOKEN")]
    [TestCase(Token, "none", "ANTHROPIC_AUTH_TOKEN")]
    [TestCase(Cloud, "none", "CLAUDE_CODE_USE_BEDROCK")]
    [TestCase(Console, "none", null)]
    public async Task NativeAuthenticationUsesTheExternalCliAndResumesWithoutChangingItsCredentials(
        string auth, string initSource, string? variable)
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = auth, InitApiKeySource = initSource, BlockingVariable = variable };
        var provider = Provider(system);
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.True);
        await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);

        var first = await RunAsync(session, Request());
        var second = await RunAsync(session, Request());

        Assert.Multiple(() =>
        {
            Assert.That(first.Concat(second).Where(item => item.Kind == AgentEventKind.AgentError), Is.Empty);
            Assert.That(system.Processes, Has.Count.EqualTo(2));
            Assert.That(system.Processes[1].Arguments, Does.Contain("--resume"));
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(system.Processes.All(process => process.InputBytesWritten > 0), Is.True);
            Assert.That(system.BlockingVariable, Is.EqualTo(variable), "A configuração externa não é alterada pelo app.");
        });
    }

    [TestCase(ApiKey)]
    [TestCase(Helper)]
    [TestCase(Token)]
    [TestCase(Cloud)]
    [TestCase(Console)]
    public async Task ChangingAuthenticationBeforeATurnBlocksBeforeCreatingAProcess(string changedAuth)
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = Subscription };
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        system.AuthOutput = changedAuth;
        var request = Request();

        var events = await RunAsync(session, request);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text,
                Is.EqualTo(ClaudeCodeErrorCodes.AuthenticationChanged));
            Assert.That(system.Processes, Is.Empty);
            Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.NothingSent));
        });
    }

    [Test]
    public async Task EnvironmentTokenBillingChangeIsDetectedEvenWhenAuthStatusIsIdentical()
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = Token, BlockingVariable = "CLAUDE_CODE_OAUTH_TOKEN" };
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        system.BlockingVariable = "ANTHROPIC_AUTH_TOKEN";

        var events = await RunAsync(session, Request());

        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text,
            Is.EqualTo(ClaudeCodeErrorCodes.AuthenticationChanged));
        Assert.That(system.Processes, Is.Empty);
    }

    [Test]
    public async Task AuthenticationChangeDuringProcessCreationKillsTheProcessBeforeWritingStdin()
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = Subscription };
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        system.BeforeProcessStart = () => system.AuthOutput = ApiKey;
        var request = Request();

        var events = await RunAsync(session, request);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text,
                Is.EqualTo(ClaudeCodeErrorCodes.AuthenticationChanged));
            Assert.That(system.Processes.Single().WasKilled, Is.True);
            Assert.That(system.Processes.Single().InputBytesWritten, Is.Zero);
            Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.NothingSent));
        });
    }

    [TestCase(Subscription, "ANTHROPIC_API_KEY")]
    [TestCase(ApiKey, "none")]
    [TestCase(ApiKey, "apiKeyHelper")]
    public async Task UnexpectedInitKeySourceAbortsAndNeverContinuesWithAnotherAuthentication(string auth, string observedSource)
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = auth, InitApiKeySource = observedSource };
        await using var session = await Provider(system).CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);

        var events = await RunAsync(session, Request());

        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo(ClaudeCodeErrorCodes.InitMismatch));
        Assert.That(system.Processes, Has.Count.EqualTo(1));
        Assert.That(system.Processes.Single().WasKilled, Is.True);
    }

    [TestCase("""{"loggedIn":true}""")]
    [TestCase("""{"loggedIn":true,"apiProvider":"firstParty"}""")]
    [TestCase("""{"loggedIn":true,"authMethod":"console","apiKeySource":{}}""")]
    [TestCase("""{"loggedIn":true,"authMethod":"console","apiProvider":"private secret canary"}""")]
    public void MalformedAuthenticationDoesNotBecomeADelegatedMethod(string status)
    {
        var parsed = ClaudeCodeAuthStatus.Parse(status);
        Assert.That(parsed.IsAuthenticated, Is.False);
        Assert.That(parsed.ToString(), Does.Not.Contain("secret canary"));
    }

    [Test]
    public async Task FailedAuthProbeCannotAuthorizeAReportedLogin()
    {
        var system = new MemoryClaudeCodeSystem { AuthOutput = Subscription, AuthExitCode = 1 };
        var status = await Provider(system).GetStatusAsync(CancellationToken.None);
        Assert.That(status.IsAvailable, Is.False);
        Assert.That(status.UnavailableCode, Is.EqualTo("AuthStatusUnreadable"));
        Assert.That(system.Processes, Is.Empty);
    }
}
