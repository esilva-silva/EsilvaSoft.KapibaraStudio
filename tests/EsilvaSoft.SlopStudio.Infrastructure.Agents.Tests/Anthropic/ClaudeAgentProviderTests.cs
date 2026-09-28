using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Anthropic;

/// <summary>Configuração, credencial, capabilities, catálogo e orçamento do provider Claude (offline).</summary>
[TestFixture]
[CancelAfter(30_000)]
public sealed class ClaudeAgentProviderTests
{
    private static readonly string[] OfficialModelIds = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"];
    private static readonly string[] FirstChunkOnly = ["0123456789"];

    private static AgentTurnRequest Turn(string message = "Olá") => new(AgentTurnId.New(), message, "tab-1", 1);

    private static async Task<List<AgentProviderEvent>> RunAsync(
        IAgentSession session, AgentTurnRequest request, Func<AgentProviderEvent, AgentToolResult?>? onTool = null)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.ToolRequested && onTool?.Invoke(item) is { } result)
            {
                _ = Task.Run(() => session.SubmitToolResultAsync(result, CancellationToken.None), CancellationToken.None);
            }
        }

        return events;
    }

    private static AgentToolResult Ok(AgentTurnRequest request, AgentProviderEvent tool) =>
        new(AgentSessionId.New(), request.TurnId, tool.ToolCallId!.Value, AgentToolResultStatus.Succeeded, "{}");

    [Test]
    public async Task WithoutKeyReferenceProviderIsUnavailableAndNeverCallsTheVault()
    {
        using var handler = new FakeClaudeHandler();
        var credentials = new FakeCredentialProvider();
        using var provider = ClaudeFixture.Provider(handler, ClaudeFixture.Options() with { ApiKeyReference = null }, credentials);

        var availability = await provider.GetAvailabilityAsync();
        var failure = Assert.ThrowsAsync<ClaudeProviderUnavailableException>(() =>
            provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None));

        Assert.That(availability, Is.EqualTo(new ClaudeAgentAvailability(false, ClaudeAgentUnavailableReason.NotConfigured,
            AgentProviderCapabilities.None with { UsesNetwork = true })));
        Assert.That(failure!.Reason, Is.EqualTo(ClaudeAgentUnavailableReason.NotConfigured));
        Assert.That(credentials.Calls, Is.Zero);
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(SecretStoreFailureCode.NotFound, ClaudeAgentUnavailableReason.CredentialMissing)]
    [TestCase(SecretStoreFailureCode.Locked, ClaudeAgentUnavailableReason.VaultUnavailable)]
    [TestCase(SecretStoreFailureCode.Unavailable, ClaudeAgentUnavailableReason.VaultUnavailable)]
    [TestCase(SecretStoreFailureCode.Cancelled, ClaudeAgentUnavailableReason.VaultUnavailable)]
    public async Task VaultFailuresMakeProviderUnavailableWithoutNetwork(SecretStoreFailureCode code, ClaudeAgentUnavailableReason expected)
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler, credentials: new FakeCredentialProvider { Failure = code });

        var availability = await provider.GetAvailabilityAsync();

        Assert.That(availability.IsAvailable, Is.False);
        Assert.That(availability.Reason, Is.EqualTo(expected));
        Assert.That(availability.Capabilities, Is.EqualTo(AgentProviderCapabilities.None with { UsesNetwork = true }));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task CredentialRemovedAfterSessionStartFailsTheTurnBeforeAnyRequest()
    {
        using var handler = new FakeClaudeHandler();
        var credentials = new FakeCredentialProvider();
        using var provider = ClaudeFixture.Provider(handler, credentials: credentials);
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        credentials.Failure = SecretStoreFailureCode.NotFound;

        var events = await RunAsync(session, Turn());

        Assert.That(events.Single().Text, Is.EqualTo(ClaudeErrorCodes.CredentialUnavailable));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task EffectiveCapabilitiesExposeOnlyWhatTheAdapterImplements()
    {
        using var handler = new FakeClaudeHandler();
        using var withoutTools = ClaudeFixture.Provider(handler);
        using var withTools = ClaudeFixture.Provider(handler,
            registry: new CatalogOnlyRegistry(("list_connections", """{"type":"object","properties":{},"additionalProperties":false}""")));

        IAgentProvider port = withTools;
        var plain = (await ((IAgentProvider)withoutTools).GetStatusAsync(CancellationToken.None)).Capabilities;
        var tooled = await port.GetStatusAsync(CancellationToken.None);
        var descriptor = port.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Chat && plain.Streaming && plain.Sessions && plain.ModelSelection && plain.UsesNetwork, Is.True);
            Assert.That(plain.ToolCalling, Is.False, "Sem tools liberadas no registry não há tool calling.");
            Assert.That(tooled.IsAvailable, Is.True);
            Assert.That(tooled.AuthState, Is.EqualTo(AgentProviderAuthState.Configured));
            Assert.That(tooled.Capabilities.ToolCalling, Is.True);
            Assert.That(tooled.DefaultModel, Is.EqualTo(ClaudeAgentProviderOptions.DefaultModelId));
            Assert.That(tooled.Models, Is.EqualTo(ClaudeAgentProviderOptions.DefaultModelIds));
            Assert.That(descriptor.ProviderId, Is.EqualTo(ClaudeAgentProvider.Id));
            Assert.That(descriptor.AuthenticationMethods, Is.EqualTo(new[] { AgentAuthenticationMethod.ApiKey }),
                "Somente API Key; nenhum login de assinatura.");
            Assert.That(descriptor.Capabilities.Evidence, Is.EqualTo(AgentCapabilityEvidence.AutomatedContract),
                "Sem homologação real a evidência não é Homologated.");
            foreach (var capabilities in new[] { plain, tooled.Capabilities, descriptor.Capabilities })
            {
                Assert.That(capabilities.FileEditing || capabilities.CommandExecution || capabilities.SubAgents ||
                            capabilities.ThinkingSummary || capabilities.Mcp || capabilities.CodeProposals, Is.False);
            }
        });
        Assert.That(port.IsLocal, Is.False, "Destino de saída é sempre externo.");
        Assert.That(handler.Requests, Is.Empty, "Descrever e consultar estado não chama a rede.");
    }

    [Test]
    public async Task StatusReportsInvalidCredentialAfterRejectionAndVaultStates()
    {
        using var handler = new FakeClaudeHandler().EnqueueError(System.Net.HttpStatusCode.Unauthorized, "authentication_error");
        using var provider = ClaudeFixture.Provider(handler);
        IAgentProvider port = provider;
        await using (var session = await port.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None))
        {
            await RunAsync(session, Turn());
        }

        var rejected = await port.GetStatusAsync(CancellationToken.None);
        using var locked = ClaudeFixture.Provider(handler, credentials: new FakeCredentialProvider { Failure = SecretStoreFailureCode.Locked });
        var vault = await ((IAgentProvider)locked).GetStatusAsync(CancellationToken.None);
        using var missing = ClaudeFixture.Provider(handler, ClaudeFixture.Options() with { ApiKeyReference = null });
        var notConfigured = await ((IAgentProvider)missing).GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((rejected.IsAvailable, rejected.AuthState, rejected.UnavailableCode),
                Is.EqualTo((false, AgentProviderAuthState.Invalid, "CredentialRejected")));
            Assert.That((vault.AuthState, vault.UnavailableCode), Is.EqualTo((AgentProviderAuthState.VaultUnavailable, "VaultUnavailable")));
            Assert.That(notConfigured.AuthState, Is.EqualTo(AgentProviderAuthState.NotConfigured));
            Assert.That(rejected.Capabilities.Chat || vault.Capabilities.Chat || notConfigured.Capabilities.Chat, Is.False);
        });
    }

    [Test]
    public void ModelOutsideTheConfiguredListIsRejected()
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler);

        var failure = Assert.ThrowsAsync<ClaudeProviderUnavailableException>(() =>
            provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id, "claude-opus-5-20260401"), CancellationToken.None));

        Assert.That(failure!.Reason, Is.EqualTo(ClaudeAgentUnavailableReason.ModelNotAllowed));
    }

    [Test]
    public void WrongProviderIdIsRejected()
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler);

        Assert.That(() => provider.CreateSessionAsync(new AgentSessionOptions("openai"), CancellationToken.None),
            Throws.ArgumentException);
    }

    [Test]
    public void InvalidConfigurationIsRejected()
    {
        var credentials = new FakeCredentialProvider();
        Assert.Multiple(() =>
        {
            Assert.That(() => new ClaudeAgentProvider(credentials, ClaudeFixture.Options() with { DefaultModel = "claude-x" }),
                Throws.ArgumentException);
            Assert.That(() => new ClaudeAgentProvider(credentials, ClaudeFixture.Options() with { BaseUrl = new Uri("http://api.example.com") }),
                Throws.ArgumentException, "Somente HTTPS fora de loopback.");
            Assert.That(() => new ClaudeAgentProvider(credentials, ClaudeFixture.Options() with { AllowedModelIds = [] }),
                Throws.ArgumentException);
            Assert.That(() => new ClaudeAgentProvider(credentials, ClaudeFixture.Options() with
            {
                Budget = ClaudeAgentBudget.Default with { MaxOutputTokensPerRequest = 0 },
            }), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void DefaultModelIdsMatchTheCurrentOfficialCatalogConsultedForThisRelease()
    {
        // Referência consultada em 24/09/2026 (skill claude-api, tabela "Current Models"): IDs sem sufixo de data.
        Assert.That(ClaudeAgentProviderOptions.DefaultModelIds, Is.EqualTo(OfficialModelIds));
        Assert.That(ClaudeAgentProviderOptions.DefaultModelIds, Has.None.Matches<string>(static id => id.Any(char.IsUpper)));
    }

    [Test]
    public async Task RegistryCatalogIsDeclaredWithoutSlopSchemaIdentity()
    {
        const string schema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"urn:esilvasoft:slopstudio:agent-tool:list_databases:v1:input",
             "type":"object","properties":{"connection_id":{"type":"string"}},"required":["connection_id"],"additionalProperties":false}
            """;
        using var handler = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("ok").Stop("end_turn").Build());
        using var provider = ClaudeFixture.Provider(handler, registry: new CatalogOnlyRegistry(("list_databases", schema),
            (new string('a', 65), """{"type":"object"}""")));
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        await RunAsync(session, Turn());

        var tools = handler.Requests.Single().Json.GetProperty("tools");
        Assert.That(tools.GetArrayLength(), Is.EqualTo(1), "Nome acima do limite da API não é declarado.");
        var tool = tools[0];
        var inputSchema = tool.GetProperty("input_schema");
        Assert.Multiple(() =>
        {
            Assert.That(tool.GetProperty("name").GetString(), Is.EqualTo("list_databases"));
            Assert.That(inputSchema.TryGetProperty("$id", out _), Is.False);
            Assert.That(inputSchema.TryGetProperty("$schema", out _), Is.False);
            Assert.That(inputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(inputSchema.GetProperty("required")[0].GetString(), Is.EqualTo("connection_id"));
        });
    }

    [Test]
    public async Task RequestBudgetStopsTheToolLoop()
    {
        var toolRound = new SseBuilder().Start().ToolUse("toolu_1", "list_connections", "{}").Stop("tool_use").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(toolRound);
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxRequestsPerTurn = 1 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var request = Turn();

        var events = await RunAsync(session, request, tool => Ok(request, tool));

        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.RequestBudgetExceeded));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TokenBudgetUsesProviderUsageAndStopsBeforeTheNextRequest()
    {
        var toolRound = new SseBuilder().Start(inputTokens: 900).ToolUse("toolu_1", "list_connections", "{}").Stop("tool_use", 200).Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(toolRound);
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxTokensPerTurn = 1000 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var request = Turn();

        var events = await RunAsync(session, request, tool => Ok(request, tool));

        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.TokenBudgetExceeded));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task OutputCharacterBudgetEndsTheStream()
    {
        var sse = new SseBuilder().Start().Text("0123456789", "0123456789", "não deveria chegar").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(sse);
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxOutputCharsPerTurn = 15 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Where(static e => e.Kind == AgentEventKind.MessageDelta).Select(static e => e.Text),
            Is.EqualTo(FirstChunkOnly));
        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.OutputBudgetExceeded));
    }

    [Test]
    public async Task OversizedToolArgumentsAreRejectedLocally()
    {
        var round = new SseBuilder().Start().ToolUse("toolu_big", "mongo_find", "{\"filter\":\"", new string('x', 64), "\"}")
            .Stop("tool_use").Build();
        var end = new SseBuilder().Start().Text("ok").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(round).EnqueueSse(end);
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxToolArgumentsChars = 32 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolRequested), Is.False);
        var result = handler.Requests.ElementAt(1).Json.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.That(result.GetProperty("content").GetString(), Does.Contain(ClaudeErrorCodes.ToolArgumentsTooLarge));
    }

    [Test]
    public async Task InputAndContextBudgetsFailBeforeAnyRequest()
    {
        using var handler = new FakeClaudeHandler();
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxUserInputChars = 8 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        var tooLarge = await RunAsync(session, Turn("mensagem longa demais"));
        var empty = await RunAsync(session, Turn("   "));

        Assert.That(tooLarge.Single().Text, Is.EqualTo(ClaudeErrorCodes.InputTooLarge));
        Assert.That(empty.Single().Text, Is.EqualTo(ClaudeErrorCodes.EmptyMessage));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task HistoryBudgetRefusesInsteadOfSilentlyTruncating()
    {
        using var handler = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("r").Stop("end_turn").Build());
        var options = ClaudeFixture.Options();
        using var provider = ClaudeFixture.Provider(handler, options with { Budget = options.Budget with { MaxHistoryMessages = 2 } });
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        await RunAsync(session, Turn("1"));
        var second = await RunAsync(session, Turn("2"));

        Assert.That(second.Single().Text, Is.EqualTo(ClaudeErrorCodes.ContextBudgetExceeded));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SecretNeverAppearsInEventsOrErrors()
    {
        using var handler = new FakeClaudeHandler()
            .EnqueueSse(new SseBuilder().Start().Text("resposta").Stop("end_turn").Build())
            .EnqueueError(System.Net.HttpStatusCode.Unauthorized, "authentication_error");
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);

        var events = (await RunAsync(session, Turn())).Concat(await RunAsync(session, Turn())).ToList();

        Assert.That(events.Select(static e => e.ToString()), Has.None.Contains(ClaudeFixture.SyntheticKey));
        Assert.That(events.Select(static e => e.ToString()), Has.None.Contains("synthetic"), "Mensagem de erro do provider não é repassada.");
    }

    [Test]
    public void RegistrationReadsNoSecretAndExposesTheProviderOnce()
    {
        var credentials = new FakeCredentialProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IAgentCredentialProvider>(credentials);
        // Default budget: the fixture's short tool-result wait would not cover the runtime's worst-case tool call.
        services.AddSlopStudioClaudeAgentProvider(ClaudeFixture.Options(ClaudeAgentBudget.Default));

        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IAgentProvider>().ToArray();

        Assert.That(providers.Select(static item => item.ProviderId), Is.EqualTo(new[] { ClaudeAgentProvider.Id }));
        Assert.That(providers[0].IsLocal, Is.False);
        Assert.That(credentials.Calls, Is.Zero);
        Assert.Throws<InvalidOperationException>(() => services.AddSlopStudioClaudeAgentProvider());
    }

    [Test]
    public void DefaultToolResultWaitCoversTheRuntimeWorstCaseToolCallWithMargin()
    {
        // A write waiting for human approval: 35 s preflight + 120 s approval + 2x5 s stop + 35 s execution = 200 s.
        var required = AgentRuntimeOptions.Default.MaxToolCallDuration + AgentProviderServiceCollectionExtensions.ToolResultWaitMargin;

        Assert.That(ClaudeAgentBudget.Default.ToolResultTimeout, Is.GreaterThanOrEqualTo(required));
        Assert.DoesNotThrow(() => new ServiceCollection().AddSingleton(AgentRuntimeOptions.Default)
            .AddSlopStudioClaudeAgentProvider(ClaudeFixture.Options(ClaudeAgentBudget.Default)));
    }

    [Test]
    public void RegistrationRefusesAToolResultWaitShorterThanTheComposedRuntimeBudget()
    {
        var shortWait = ClaudeFixture.Options(ClaudeAgentBudget.Default with { ToolResultTimeout = TimeSpan.FromSeconds(170) });
        var tighterRuntime = new AgentRuntimeOptions { ApprovalTimeout = TimeSpan.FromSeconds(60) };
        var slowerRuntime = new AgentRuntimeOptions { ToolTimeout = TimeSpan.FromSeconds(90) };

        Assert.Multiple(() =>
        {
            // The old 170 s default: shorter than the default runtime's 200 s worst case plus margin.
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSingleton(AgentRuntimeOptions.Default)
                .AddSlopStudioClaudeAgentProvider(shortWait));
            // Derived from the composed runtime, not a fixed number: a shorter approval window fits in 170 s.
            Assert.DoesNotThrow(() => new ServiceCollection().AddSingleton(tighterRuntime)
                .AddSlopStudioClaudeAgentProvider(shortWait));
        });

        // Order-independent: runtime options composed after the provider are checked when the provider is resolved.
        var services = new ServiceCollection();
        services.AddSingleton<IAgentCredentialProvider>(new FakeCredentialProvider());
        services.AddSlopStudioClaudeAgentProvider(ClaudeFixture.Options(ClaudeAgentBudget.Default));
        services.AddSingleton(slowerRuntime);
        using var container = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<ClaudeAgentProvider>());
    }
}
