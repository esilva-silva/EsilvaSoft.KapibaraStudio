using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentToolRegistryTests
{
    private static readonly Guid PrincipalId = Guid.Parse("a1fba9ce-c3c4-4ee0-8c74-3e74cfc8f4a1");
    private static readonly string[] SummaryPropertyNames = ["id", "name", "readOnly"];
    private static readonly string[] AllowedDatabaseName = ["allowed"];
    private static readonly string[] VisibleCollectionName = ["visible"];
    private static readonly string[] IndexSummaryPropertyNames = ["name", "keyFields", "keyDirections", "unique", "sparse", "hidden", "ttlSeconds", "partialFilterFields"];
    private static readonly string[] DatabaseAuditToolNames = ["list_databases", "list_databases"];
    private static readonly AgentAuditOutcome[] CancelledAuditOutcomes =
        [AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled];
    private static readonly Guid SessionId = Guid.Parse("a6284a92-6942-4bd6-846a-cf879b08f17e");
    private static readonly Guid TurnId = Guid.Parse("6b8097c6-8313-4ed6-9a02-2e657fd9a387");
    private static readonly AgentPermission[] ExplainPermissions =
        [AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments];
    private static AgentOutputDestination LocalDestination => AgentOutputDestination.Local();
    private static AgentOutputDataScope Metadata => AgentOutputDataScope.Metadata;

    [Test]
    public async Task TurnQuotaDeniesTheTwentyFirstAuditableCallBeforeProfileAccess()
    {
        var profiles = new StubProfileRepository([]);
        var policy = Policy(104);
        var provider = new CountingPolicyProvider(policy);
        var audit = new RecordingAuditRepository();
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), audit: audit);

        for (var i = 0; i < 20; i++)
        {
            var admitted = await registry.InvokeAsync(Principal(104), Context(), LocalDestination,
                Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
            Assert.That(admitted.Succeeded, Is.True, $"Call {i + 1} should be admitted.");
        }
        var profileReads = profiles.GetAllCalls;
        var denied = await registry.InvokeAsync(Principal(104), Context(), LocalDestination,
            Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(denied.ErrorCode, Is.EqualTo("ToolCallLimitExceeded"));
        Assert.That(profiles.GetAllCalls, Is.EqualTo(profileReads));
        Assert.That(audit.Events[^2].Outcome, Is.EqualTo(AgentAuditOutcome.Intent));
        Assert.That(audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
        Assert.That(audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.LimitExceeded));
        var recovered = await registry.InvokeAsync(Principal(104), new AgentInvocationContext(null, null, SessionId, Guid.NewGuid()),
            LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
        Assert.That(profiles.GetAllCalls, Is.GreaterThan(profileReads));
    }

    [Test]
    public async Task ListConnectionsRequiresClosedEmptyObjectAndKnownTool()
    {
        var profiles = new StubProfileRepository([]);
        var policy = Policy(4);
        var registry = Registry(profiles, new SequencePolicyProvider(policy), new AgentPermissionEvaluator(new SequencePolicyProvider(policy)));
        var principal = Principal(4);

        var extra = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{\"approved\":true}");
        var control = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{\"principalId\":\"spoof\"}");
        var unknown = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, "run_command", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(extra.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(control.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(unknown.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task MissingOrUnreadablePolicyDeniesBeforeEnumeratingProfiles()
    {
        var profiles = new StubProfileRepository([Connection("restrita")]);
        var missingProvider = new SequencePolicyProvider([null]);
        var missing = Registry(profiles, missingProvider, new AgentPermissionEvaluator(missingProvider));
        var unreadableProvider = new SequencePolicyProvider(true);
        var unreadable = Registry(profiles, unreadableProvider, new AgentPermissionEvaluator(unreadableProvider));

        var missingResult = await missing.InvokeAsync(Principal(5), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var unreadableResult = await unreadable.InvokeAsync(Principal(5), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(missingResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(unreadableResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ListConnectionsReturnsOnlyProfilesWithExplicitConnectionGrantAndAllowlistedFields()
    {
        var allowed = Connection("permitida") with
        {
            ConnectionString = "mongodb://user:uri-canary@mongo-host:27017",
            Environment = "environment-canary",
            TargetHost = "target-canary",
            Tags = "tag-canary",
            Folder = "folder-canary"
        };
        var denied = Connection("negada");
        var policy = Policy(8, Grant(allowed));
        var provider = new SequencePolicyProvider(policy);
        var profiles = new StubProfileRepository([allowed, denied]);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(8), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        var root = output.RootElement;
        var items = root.GetProperty("connections").EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Length.EqualTo(1));
            Assert.That(items[0].GetProperty("id").GetString(), Is.EqualTo(allowed.Id.ToString()));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("permitida"));
            Assert.That(items[0].GetProperty("readOnly").GetBoolean(), Is.False);
            Assert.That(items[0].EnumerateObject().Select(property => property.Name), Is.EquivalentTo(SummaryPropertyNames));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("uri-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("mongo-host"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("environment-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("target-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("tag-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("folder-canary"));
        });
    }

    [Test]
    public async Task ListConnectionsCapsAuthorizedProfilesAndMarksTruncation()
    {
        var all = Enumerable.Range(0, 201).Select(index => Connection($"conexao-{index:D3}")).ToArray();
        var policy = Policy(9, all.Select(Grant).ToArray());
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository(all), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(9), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("connections").GetArrayLength(), Is.EqualTo(200));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ListConnectionsTruncatesAtUtf8ItemBoundariesAndNeverReturnsPartialJson()
    {
        var small = Connection("pequena");
        var tooLarge = Connection(string.Concat(Enumerable.Repeat("😀", 70_000)));
        var policy = Policy(14, Grant(small), Grant(tooLarge));
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository([small, tooLarge]), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(14), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("connections").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(), Is.EqualTo("pequena"));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ListConnectionsDeniesCorruptProfileCollectionWithoutReturningPartialOutput()
    {
        var invalidProfiles = new ConnectionProfile?[]
        {
            null,
            Connection("nome-nulo") with { Name = null! },
            Connection("nome-invalido") with { Name = "nome\uD800" },
            Connection("id-vazio") with { Id = Guid.Empty },
            Connection("generation-ausente") with { SourceGenerationId = null },
            Connection("generation-vazia") with { SourceGenerationId = Guid.Empty }
        };

        foreach (var invalidProfile in invalidProfiles)
        {
            var provider = new SequencePolicyProvider(Policy(15));
            var profiles = new StubProfileRepository([Connection("válida"), invalidProfile!]);
            var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

            var result = await registry.InvokeAsync(Principal(15), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"), $"Perfil inválido: {invalidProfile?.Name ?? "null"}");
            Assert.That(result.StructuredContentJson, Is.Null, "dados parciais não podem escapar");
        }

        var nullListProvider = new SequencePolicyProvider(Policy(15));
        var nullListRegistry = Registry(new StubProfileRepository(returnNull: true), nullListProvider, new AgentPermissionEvaluator(nullListProvider));
        var nullListResult = await nullListRegistry.InvokeAsync(Principal(15), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        Assert.That(nullListResult.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(nullListResult.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task ListConnectionsEnforcesDeadlineOnPolicyAndProfileReads()
    {
        var policyTimeoutRegistry = Registry(
            new StubProfileRepository([Connection("nao-enumerada")]),
            new BlockingPolicyProvider(),
            new AgentPermissionEvaluator(new BlockingPolicyProvider()),
            TimeSpan.FromMilliseconds(20));
        var policyTimeout = await policyTimeoutRegistry.InvokeAsync(Principal(16), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        var profileRepository = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Array.Empty<ConnectionProfile>();
            }
        };
        var policy = Policy(17);
        var profileProvider = new SequencePolicyProvider(policy);
        var profileTimeoutRegistry = Registry(profileRepository, profileProvider, new AgentPermissionEvaluator(profileProvider), TimeSpan.FromMilliseconds(20));
        var profileTimeout = await profileTimeoutRegistry.InvokeAsync(Principal(17), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(policyTimeout.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(profileTimeout.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(policyTimeout.StructuredContentJson, Is.Null);
            Assert.That(profileTimeout.StructuredContentJson, Is.Null);
        });
    }

    [Test]
    public async Task ListConnectionsDeadlineReturnsWhenRepositoryIgnoresCancellationAndObservesLateFault()
    {
        var lateOperation = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = _ => lateOperation.Task
        };
        var policy = Policy(20);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(30));

        var result = await registry.InvokeAsync(Principal(20), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}").WaitAsync(TimeSpan.FromSeconds(2));
        lateOperation.TrySetException(new InvalidOperationException("falha tardia não deve escapar"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(profiles.GetAllCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CallerCancellationRemainsCancellationWhenRepositoryIgnoresToken()
    {
        var lateOperation = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = _ => lateOperation.Task
        };
        var policy = Policy(21);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var invocation = registry.InvokeAsync(Principal(21), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
        lateOperation.TrySetException(new InvalidOperationException("falha tardia não deve escapar"));
    }

    [Test]
    public async Task ListConnectionsRevalidatesPolicyRevisionBeforeReturningOutput()
    {
        var profile = Connection("permitida");
        var provider = new SequencePolicyProvider(Policy(10, Grant(profile)), Policy(10, Grant(profile)), Policy(11));
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(10), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task ListConnectionsHonorsCancellationBeforeProfileAccess()
    {
        var profiles = new StubProfileRepository([Connection("local")]);
        var policy = Policy(12, Grant(profiles.Profiles[0]));
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => registry.InvokeAsync(
            Principal(12), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token));
        Assert.That(profiles.GetAllCalls, Is.Zero);
    }

    [TestCase("generation")]
    [TestCase("name")]
    [TestCase("readOnly")]
    [TestCase("id")]
    [TestCase("removed")]
    [TestCase("duplicate")]
    [TestCase("invalidName")]
    [TestCase("missingGeneration")]
    [TestCase("nullProfile")]
    [TestCase("nullCollection")]
    public async Task ListConnectionsDeniesChangedProjectedProfilesBeforeReturningOutput(string change)
    {
        var profile = Connection("permitida");
        IReadOnlyList<ConnectionProfile> final = change switch
        {
            "generation" => [profile with { SourceGenerationId = Guid.NewGuid() }],
            "name" => [profile with { Name = "renomeada" }],
            "readOnly" => [profile with { IsReadOnly = !profile.IsReadOnly }],
            "id" => [profile with { Id = Guid.NewGuid() }],
            "removed" => [],
            "duplicate" => [profile, profile],
            "invalidName" => [profile with { Name = "inválido\uD800" }],
            "missingGeneration" => [profile with { SourceGenerationId = null }],
            "nullProfile" => [null!],
            "nullCollection" => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var profiles = new SequenceProfileRepository([profile], final);
        var provider = new SequencePolicyProvider(Policy(22, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(22), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(change == "nullCollection" ? "ExecutionFailed" : "PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ListConnectionsAcceptsUnchangedProjectionWhenFinalOrderOrUnexposedFieldsChange()
    {
        var first = Connection("primeira");
        var second = Connection("segunda");
        var profiles = new SequenceProfileRepository([first, second],
            [second with { Tags = "não expor" }, first with { IsFavorite = true }]);
        var provider = new SequencePolicyProvider(Policy(23, Grant(first), Grant(second)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(23), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var connections = json.RootElement.GetProperty("connections");
        Assert.That(connections.GetArrayLength(), Is.EqualTo(2));
        Assert.That(connections[0].GetProperty("name").GetString(), Is.EqualTo(first.Name));
        Assert.That(connections[1].GetProperty("name").GetString(), Is.EqualTo(second.Name));
        Assert.That(result.StructuredContentJson, Does.Not.Contain("não expor"));
        Assert.That(profiles.GetAllCalls, Is.EqualTo(4));
    }

    [Test]
    public async Task FinalProfileReadFailureDoesNotExposeRepositoryError()
    {
        var profile = Connection("permitida");
        var profiles = new SequenceProfileRepository([profile], [profile])
        {
            FinalReadHandler = _ => throw new InvalidOperationException("mongodb://private:secret@host")
        };
        var provider = new SequencePolicyProvider(Policy(24, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(24), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("ExecutionFailed"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task FinalProfileReadHonorsDeadlineWhenRepositoryIgnoresCancellation()
    {
        var profile = Connection("permitida");
        var lateRead = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new SequenceProfileRepository([profile], [profile]) { FinalReadHandler = _ => lateRead.Task };
        var provider = new SequencePolicyProvider(Policy(25, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(30));

        var result = await registry.InvokeAsync(Principal(25), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}").WaitAsync(TimeSpan.FromSeconds(2));
        lateRead.TrySetException(new InvalidOperationException("falha tardia privada"));

        Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task FinalProfileReadHonorsCallerCancellationWhenRepositoryIgnoresToken()
    {
        var profile = Connection("permitida");
        var enteredFinalRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateRead = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new SequenceProfileRepository([profile], [profile])
        {
            FinalReadHandler = _ => { enteredFinalRead.SetResult(); return lateRead.Task; }
        };
        var provider = new SequencePolicyProvider(Policy(26, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(26), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        await enteredFinalRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
        lateRead.TrySetException(new InvalidOperationException("falha tardia privada"));
    }

    [Test]
    public async Task ListConnectionsRequiresInvocationAndExplicitOutputScopeBeforeProfileEnumeration()
    {
        var profiles = new StubProfileRepository([]);
        var policy = Policy(18);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var noContext = await registry.InvokeAsync(Principal(18), null, LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var noDestination = await registry.InvokeAsync(Principal(18), Context(), null, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var noScope = await registry.InvokeAsync(Principal(18), Context(), LocalDestination, null, AgentToolRegistry.ListConnectionsToolName, "{}");
        var wrongScope = await registry.InvokeAsync(Principal(18), Context(), LocalDestination, AgentOutputDataScope.DocumentValues, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(noContext.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(noDestination.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(noScope.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(wrongScope.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ExternalOutputRequiresMatchingProviderAndExactGenerationGrant()
    {
        var profile = Connection("externa");
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        // Internal principal (native chat): its channel carries no MCP client ID.
        var externalContext = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var mismatchedContext = new AgentInvocationContext("provider-b", null, SessionId, TurnId);
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id), externalDestination, Metadata);
        var policy = Policy(19, grant);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var mismatch = await registry.InvokeAsync(Principal(19), mismatchedContext, externalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var matching = await registry.InvokeAsync(Principal(19), externalContext, externalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(mismatch.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(matching.Succeeded, Is.True);
    }

    [Test]
    public void ListConnectionsDescriptorPublishesClosedSchemasAndCanonicalReadMetadataGrant()
    {
        var policy = Policy(13);
        var registry = Registry(new StubProfileRepository([]), new SequencePolicyProvider(policy), new AgentPermissionEvaluator(new SequencePolicyProvider(policy)));
        var descriptor = registry.FindDescriptor(AgentToolRegistry.ListConnectionsToolName)!;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Risk, Is.EqualTo(AgentToolRisk.ReadOnly));
            Assert.That(descriptor.RequiredPermissions, Is.EqualTo(new[] { AgentPermission.ReadMetadata }));
            Assert.That(registry.GetInputSchemaJson(descriptor.Name), Does.Contain("\"additionalProperties\":false"));
            Assert.That(registry.GetOutputSchemaJson(descriptor.Name), Does.Contain("\"additionalProperties\":false"));
        });
    }

    [TestCase("mongodb://user:uri-password-canary@private-host:27017", "uri-password-canary")]
    [TestCase("sk-proj-token-canary-0123456789abcdefghijklmnopqrstuv", "token-canary")]
    [TestCase("arbitrary-secret-canary-7F41B9", "arbitrary-secret-canary")]
    [TestCase("Produção", "Produção")]
    public async Task ExternalConnectionNamesAlwaysUseAliasesWhileLocalNamesRemainUnchanged(string name, string canary)
    {
        var profile = Connection(name);
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var externalContext = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var externalGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            externalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(27, Grant(profile), externalGrant));
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var external = await registry.InvokeAsync(Principal(27), externalContext, externalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");
        var local = await registry.InvokeAsync(Principal(27), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(external.Succeeded, Is.True);
        Assert.That(local.Succeeded, Is.True);
        using var externalJson = JsonDocument.Parse(external.StructuredContentJson!);
        using var localJson = JsonDocument.Parse(local.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(externalJson.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(),
                Is.EqualTo($"Conexão {profile.Id:D}"));
            Assert.That(external.StructuredContentJson, Does.Not.Contain(canary));
            Assert.That(localJson.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(), Is.EqualTo(name));
            Assert.That(Encoding.UTF8.GetByteCount(external.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        });
    }

    [Test]
    public async Task ExternalAliasStillRevalidatesOriginalNameBeforeReturningOutput()
    {
        var profile = Connection("nome original");
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var externalContext = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var externalGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            externalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(28, externalGrant));
        var profiles = new SequenceProfileRepository([profile], [profile with { Name = "nome alterado" }]);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(28), externalContext, externalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task AuditIntentFailureBlocksPolicyAndProfileAccess()
    {
        var profiles = new StubProfileRepository([Connection("restrita")]);
        var provider = new CountingPolicyProvider(Policy(31));
        var audit = new RecordingAuditRepository
        {
            AppendHandler = (_, _) => throw new IOException("segredo de armazenamento")
        };
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), audit: audit);

        var result = await registry.InvokeAsync(Principal(31), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(provider.LoadCalls, Is.Zero);
            Assert.That(profiles.GetAllCalls, Is.Zero);
            Assert.That(audit.Events, Is.Empty);
        });
    }

    [Test]
    public async Task AuditTerminalFailureSuppressesSuccessfulOutput()
    {
        var profile = Connection("permitida");
        var provider = new SequencePolicyProvider(Policy(32, Grant(profile)));
        var audit = new RecordingAuditRepository
        {
            AppendHandler = (entry, _) => entry.Outcome == AgentAuditOutcome.Intent
                ? Task.CompletedTask : throw new IOException("segredo de armazenamento")
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);

        var result = await registry.InvokeAsync(Principal(32), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[] { AgentAuditOutcome.Intent }));
    }

    [Test]
    public async Task RevocationDuringSuccessAuditSuppressesAlreadyReadOutput()
    {
        var profile = Connection("nome privado canary");
        var provider = new MutablePolicyProvider(Policy(32, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audit = new RecordingAuditRepository
        {
            AppendHandler = async (entry, _) =>
            {
                if (entry.Outcome != AgentAuditOutcome.Succeeded) return;
                entered.TrySetResult();
                await release.Task;
            }
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);

        var pending = registry.InvokeAsync(Principal(32), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        provider.Current = Policy(33);
        release.TrySetResult();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(audit.Events.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded, AgentAuditOutcome.Denied }));
            Assert.That(audit.Events[^1].DecisionReason,
                Is.EqualTo(AgentAuditDecisionReason.PolicyRevisionMismatch));
            Assert.That(audit.Events[^1].OutputBytes, Is.Zero);
        });
    }

    [Test]
    public async Task CancellationDuringSuccessAuditSuppressesAlreadyReadOutput()
    {
        var profile = Connection("permitida");
        var provider = new MutablePolicyProvider(Policy(32, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audit = new RecordingAuditRepository
        {
            AppendHandler = async (entry, _) =>
            {
                if (entry.Outcome != AgentAuditOutcome.Succeeded) return;
                entered.TrySetResult();
                await release.Task;
            }
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);
        using var cancellation = new CancellationTokenSource();

        var pending = registry.InvokeAsync(Principal(32), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        release.TrySetResult();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.That(audit.Events.Select(item => item.Outcome),
            Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded, AgentAuditOutcome.Cancelled }));
        Assert.That(audit.Events[^1].OutputBytes, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AuditCorrelatesIntentWithSuccessOrDenial(bool allowed)
    {
        var profile = Connection("nome privado canary");
        var provider = new SequencePolicyProvider(allowed ? Policy(33, Grant(profile)) : null);
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);

        var result = await registry.InvokeAsync(Principal(33), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.EqualTo(allowed));
        Assert.That(audit.Events, Has.Count.EqualTo(2));
        var intent = audit.Events[0];
        var terminal = audit.Events[1];
        Assert.Multiple(() =>
        {
            Assert.That(intent.Outcome, Is.EqualTo(AgentAuditOutcome.Intent));
            Assert.That(intent.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.NotEvaluated));
            Assert.That(terminal.InvocationId, Is.EqualTo(intent.InvocationId));
            Assert.That(terminal.PrincipalId, Is.EqualTo(intent.PrincipalId));
            Assert.That(terminal.StartedAtUtc, Is.EqualTo(intent.StartedAtUtc));
            Assert.That(terminal.Outcome, Is.EqualTo(allowed ? AgentAuditOutcome.Succeeded : AgentAuditOutcome.Denied));
            Assert.That(terminal.ItemCount, Is.EqualTo(allowed ? 1 : 0));
            Assert.That(terminal.OutputBytes, Is.EqualTo(allowed ? Encoding.UTF8.GetByteCount(result.StructuredContentJson!) : 0));
            Assert.That(string.Join(" ", audit.Events.Select(item => item.ToString())), Does.Not.Contain("nome privado canary"));
        });
    }

    [Test]
    public async Task AuditKeepsTypedCauseAndSeparatesExecutionFailureFromPermissionDenial()
    {
        var original = Connection("permitida");
        var changed = original with { SourceGenerationId = Guid.NewGuid() };
        var scenarios = new (string Name, IConnectionProfileRepository Profiles,
            IAgentAuthorizationPolicyProvider Policies, long Revision,
            AgentAuditDecisionReason Reason, AgentAuditOutcome Outcome)[]
        {
            ("missing policy", new StubProfileRepository([original]),
                new SequencePolicyProvider((AgentAuthorizationPolicySnapshot?)null), 40,
                AgentAuditDecisionReason.PolicyMissing, AgentAuditOutcome.Denied),
            ("policy read failure", new StubProfileRepository([original]),
                new SequencePolicyProvider(throws: true), 40,
                AgentAuditDecisionReason.PolicyUnavailable, AgentAuditOutcome.Denied),
            ("policy revision changed", new StubProfileRepository([original]),
                new SequencePolicyProvider(Policy(41, Grant(original))), 40,
                AgentAuditDecisionReason.PolicyRevisionMismatch, AgentAuditOutcome.Denied),
            ("permission/source generation missing", new StubProfileRepository([changed]),
                new SequencePolicyProvider(Policy(40, Grant(original))), 40,
                AgentAuditDecisionReason.PermissionMissing, AgentAuditOutcome.Denied),
            ("profile repository failed", new StubProfileRepository([])
                { GetAllHandler = _ => throw new IOException("private profile store detail") },
                new SequencePolicyProvider(Policy(40, Grant(original))), 40,
                AgentAuditDecisionReason.ExecutionFailed, AgentAuditOutcome.Failed),
            ("profile final read failed", new SequenceProfileRepository([original], [original])
                { FinalReadHandler = _ => throw new IOException("private final read detail") },
                new SequencePolicyProvider(Policy(40, Grant(original))), 40,
                AgentAuditDecisionReason.ExecutionFailed, AgentAuditOutcome.Failed)
        };

        foreach (var scenario in scenarios)
        {
            var audit = new RecordingAuditRepository();
            var registry = Registry(scenario.Profiles, scenario.Policies,
                new AgentPermissionEvaluator(scenario.Policies), audit: audit);
            var result = await registry.InvokeAsync(Principal(scenario.Revision), Context(),
                LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo(scenario.Reason == AgentAuditDecisionReason.ExecutionFailed ? "ExecutionFailed" : "PermissionDenied"), scenario.Name);
                Assert.That(result.StructuredContentJson, Is.Null, scenario.Name);
                Assert.That(audit.Events, Has.Count.EqualTo(2), scenario.Name);
                Assert.That(audit.Events[^1].DecisionReason, Is.EqualTo(scenario.Reason), scenario.Name);
                Assert.That(audit.Events[^1].Outcome, Is.EqualTo(scenario.Outcome), scenario.Name);
            });
        }
    }

    [Test]
    public async Task AuditRecordsCallerCancellationAfterIntent()
    {
        var enteredPolicy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new GatePolicyProvider(enteredPolicy);
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(34), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        await enteredPolicy.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await invocation);
        Assert.That(audit.Events.Select(item => item.Outcome),
            Is.EqualTo(CancelledAuditOutcomes));
    }

    [Test]
    public async Task AuditRecordsDeadlineAfterIntent()
    {
        var provider = new BlockingPolicyProvider();
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([]), provider,
            new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(20), audit);

        var result = await registry.InvokeAsync(Principal(35), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        Assert.That(audit.Events.Select(item => item.Outcome),
            Is.EqualTo(CancelledAuditOutcomes));
    }

    [Test]
    public async Task ExternalAuditUsesValidatedTechnicalProviderIdentifier()
    {
        var profile = Connection("segredo livre canary");
        var destination = AgentOutputDestination.ProviderExternal("provider-a");
        var context = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForConnection(profile.Id), destination, Metadata);
        var provider = new SequencePolicyProvider(Policy(36, grant));
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit);

        var result = await registry.InvokeAsync(Principal(36), context, destination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(audit.Events, Has.Count.EqualTo(2));
        Assert.That(audit.Events.All(item => item.Channel == AgentAuditChannel.ProviderExternal &&
            item.ExternalIdentifier == "provider-a"), Is.True);
        Assert.That(string.Join(" ", audit.Events), Does.Not.Contain("segredo livre canary"));
    }

    private static AgentToolRegistry Registry(
        IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies,
        IAgentPermissionEvaluator evaluator,
        TimeSpan? timeout = null,
        IAgentAuditRepository? audit = null,
        IMongoMetadataSource? metadata = null,
        IAgentMongoFindSource? find = null,
        IAgentMongoCountSource? count = null,
        IAgentMongoDistinctSource? distinct = null,
        IAgentMongoIndexSource? indexes = null,
        IAgentMongoExplainSource? explain = null) => new(profiles, policies, evaluator, audit ?? new RecordingAuditRepository(), timeout, metadata, find: find, count: count, distinct: distinct, indexes: indexes, explain: explain,
        exposure: AllReadStages, principalAuthority: new TestAgentPrincipalAuthority());

    // Behavioral fixtures exercise the whole read catalog. Closed-by-default exposure has dedicated tests.
    private static AgentToolExposure AllReadStages => AgentToolExposure.Through(AgentToolExposureStage.DerivedReads);

    [Test]
    public async Task MongoCountRequiresDocumentGrantsAndReturnsCanonicalInt64WithAudit()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(80, grants));
        var source = new StubCountSource { Result = new("{\"$numberLong\":\"9007199254740993\"}", true) };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, count: source);
        var filter = "{\"name\":\"ENV.PRIVATE_CANARY\",\"amount\":{\"$numberDecimal\":\"12.5\"}}";
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", filterEjson = filter, maxTimeMs = 30_000 });

        var result = await registry.InvokeAsync(Principal(80), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName, arguments);

        Assert.That(result.Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(source.LastQuery!.FilterEjson, Is.EqualTo(filter));
            Assert.That(source.LastQuery.MaxTimeMs, Is.EqualTo(5_000));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(audit.Events[0].Permission, Is.EqualTo(AgentPermission.ReadDocuments));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
            Assert.That(audit.Events[1].OutputBytes, Is.GreaterThan(0));
        });
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("countEjson").GetString(),
                Is.EqualTo("{\"$numberLong\":\"9007199254740993\"}"));
            Assert.That(output.RootElement.GetProperty("estimated").GetBoolean(), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListConnectionsExternalTruncationCountsOnlyAuthorizedProfilesAndNeverReadsMongo(bool authorizeExtra)
    {
        var authorized = Enumerable.Range(0, 200).Select(index => Connection("name-secret-canary-" + index) with
        {
            ConnectionString = "mongodb://account-canary:uri-secret-canary@host-canary:27017",
            TargetHost = "target-host-canary", Environment = "environment-canary"
        }).ToArray();
        var extra = Connection("extra-private-name-canary");
        var denied = Connection("denied-private-name-canary");
        var destination = AgentOutputDestination.ProviderExternal("provider-a");
        var context = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var granted = authorizeExtra ? authorized.Append(extra) : authorized;
        var grants = granted.Select(profile => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id), destination, Metadata)).ToArray();
        var provider = new SequencePolicyProvider(Policy(9, grants));
        var metadata = new StubMetadataSource { Fail = true };
        var indexes = new StubIndexSource();
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([denied, .. authorized, extra]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, metadata: metadata, indexes: indexes);

        var result = await registry.InvokeAsync(Principal(9), context, destination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var connections = json.RootElement.GetProperty("connections").EnumerateArray().ToArray();
        var publicOutput = result.StructuredContentJson + JsonSerializer.Serialize(audit.Events);
        Assert.Multiple(() =>
        {
            Assert.That(connections, Has.Length.EqualTo(200));
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.EqualTo(authorizeExtra),
                "An inaccessible extra profile must not change the truncation bit.");
            Assert.That(connections.Select(item => item.GetProperty("id").GetGuid()),
                Is.EqualTo(authorized.Select(profile => profile.Id)));
            Assert.That(connections.Select(item => item.GetProperty("name").GetString()),
                Is.EqualTo(authorized.Select(profile => $"Conexão {profile.Id:D}")));
            Assert.That(connections.All(item => item.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal).SequenceEqual(SummaryPropertyNames.Order(StringComparer.Ordinal))), Is.True);
            Assert.That(publicOutput, Does.Not.Contain("canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain(denied.Id.ToString("D")));
            Assert.That(result.StructuredContentJson, Does.Not.Contain(extra.Id.ToString("D")));
            Assert.That(metadata.DatabaseCalls + metadata.CollectionCalls + metadata.BoundedDatabaseCalls +
                metadata.BoundedCollectionCalls + indexes.Calls, Is.Zero);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(200));
            Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        });
    }

    [Test]
    public async Task MongoCountReturnsCanonicalZeroForEmptyFilteredResult()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(145, grants));
        const string filter = "{\"state\":\"no-matches\"}";
        var source = new StubCountSource { Result = new("{\"$numberLong\":\"0\"}", true) };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, count: source);
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", filterEjson = filter });

        var result = await registry.InvokeAsync(Principal(145), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName, arguments);

        Assert.That(result.Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastQuery!.FilterEjson, Is.EqualTo(filter));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1),
                "A contagem é um resultado escalar de saída, mesmo quando seu valor é zero.");
        });
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("countEjson").GetString(),
                Is.EqualTo("{\"$numberLong\":\"0\"}"));
            Assert.That(output.RootElement.GetProperty("estimated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task MongoCountCallerCancellationDiscardsLateSourceResultWithoutRetry()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(151, grants));
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResult = new TaskCompletionSource<AgentMongoCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubCountSource
        {
            Handler = _ =>
            {
                sourceStarted.TrySetResult();
                return lateResult.Task;
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, count: source);
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(151), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}",
            cancellation.Token);
        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await invocation);
        lateResult.TrySetResult(new AgentMongoCountResult("{\"$numberLong\":\"42\"}", true));
        await lateResult.Task;

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(audit.Events.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
        });
    }

    [Test]
    public async Task MongoCountDeadlineDiscardsLateSourceResultWithoutRetry()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(152, grants));
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResult = new TaskCompletionSource<AgentMongoCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubCountSource
        {
            Handler = _ =>
            {
                sourceStarted.TrySetResult();
                return lateResult.Task;
            }
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), timeout: TimeSpan.FromMilliseconds(1_000), count: source);

        var invocation = registry.InvokeAsync(Principal(152), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");
        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await invocation.WaitAsync(TimeSpan.FromSeconds(3));
        lateResult.TrySetResult(new AgentMongoCountResult("{\"$numberLong\":\"42\"}", true));
        await lateResult.Task;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
        });
    }

    [Test]
    public async Task MongoCountDeniesMissingGrantAndRejectsUnclosedOrCostlyInputs()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var onlyQueryGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(81, onlyQueryGrant));
        var source = new StubCountSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), count: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(81), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName, prefix + "}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var suffix in new[]
                 { ",\"approved\":true}", ",\"limit\":1}", ",\"maxTimeMs\":0}",
                   ",\"maxTimeMs\":30001}", ",\"filterEjson\":\"{\\\"$where\\\":\\\"true\\\"}\"}",
                   ",\"filterEjson\":\"ObjectId('507f1f77bcf86cd799439011')\"}",
                   ",\"database\":\"allowed\"}" })
        {
            var invalid = await registry.InvokeAsync(Principal(81), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task MongoCountSuppressesUnverifiedOrNonCanonicalResult()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var arguments = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}";
        foreach (var invalid in new[] { new AgentMongoCountResult("{\"$numberLong\":\"2\"}", false),
                     new AgentMongoCountResult("2", true),
                     new AgentMongoCountResult("{\"$numberLong\":\"-1\"}", true) })
        {
            var provider = new SequencePolicyProvider(Policy(82, grants));
            var registry = Registry(new StubProfileRepository([profile]), provider,
                new AgentPermissionEvaluator(provider), count: new StubCountSource { Result = invalid });
            var result = await registry.InvokeAsync(Principal(82), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName, arguments);
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        }
    }

    [Test]
    public async Task MongoCountSuppressesOutputWhenPolicyIsRevokedAfterCount()
    {
        var profile = Connection("count");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var allowed = Policy(83, grants);
        var provider = new SequencePolicyProvider(allowed, allowed, allowed, allowed, Policy(84, grants));
        var source = new StubCountSource { Result = new("{\"$numberLong\":\"42\"}", true) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), count: source);

        var result = await registry.InvokeAsync(Principal(83), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoCountToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [Test]
    public async Task SampleDocumentsUsesFirstFindPageWithLiteralProjectionAndAudit()
    {
        var profile = Connection("sample");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(90, grants));
        var document = "{\"literal\":\"ENV.PRIVATE_CANARY\",\"value\":{\"$numberLong\":\"9007199254740993\"}}";
        var source = new StubFindSource { Page = new([document], false, false, true, false) };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, find: source);
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", projectionEjson = "{\"_id\":0,\"literal\":1,\"value\":1}" });

        var result = await registry.InvokeAsync(Principal(90), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName, arguments);

        Assert.That(result.Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(source.LastQuery!.FilterEjson, Is.EqualTo("{}"));
            Assert.That(source.LastQuery.Skip, Is.Zero);
            Assert.That(source.LastQuery.SortEjson, Is.Null);
            Assert.That(source.LastQuery.Limit, Is.EqualTo(5));
            Assert.That(source.LastQuery.MaxTimeMs, Is.EqualTo(5_000));
            Assert.That(source.LastQuery.ProjectionEjson, Is.EqualTo("{\"_id\":0,\"literal\":1,\"value\":1}"));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
        });
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("documentsEjson")[0].GetString(), Is.EqualTo(document));
    }

    [Test]
    public void SampleDocumentsOutputSchemaCapsDocumentsAtTwenty()
    {
        var policy = Policy(90);
        var registry = Registry(new StubProfileRepository([]), new SequencePolicyProvider(policy),
            new AgentPermissionEvaluator(new SequencePolicyProvider(policy)), find: new StubFindSource());
        using var schema = JsonDocument.Parse(registry.GetOutputSchemaJson(AgentToolRegistry.SampleDocumentsToolName)!);

        Assert.Multiple(() =>
        {
            Assert.That(schema.RootElement.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(schema.RootElement.GetProperty("properties").GetProperty("documentsEjson")
                .GetProperty("maxItems").GetInt32(), Is.EqualTo(20));
            Assert.That(schema.RootElement.GetProperty("properties").GetProperty("returnedCount")
                .GetProperty("maximum").GetInt32(), Is.EqualTo(20));
        });
    }

    [Test]
    public async Task MongoFindOneReturnsCanonicalDocumentOrNullAndAuditsActualItemCount()
    {
        var profile = Connection("find-one");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        const string bson = "{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"n\":{\"$numberLong\":\"9007199254740993\"},\"literal\":\"ENV.PRIVATE_CANARY\"}";
        foreach (var (items, expectedCount) in new[]
                 { ((IReadOnlyList<string>)new[] { bson }, 1), ((IReadOnlyList<string>)Array.Empty<string>(), 0) })
        {
            var provider = new SequencePolicyProvider(Policy(94, grants));
            var source = new StubFindSource { Page = new(items, false, false, true, false) };
            var audit = new RecordingAuditRepository();
            var registry = Registry(new StubProfileRepository([profile]), provider,
                new AgentPermissionEvaluator(provider), audit: audit, find: source);
            var filter = "{\"literal\":\"ENV.PRIVATE_CANARY\"}";
            var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
                collection = "visible", filterEjson = filter, projectionEjson = "{\"_id\":0,\"n\":1}",
                sortEjson = "{\"_id\":1}" });

            var result = await registry.InvokeAsync(Principal(94), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName, arguments);

            Assert.That(result.Succeeded, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(source.LastQuery!.Limit, Is.EqualTo(1));
                Assert.That(source.LastQuery.Skip, Is.Zero);
                Assert.That(source.LastQuery.FilterEjson, Is.EqualTo(filter));
                Assert.That(source.LastQuery.ProjectionEjson, Is.EqualTo("{\"_id\":0,\"n\":1}"));
                Assert.That(source.LastQuery.SortEjson, Is.EqualTo("{\"_id\":1}"));
                Assert.That(audit.Events[1].ItemCount, Is.EqualTo(expectedCount));
                Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            });
            using var output = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.That(output.RootElement.EnumerateObject().Single().Name, Is.EqualTo("documentEjson"));
            Assert.That(expectedCount == 0
                ? output.RootElement.GetProperty("documentEjson").ValueKind == JsonValueKind.Null
                : output.RootElement.GetProperty("documentEjson").GetString() == bson, Is.True);
        }
    }

    [Test]
    public async Task MongoFindOneRejectsLimitSkipAndMissingDocumentGrant()
    {
        var profile = Connection("find-one");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(95, grant));
        var source = new StubFindSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(95), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName, prefix + "}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var suffix in new[] { ",\"limit\":1}", ",\"skip\":0}", ",\"approved\":true}",
                     ",\"filterEjson\":\"ObjectId('507f1f77bcf86cd799439011')\"}",
                     ",\"projectionEjson\":\"{\\\"public\\\":1,\\\"secret\\\":0}\"}" })
        {
            var invalid = await registry.InvokeAsync(Principal(95), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        Assert.That(source.Calls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoFindOneReturnsOneMatchButRejectsAnOverpopulatedSourcePage(bool returnTwoDocuments)
    {
        var profile = Connection("find-one");
        var policies = new SequencePolicyProvider(Policy(154, FindOneDocumentGrants(profile)));
        const string first = "{\"number\":{\"$numberLong\":\"42\"}}";
        var source = new StubFindSource
        {
            Page = new(returnTwoDocuments ? [first, "{\"secret\":\"extra-match-canary\"}"] : [first],
                true, false, true, false)
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), audit: audit, find: source);
        var arguments = JsonSerializer.Serialize(new
        {
            connectionId = profile.Id, database = "allowed", collection = "visible",
            filterEjson = "{\"enabled\":true}", projectionEjson = "{\"_id\":0,\"number\":1}",
            sortEjson = "{\"priority\":-1,\"_id\":1}"
        });

        var result = await registry.InvokeAsync(Principal(154), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName, arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.EqualTo(!returnTwoDocuments));
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastQuery?.Limit, Is.EqualTo(1));
            Assert.That(source.LastQuery?.Skip, Is.Zero);
            Assert.That(source.LastQuery?.FilterEjson, Is.EqualTo("{\"enabled\":true}"));
            Assert.That(source.LastQuery?.ProjectionEjson, Is.EqualTo("{\"_id\":0,\"number\":1}"));
            Assert.That(source.LastQuery?.SortEjson, Is.EqualTo("{\"priority\":-1,\"_id\":1}"));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(returnTwoDocuments ? 0 : 1));
            Assert.That(result.StructuredContentJson + JsonSerializer.Serialize(audit.Events),
                Does.Not.Contain("extra-match-canary"));
        });
        if (returnTwoDocuments)
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
        }
        else
        {
            using var json = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.That(json.RootElement.GetProperty("documentEjson").GetString(), Is.EqualTo(first));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoFindOneSuppressesDocumentWhenPolicyOrGenerationChangesDuringRead(bool changeGeneration)
    {
        var profile = Connection("find-one");
        var policies = new MutablePolicyProvider(Policy(155, FindOneDocumentGrants(profile)));
        IReadOnlyList<ConnectionProfile> currentProfiles = [profile];
        var repository = new StubProfileRepository { GetAllHandler = _ => Task.FromResult(currentProfiles) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ => { entered.TrySetResult(); return release.Task; }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(repository, policies, new AgentPermissionEvaluator(policies), audit: audit, find: source);
        var pending = registry.InvokeAsync(Principal(155), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (changeGeneration) currentProfiles = [profile with { SourceGenerationId = Guid.NewGuid() }];
            else policies.Current = Policy(156);
        }
        finally
        {
            release.TrySetResult(new(["{\"secret\":\"late-find-one-canary\"}"], false, false, true, false));
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(changeGeneration
                ? AgentAuditDecisionReason.ValidationRejected : AgentAuditDecisionReason.PolicyRevisionMismatch));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-find-one-canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoFindOneCallerCancellationDiscardsLateResultOrFaultWithoutRetry(bool lateFault)
    {
        var profile = Connection("find-one");
        var policies = new SequencePolicyProvider(Policy(157, FindOneDocumentGrants(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ => { entered.TrySetResult(); return late.Task; }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), audit: audit, find: source);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(157), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            if (lateFault) late.TrySetException(new IOException("late-find-one-canary"));
            else late.TrySetResult(new(["{\"secret\":\"late-find-one-canary\"}"], false, false, true, false));
        }

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-find-one-canary"));
        });
    }

    private static AgentPermissionGrant[] FindOneDocumentGrants(ConnectionProfile profile) =>
        new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible"), LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();

    [Test]
    public async Task GetDocumentUsesLiteralIdReturnsCanonicalDocumentOrNullAndAuditsCount()
    {
        var profile = Connection("document");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        const string bson = "{\"_id\":{\"$numberLong\":\"9007199254740993\"},\"literal\":\"ENV.PRIVATE_CANARY\"}";
        var ids = new[]
        {
            "{\"$oid\":\"507f1f77bcf86cd799439011\"}",
            "\"ENV.PRIVATE_CANARY\"",
            "{\"$numberLong\":\"9007199254740993\"}",
            "{\"$date\":{\"$numberLong\":\"1700000000000\"}}",
            "{\"nested\":{\"$numberInt\":\"7\"}}",
            "{\"$binary\":{\"base64\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"subType\":\"04\"}}"
        };
        foreach (var id in ids)
        {
            var provider = new SequencePolicyProvider(Policy(96, grants));
            var source = new StubFindSource { Page = new([bson], false, false, true, false) };
            var audit = new RecordingAuditRepository();
            var registry = Registry(new StubProfileRepository([profile]), provider,
                new AgentPermissionEvaluator(provider), audit: audit, find: source);
            var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
                collection = "visible", idEjson = id });

            var result = await registry.InvokeAsync(Principal(96), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName, arguments);

            Assert.That(result.Succeeded, Is.True, id);
            Assert.Multiple(() =>
            {
                Assert.That(source.LastByIdQuery!.IdEjson, Is.EqualTo(id));
                Assert.That(source.LastByIdQuery.MaxTimeMs, Is.EqualTo(5_000));
                Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
                Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            });
            using var output = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.That(output.RootElement.GetProperty("documentEjson").GetString(), Is.EqualTo(bson));
        }

        var emptyProvider = new SequencePolicyProvider(Policy(96, grants));
        var emptySource = new StubFindSource { Page = new([], false, false, true, false) };
        var emptyAudit = new RecordingAuditRepository();
        var emptyRegistry = Registry(new StubProfileRepository([profile]), emptyProvider,
            new AgentPermissionEvaluator(emptyProvider), audit: emptyAudit, find: emptySource);
        var emptyArguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", idEjson = "\"missing\"" });
        var missing = await emptyRegistry.InvokeAsync(Principal(96), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName, emptyArguments);

        Assert.That(missing.Succeeded, Is.True);
        using var emptyOutput = JsonDocument.Parse(missing.StructuredContentJson!);
        Assert.That(emptyOutput.RootElement.GetProperty("documentEjson").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(emptyAudit.Events[1].ItemCount, Is.Zero);
    }

    [Test]
    public async Task GetDocumentSuppressesOutputWhenCapturedProfileChangesAfterRead()
    {
        var profile = Connection("document");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(143, grants));
        var profileReads = 0;
        var profiles = new StubProfileRepository
        {
            GetAllHandler = _ => Task.FromResult<IReadOnlyList<ConnectionProfile>>(
                [++profileReads < 3 ? profile : profile with { SourceGenerationId = Guid.NewGuid() }])
        };
        var source = new StubFindSource { Page = new(["{\"_id\":\"captured\",\"secret\":\"canary\"}"],
            false, false, true, false) };
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), find: source);

        var result = await registry.InvokeAsync(Principal(143), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"idEjson\":\"\\\"captured\\\"\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastByIdQuery!.Database, Is.EqualTo("allowed"));
            Assert.That(source.LastByIdQuery.Collection, Is.EqualTo("visible"));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SampleDocumentsSuppressesPageWhenPolicyOrGenerationChangesDuringRead(bool changeGeneration)
    {
        var profile = Connection("sample");
        var grants = SampleDocumentsReadGrants(profile);
        var policies = new MutablePolicyProvider(Policy(151, grants));
        IReadOnlyList<ConnectionProfile> currentProfiles = [profile];
        var repository = new StubProfileRepository
        {
            GetAllHandler = _ => Task.FromResult(currentProfiles)
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ =>
            {
                entered.TrySetResult();
                return release.Task;
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(repository, policies, new AgentPermissionEvaluator(policies),
            audit: audit, find: source);
        var pending = registry.InvokeAsync(Principal(151), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (changeGeneration)
                currentProfiles = [profile with { SourceGenerationId = Guid.NewGuid() }];
            else
                policies.Current = Policy(152);
        }
        finally
        {
            release.TrySetResult(new(["{\"secret\":\"late-sample-canary\"}"], false, false, true, false));
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastQuery?.Database, Is.EqualTo("allowed"));
            Assert.That(source.LastQuery?.Collection, Is.EqualTo("visible"));
            Assert.That(source.LastQuery?.Limit, Is.EqualTo(5));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(changeGeneration
                ? AgentAuditDecisionReason.ValidationRejected : AgentAuditDecisionReason.PolicyRevisionMismatch));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-sample-canary"));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SampleDocumentsCancellationOrDeadlineDiscardsLateCompletionWithoutRetry(bool deadline, bool lateFault)
    {
        var profile = Connection("sample");
        var policies = new SequencePolicyProvider(Policy(153, SampleDocumentsReadGrants(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ =>
            {
                entered.TrySetResult();
                return late.Task;
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), timeout: TimeSpan.FromMilliseconds(deadline ? 250 : 5_000),
            audit: audit, find: source);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(153), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (deadline)
            {
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
            }
            else
            {
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            }
        }
        finally
        {
            if (lateFault) late.TrySetException(new IOException("late-sample-canary"));
            else late.TrySetResult(new(["{\"secret\":\"late-sample-canary\"}"], false, false, true, false));
        }

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(source.LastQuery?.MaxTimeMs, Is.EqualTo(deadline ? 250 : 5_000));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-sample-canary"));
        });
    }

    private static AgentPermissionGrant[] SampleDocumentsReadGrants(ConnectionProfile profile) =>
        new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible"), LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();

    [Test]
    public async Task GetDocumentDeniesMissingGrantAndRejectsCodeDuplicatesAndExtraArguments()
    {
        var profile = Connection("document");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(97, grant));
        var source = new StubFindSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(97), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName,
            prefix + ",\"idEjson\":\"\\\"literal\\\"\"}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var id in new[] { "ObjectId('507f1f77bcf86cd799439011')", "{\"$where\":\"true\"}",
                     "{\"$code\":\"return true\"}", "{\"a\":1,\"a\":2}" })
        {
            var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
                collection = "visible", idEjson = id });
            var invalid = await registry.InvokeAsync(Principal(97), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName, arguments);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), id);
        }
        foreach (var suffix in new[] { ",\"idEjson\":\"\\\"x\\\"\",\"limit\":1}",
                     ",\"idEjson\":\"\\\"x\\\"\",\"approved\":true}",
                     ",\"idEjson\":\"\\\"x\\\"\",\"idEjson\":\"\\\"y\\\"\"}" })
        {
            var invalid = await registry.InvokeAsync(Principal(97), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        var oversized = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", idEjson = "\"" + new string('x', 65_536) + "\"" });
        var oversizedResult = await registry.InvokeAsync(Principal(97), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.GetDocumentToolName, oversized);
        Assert.That(oversizedResult.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task MongoDistinctPreservesLiteralEjsonAndAuditsReturnedValues()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(98, grants));
        var values = new[] { "{\"$numberLong\":\"9007199254740993\"}",
            "{\"$numberDecimal\":\"123.45\"}", "\"ENV.PRIVATE_CANARY\"" };
        var source = new StubDistinctSource { Page = new(values, false, true, false) };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, distinct: source);
        var filter = "{\"literal\":\"ENV.PRIVATE_CANARY\"}";
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", field = "nested.value", filterEjson = filter, maximumValues = 3,
            maxTimeMs = 30_000 });

        var result = await registry.InvokeAsync(Principal(98), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName, arguments);

        Assert.That(result.Succeeded, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(source.LastQuery!.Field, Is.EqualTo("nested.value"));
            Assert.That(source.LastQuery.FilterEjson, Is.EqualTo(filter));
            Assert.That(source.LastQuery.MaximumValues, Is.EqualTo(3));
            Assert.That(source.LastQuery.MaxTimeMs, Is.EqualTo(5_000));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(3));
        });
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("valuesEjson").EnumerateArray()
            .Select(item => item.GetString()), Is.EqualTo(values));
        Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task MongoDistinctDeniesMissingGrantAndRejectsUnsafeFieldFilterAndBounds()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(99, grant));
        var source = new StubDistinctSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), distinct: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(99), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            prefix + ",\"field\":\"nested.value\"}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var field in new[] { "", "$where", "a..b", ".a", "a.", "a\n", "a.$ne" })
        {
            var invalid = await registry.InvokeAsync(Principal(99), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
                JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
                    collection = "visible", field }));
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), field);
        }
        foreach (var suffix in new[] { ",\"field\":\"x\",\"maximumValues\":101}",
                     ",\"field\":\"x\",\"maximumValues\":0}",
                     ",\"field\":\"x\",\"maxTimeMs\":30001}",
                     ",\"field\":\"x\",\"filterEjson\":\"{\\\"$where\\\":\\\"true\\\"}\"}",
                     ",\"field\":\"x\",\"approved\":true}" })
        {
            var invalid = await registry.InvokeAsync(Principal(99), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task MongoDistinctTruncatesAtWholeValueBoundary()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(100, grants));
        var large = JsonSerializer.Serialize(new string('x', 160_000));
        var source = new StubDistinctSource { Page = new([large, large], false, true, false) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), distinct: source);

        var result = await registry.InvokeAsync(Principal(100), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\"}}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("valuesEjson").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(output.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("OutputLimit"));
        });
    }

    [Test]
    public async Task MongoDistinctPreservesValueLimitReasonFromSource()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(140, grants));
        var source = new StubDistinctSource
        {
            Page = new(["\"first\""], true, true, false, AgentMongoDistinctTruncationReason.ValueLimit)
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), distinct: source);

        var result = await registry.InvokeAsync(Principal(140), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\",\"maximumValues\":1}}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("valuesEjson").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(output.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("ValueLimit"));
            Assert.That(source.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task MongoDistinctCallerCancellationStopsWithoutRetry()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(141, grants));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubDistinctSource
        {
            Handler = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new AgentMongoDistinctPage([], false, true, false);
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, distinct: source);
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(141), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\"}}",
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await invocation);
        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(audit.Events.Count(item => item.Outcome == AgentAuditOutcome.Intent), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task MongoDistinctDeadlineReturnsFailureWithoutRetry()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(142, grants));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubDistinctSource
        {
            Handler = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new AgentMongoDistinctPage([], false, true, false);
            }
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), timeout: TimeSpan.FromMilliseconds(300), distinct: source);

        var result = await registry.InvokeAsync(Principal(142), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\"}}");

        Assert.That(started.Task.IsCompleted, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task MongoDistinctSuppressesValuesAfterPolicyRevisionChanges()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var allowed = Policy(101, grants);
        var provider = new SequencePolicyProvider(allowed, allowed, allowed, allowed, Policy(102, grants));
        var source = new StubDistinctSource { Page = new(["\"secret-canary\""], false, true, false) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), distinct: source);

        var result = await registry.InvokeAsync(Principal(101), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastQuery!.MaximumValues, Is.EqualTo(20));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [Test]
    public async Task MongoDistinctSuppressesValuesWhenCapturedProfileChangesAfterSource()
    {
        var profile = Connection("distinct");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(150, grants));
        var profileReads = 0;
        var profiles = new StubProfileRepository
        {
            GetAllHandler = _ => Task.FromResult<IReadOnlyList<ConnectionProfile>>(
                [++profileReads < 3 ? profile : profile with { SourceGenerationId = Guid.NewGuid() }])
        };
        var source = new StubDistinctSource { Page = new(["\"generation-canary\""], false, true, false) };
        var audit = new RecordingAuditRepository();
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), audit: audit,
            distinct: source);

        var result = await registry.InvokeAsync(Principal(150), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoDistinctToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\",\"field\":\"value\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
            Assert.That(audit.Events[^1].ItemCount, Is.Zero);
        });
    }

    [Test]
    public async Task SampleDocumentsRequiresBothGrantsAndClosedBoundedInput()
    {
        var profile = Connection("sample");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(91, grant));
        var source = new StubFindSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(91), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName, prefix + "}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var suffix in new[]
                 { ",\"filterEjson\":\"{}\"}", ",\"skip\":1}", ",\"sortEjson\":\"{}\"}",
                   ",\"maxTimeMs\":1000}", ",\"limit\":21}", ",\"limit\":0}",
                   ",\"projectionEjson\":\"{\\\"x\\\":{\\\"$where\\\":\\\"true\\\"}}\"}",
                   ",\"projectionEjson\":\"{\\\"public\\\":1,\\\"secret\\\":0}\"}" })
        {
            var invalid = await registry.InvokeAsync(Principal(91), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task SampleDocumentsCapsOutputAtWholeDocumentBoundary()
    {
        var profile = Connection("sample");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(92, grants));
        var large = JsonSerializer.Serialize(new { value = new string('x', 160_000) });
        var source = new StubFindSource { Page = new([large, large], false, false, true, false) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);

        var result = await registry.InvokeAsync(Principal(92), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.SampleDocumentsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("documentsEjson").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(output.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("OutputLimit"));
        });
    }

    [Test]
    public async Task MongoFindRequiresBothDocumentGrantsAndPreservesLiteralEjson()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(70, grants));
        var source = new StubFindSource
        {
            Page = new(["{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"value\":{\"$numberLong\":\"9007199254740993\"}}"],
                false, false, true, false)
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, find: source);
        var filter = "{\"name\":\"ENV.PRIVATE_CANARY\",\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"}}";
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", filterEjson = filter, projectionEjson = "{\"_id\":0,\"value\":1}",
            sortEjson = "{\"_id\":-1}", limit = 1 });

        var result = await registry.InvokeAsync(Principal(70), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, arguments);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(source.LastQuery!.FilterEjson, Is.EqualTo(filter));
        Assert.That(source.LastQuery.ProjectionEjson, Is.EqualTo("{\"_id\":0,\"value\":1}"));
        Assert.That(source.LastQuery.SortEjson, Is.EqualTo("{\"_id\":-1}"));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("documentsEjson")[0].GetString(),
                Does.Contain("\"$numberLong\":\"9007199254740993\""));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
            Assert.That(audit.Events[0].Permission, Is.EqualTo(AgentPermission.ReadDocuments));
        });
    }

    [Test]
    public async Task MongoFindDeniesMissingGrantAndMalformedInputsBeforeSource()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var onlyQueryGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries, scope, LocalDestination,
            AgentOutputDataScope.DocumentValues);
        var provider = new SequencePolicyProvider(Policy(71, onlyQueryGrant));
        var source = new StubFindSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"";

        var denied = await registry.InvokeAsync(Principal(71), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, prefix + "}");
        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        foreach (var suffix in new[]
                 { ",\"approved\":true}", ",\"limit\":101}", ",\"skip\":10001}",
                   ",\"maxTimeMs\":30001}", ",\"filterEjson\":\"{\\\"$where\\\":\\\"true\\\"}\"}",
                   ",\"filterEjson\":\"ObjectId('507f1f77bcf86cd799439011')\"}",
                   ",\"projectionEjson\":\"{\\\"public\\\":1,\\\"secret\\\":0}\"}",
                   ",\"database\":\"allowed\"}" })
        {
            var invalid = await registry.InvokeAsync(Principal(71), Context(), LocalDestination,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, prefix + suffix);
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"), suffix);
        }
        Assert.That(source.Calls, Is.Zero);
    }

    [TestCase("t")]
    [TestCase("i")]
    public async Task MongoFindRejectsTimestampOverflowBeforeTheSource(string part)
    {
        var profile = Connection("find");
        var provider = new SequencePolicyProvider(Policy(71));
        var source = new StubFindSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);
        var filter = part == "t" ? "{\"ts\":{\"$timestamp\":{\"t\":4294967296,\"i\":0}}}"
            : "{\"ts\":{\"$timestamp\":{\"t\":0,\"i\":4294967296}}}";
        var arguments = JsonSerializer.Serialize(new { connectionId = profile.Id, database = "allowed",
            collection = "visible", filterEjson = filter });

        var result = await registry.InvokeAsync(Principal(71), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, arguments);

        Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(source.Calls, Is.Zero);
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task MongoFindTruncatesOnlyBetweenDocuments()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(72, grants));
        var large = JsonSerializer.Serialize(new { value = new string('x', 160_000) });
        var source = new StubFindSource { Page = new([large, large], false, false, true, false) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), find: source);

        var result = await registry.InvokeAsync(Principal(72), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("documentsEjson").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(output.RootElement.GetProperty("hasMore").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task MongoFindCallerCancellationDiscardsLateSourceResultWithoutRetry()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(148, grants));
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latePage = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ =>
            {
                sourceStarted.TrySetResult();
                return latePage.Task;
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, find: source);
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(148), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}",
            cancellation.Token);
        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await invocation);
        latePage.TrySetResult(new(["{\"secret\":\"late-caller-canary\"}"], false, false, true, false));
        await latePage.Task;

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(audit.Events.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
        });
    }

    [Test]
    public async Task MongoFindDeadlineDiscardsLateSourceResultWithoutRetry()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(149, grants));
        var sourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latePage = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubFindSource
        {
            FindHandler = _ =>
            {
                sourceStarted.TrySetResult();
                return latePage.Task;
            }
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), timeout: TimeSpan.FromMilliseconds(1_000), find: source);

        var invocation = registry.InvokeAsync(Principal(149), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");
        await sourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await invocation.WaitAsync(TimeSpan.FromSeconds(3));
        latePage.TrySetResult(new(["{\"secret\":\"late-deadline-canary\"}"], false, false, true, false));
        await latePage.Task;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.LastCancellationToken.IsCancellationRequested, Is.True);
        });
    }

    [Test]
    public async Task MongoFindSuppressesDocumentWhenProfileChangesAfterRead()
    {
        var profile = Connection("find");
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "allowed", "visible");
        var grants = new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
                profile.SourceGenerationId!.Value, permission, scope, LocalDestination,
                AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(73, grants));
        var reads = 0;
        var repository = new StubProfileRepository
        {
            GetAllHandler = _ => Task.FromResult<IReadOnlyList<ConnectionProfile>>(
                [++reads < 3 ? profile : profile with { SourceGenerationId = Guid.NewGuid() }])
        };
        var source = new StubFindSource { Page = new(["{\"secret\":\"canary\"}"], false, false, true, false) };
        var registry = Registry(repository, provider, new AgentPermissionEvaluator(provider), find: source);

        var result = await registry.InvokeAsync(Principal(73), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"allowed\",\"collection\":\"visible\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [Test]
    public async Task MetadataNamesRequireExactNamespaceGrantAndExternalDestination()
    {
        var profile = Connection("profile canary");
        var destination = AgentOutputDestination.ProviderExternal("provider-a");
        var context = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForDatabase(profile.Id, "allowed"), destination, Metadata);
        var provider = new SequencePolicyProvider(Policy(60, grant));
        var metadata = new StubMetadataSource { Databases = ["allowed", "hidden"] };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, metadata: metadata);

        var result = await registry.InvokeAsync(Principal(60), context, destination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()),
            Is.EqualTo(AllowedDatabaseName));
        Assert.That(result.StructuredContentJson, Does.Not.Contain("hidden"));
        Assert.That(metadata.DatabaseCalls, Is.EqualTo(1));
        Assert.That(audit.Events.Select(x => x.ToolName), Is.EqualTo(DatabaseAuditToolNames));
        Assert.That(audit.Events.All(x => x.ConnectionId == profile.Id), Is.True);
    }

    [Test]
    public async Task CollectionsFilterByCollectionGrantAndKeepDatabaseOutOfAudit()
    {
        var profile = Connection("profile");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForCollection(profile.Id, "db", "visible"), LocalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(61, grant));
        var metadata = new StubMetadataSource { Collections = [new("visible", CollectionKind.Unknown), new("private", CollectionKind.Unknown)] };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, metadata: metadata);

        var result = await registry.InvokeAsync(Principal(61), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName, $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()),
            Is.EqualTo(VisibleCollectionName));
        Assert.That(metadata.CollectionCalls, Is.EqualTo(1));
        Assert.That(audit.Events.All(x => x.NamespaceKind == AgentAuditNamespaceKind.Pseudonym &&
            x.DatabaseName is null && x.CollectionName is null), Is.True);
    }

    [Test]
    public async Task MetadataDeniesBeforeSourceWithoutUsableGrantAndRejectsExtraArguments()
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(62));
        var metadata = new StubMetadataSource { Databases = ["db"] };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var arguments = $"{{\"connectionId\":\"{profile.Id:D}\"}}";

        var denied = await registry.InvokeAsync(Principal(62), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, arguments);
        var invalid = await registry.InvokeAsync(Principal(62), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, arguments[..^1] + ",\"extra\":1}");

        Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(metadata.DatabaseCalls, Is.Zero);
    }

    [TestCase(AgentToolRegistry.ListDatabasesToolName)]
    [TestCase(AgentToolRegistry.ListCollectionsToolName)]
    public async Task MetadataRevocationDuringProfileReadDeniesBeforeSource(string toolName)
    {
        var profile = Connection("profile");
        var provider = new MutablePolicyProvider(Policy(62, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new SequenceProfileRepository([profile], [profile])
        {
            InitialReadHandler = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return [profile];
            }
        };
        var metadata = new StubMetadataSource
        {
            Databases = ["db"], Collections = [new("items", CollectionKind.Unknown)]
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider),
            audit: audit, metadata: metadata);
        var arguments = toolName == AgentToolRegistry.ListDatabasesToolName
            ? $"{{\"connectionId\":\"{profile.Id:D}\"}}"
            : $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}";

        var pending = registry.InvokeAsync(Principal(62), Context(), LocalDestination, Metadata,
            toolName, arguments);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            provider.Current = Policy(63);
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(metadata.BoundedDatabaseCalls + metadata.BoundedCollectionCalls, Is.Zero,
                "Revocation observed before dispatch must prevent enumeration, not merely suppress its output.");
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PolicyRevisionMismatch));
        });
    }

    [TestCase(AgentToolRegistry.ListDatabasesToolName)]
    [TestCase(AgentToolRegistry.ListCollectionsToolName)]
    public async Task MetadataSuppressesOutputWhenProfileChangesOrSourceFails(string toolName)
    {
        var profile = Connection("profile");
        var changed = profile with { SourceGenerationId = Guid.NewGuid() };
        var grant = Grant(profile);
        var provider = new SequencePolicyProvider(Policy(63, grant));
        var metadata = new StubMetadataSource
        {
            Databases = ["db"], Collections = [new("items", CollectionKind.Unknown)]
        };
        var registry = Registry(new SequenceProfileRepository([profile], [changed]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var arguments = toolName == AgentToolRegistry.ListDatabasesToolName
            ? $"{{\"connectionId\":\"{profile.Id:D}\"}}"
            : $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}";

        var result = await registry.InvokeAsync(Principal(63), Context(), LocalDestination, Metadata,
            toolName, arguments);
        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(metadata.BoundedDatabaseCalls + metadata.BoundedCollectionCalls, Is.Zero);

        metadata = new StubMetadataSource { Fail = true };
        provider = new SequencePolicyProvider(Policy(63, grant));
        registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        result = await registry.InvokeAsync(Principal(63), Context(), LocalDestination, Metadata,
            toolName, arguments);
        Assert.That(result.ErrorCode, Is.EqualTo("ExecutionFailed"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task MetadataTruncatesAfterTwoHundredAuthorizedNames()
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(64, Grant(profile)));
        var metadata = new StubMetadataSource { Databases = Enumerable.Range(0, 201).Select(i => $"db{i}").ToArray() };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var result = await registry.InvokeAsync(Principal(64), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");
        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("names").GetArrayLength(), Is.EqualTo(200));
        Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
    }

    [Test]
    public async Task MetadataDeadlineAndCallerCancellationCloseAuditWithoutOutput()
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(65, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new StubMetadataSource
        {
            DatabaseHandler = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return [];
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(20), audit, metadata);
        var args = $"{{\"connectionId\":\"{profile.Id:D}\"}}";

        var timedOut = await registry.InvokeAsync(Principal(65), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, args);
        Assert.That(timedOut.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        Assert.That(timedOut.StructuredContentJson, Is.Null);
        Assert.That(audit.Events.Select(x => x.Outcome),
            Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));

        provider = new SequencePolicyProvider(Policy(65, Grant(profile)));
        audit = new RecordingAuditRepository();
        registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, metadata: metadata);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(65), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, args, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(audit.Events.Select(x => x.Outcome),
            Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DatabaseCancellationOrDeadlineSuppressesLateCompletionWithoutRetry(bool deadline, bool lateFault)
    {
        var profile = Connection("profile");
        var policies = new SequencePolicyProvider(Policy(164, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new StubMetadataSource
        {
            DatabaseHandler = _ => { entered.TrySetResult(); return release.Task; }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), deadline ? TimeSpan.FromMilliseconds(50) : null,
            audit, metadata);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(164), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\"}}",
            deadline ? CancellationToken.None : cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AgentToolInvocationResult? result = null;
        if (deadline)
        {
            result = await pending;
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        }
        else
        {
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        }

        if (lateFault) release.TrySetException(new IOException("late database source secret"));
        else release.TrySetResult(["late-database-canary"]);
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(result?.StructuredContentJson, Is.Null);
            Assert.That(metadata.BoundedDatabaseCalls, Is.EqualTo(1));
            Assert.That(audit.Events.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("canary"));
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("secret"));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CollectionCancellationOrDeadlineSuppressesLateCompletionWithoutRetry(bool deadline, bool lateFault)
    {
        var profile = Connection("profile");
        var policies = new SequencePolicyProvider(Policy(165, Grant(profile)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<CollectionEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new StubMetadataSource
        {
            CollectionHandler = _ => { entered.TrySetResult(); return release.Task; }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), deadline ? TimeSpan.FromMilliseconds(50) : null,
            audit, metadata);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(165), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}",
            deadline ? CancellationToken.None : cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AgentToolInvocationResult? result = null;
        if (deadline)
        {
            result = await pending;
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        }
        else
        {
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        }

        if (lateFault) release.TrySetException(new IOException("late collection source secret"));
        else release.TrySetResult([new("late-collection-canary", CollectionKind.Unknown)]);
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(result?.StructuredContentJson, Is.Null);
            Assert.That(metadata.BoundedCollectionCalls, Is.EqualTo(1));
            Assert.That(audit.Events.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("canary"));
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("secret"));
        });
    }

    [Test]
    public async Task MetadataRevokedPolicySuppressesNamesAfterSourceRead()
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(66, Grant(profile)), Policy(66, Grant(profile)),
            Policy(67, Grant(profile)));
        var metadata = new StubMetadataSource { Databases = ["db"] };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);

        var result = await registry.InvokeAsync(Principal(66), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(metadata.DatabaseCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task MetadataOverflowFromBoundedSourceFailsClosed()
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(68, Grant(profile)));
        var metadata = new StubMetadataSource { Databases = ["db"], Overflow = true };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);

        var result = await registry.InvokeAsync(Principal(68), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");

        Assert.That(result.ErrorCode, Is.EqualTo("ResultTooLarge"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(metadata.BoundedDatabaseCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task DatabasePaginationSortsAndSkipsOnlyAuthorizedNames()
    {
        var profile = Connection("profile");
        var allowed = Enumerable.Range(1, 201).Select(i => $"db{i:D3}").ToArray();
        var grants = allowed.Select(database => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForDatabase(profile.Id, database),
            LocalDestination, Metadata)).ToArray();
        var provider = new SequencePolicyProvider(Policy(69, grants));
        var metadata = new StubMetadataSource { Databases = allowed.Reverse().Append("db000").ToArray() };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var first = await registry.InvokeAsync(Principal(69), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");
        var second = await registry.InvokeAsync(Principal(69), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\",\"skip\":200}}");

        Assert.That(first.Succeeded && second.Succeeded, Is.True);
        using var firstJson = JsonDocument.Parse(first.StructuredContentJson!);
        using var secondJson = JsonDocument.Parse(second.StructuredContentJson!);
        var firstNames = firstJson.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()).ToArray();
        var secondNames = secondJson.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.That(firstNames, Is.EqualTo(allowed.Take(200)));
        Assert.That(secondNames, Is.EqualTo(allowed.Skip(200)));
        Assert.That(firstJson.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(secondJson.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
        Assert.That(firstNames.Intersect(secondNames), Is.Empty);
        Assert.That(metadata.BoundedDatabaseCalls, Is.EqualTo(2));
        Assert.That(metadata.CollectionCalls + metadata.BoundedCollectionCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DatabasesAnswerEmptyCatalogOrDenyMissingConnectionWithoutEnumeratingCollections(bool profileExists)
    {
        var profile = Connection("profile");
        var policies = new SequencePolicyProvider(Policy(161, Grant(profile)));
        var metadata = new StubMetadataSource();
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository(profileExists ? [profile] : []), policies,
            new AgentPermissionEvaluator(policies), audit: audit, metadata: metadata);

        var result = await registry.InvokeAsync(Principal(161), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.EqualTo(profileExists));
            Assert.That(result.ErrorCode, Is.EqualTo(profileExists ? null : "PermissionDenied"));
            Assert.That(metadata.BoundedDatabaseCalls, Is.EqualTo(profileExists ? 1 : 0));
            Assert.That(metadata.CollectionCalls + metadata.BoundedCollectionCalls, Is.Zero);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
        });
        if (profileExists)
        {
            using var json = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.That(json.RootElement.GetProperty("names").GetArrayLength(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
        }
        else Assert.That(result.StructuredContentJson, Is.Null);
    }

    [TestCase("generation", false)]
    [TestCase("connection-string", false)]
    [TestCase("policy", false)]
    [TestCase("policy", true)]
    public async Task DatabasesSuppressCatalogWhenProfileOrPolicyChangesDuringEnumeration(string change, bool emptyCatalog)
    {
        var profile = Connection("profile");
        var policies = new MutablePolicyProvider(Policy(162, Grant(profile)));
        IReadOnlyList<ConnectionProfile> currentProfiles = [profile];
        var repository = new StubProfileRepository { GetAllHandler = _ => Task.FromResult(currentProfiles) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new StubMetadataSource
        {
            DatabaseHandler = _ => { entered.TrySetResult(); return release.Task; }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(repository, policies, new AgentPermissionEvaluator(policies), audit: audit, metadata: metadata);
        var pending = registry.InvokeAsync(Principal(162), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName, $"{{\"connectionId\":\"{profile.Id:D}\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "policy") policies.Current = Policy(163);
            else currentProfiles = [change == "generation"
                ? profile with { SourceGenerationId = Guid.NewGuid() }
                : profile with { ConnectionString = "mongodb://changed-database-profile-canary:27018" }];
        }
        finally
        {
            release.TrySetResult(emptyCatalog ? [] : ["late-database-canary"]);
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(metadata.BoundedDatabaseCalls, Is.EqualTo(1));
            Assert.That(metadata.CollectionCalls + metadata.BoundedCollectionCalls, Is.Zero);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(change == "policy"
                ? AgentAuditDecisionReason.PolicyRevisionMismatch : AgentAuditDecisionReason.ValidationRejected));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("canary"));
        });
    }

    [Test]
    public async Task CollectionPaginationReturnsSecondPageWithoutRepeatingNames()
    {
        var profile = Connection("profile");
        var names = Enumerable.Range(0, 201).Select(i => $"coll{i:D3}").ToArray();
        var provider = new SequencePolicyProvider(Policy(70, Grant(profile)));
        var metadata = new StubMetadataSource
        {
            Collections = names.Reverse().Select(item => new CollectionEntry(item, CollectionKind.Unknown)).ToArray()
        };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var first = await registry.InvokeAsync(Principal(70), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}");
        var second = await registry.InvokeAsync(Principal(70), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"skip\":200}}");

        Assert.That(first.Succeeded && second.Succeeded, Is.True);
        using var firstJson = JsonDocument.Parse(first.StructuredContentJson!);
        using var secondJson = JsonDocument.Parse(second.StructuredContentJson!);
        Assert.That(firstJson.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()),
            Is.EqualTo(names.Take(200)));
        Assert.That(secondJson.RootElement.GetProperty("names").EnumerateArray().Select(x => x.GetString()),
            Is.EqualTo(names.Skip(200)));
        Assert.That(firstJson.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(secondJson.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [TestCase("empty-db", true, null)]
    [TestCase("", true, "InvalidArguments")]
    [TestCase("../outside", true, "InvalidArguments")]
    [TestCase("db\n", true, "InvalidArguments")]
    [TestCase("empty-db", false, "PermissionDenied")]
    public async Task CollectionsAnswerEmptyDatabaseOrRejectInvalidTargetWithoutDocumentReads(
        string database, bool profileExists, string? expectedError)
    {
        var profile = Connection("profile");
        var policies = new SequencePolicyProvider(Policy(158, Grant(profile)));
        var metadata = new StubMetadataSource();
        var documents = new StubFindSource();
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository(profileExists ? [profile] : []), policies,
            new AgentPermissionEvaluator(policies), audit: audit, metadata: metadata, find: documents);

        var result = await registry.InvokeAsync(Principal(158), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName,
            JsonSerializer.Serialize(new { connectionId = profile.Id, database }));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            Assert.That(result.Succeeded, Is.EqualTo(expectedError is null));
            Assert.That(metadata.BoundedCollectionCalls, Is.EqualTo(expectedError is null ? 1 : 0));
            Assert.That(metadata.DatabaseCalls + metadata.BoundedDatabaseCalls, Is.Zero);
            Assert.That(documents.Calls, Is.Zero);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
        });
        if (expectedError is null)
        {
            using var json = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.That(json.RootElement.GetProperty("names").GetArrayLength(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
        }
        else Assert.That(result.StructuredContentJson, Is.Null);
    }

    [TestCase("generation")]
    [TestCase("connection-string")]
    [TestCase("policy")]
    public async Task CollectionsSuppressNamesWhenProfileOrPolicyChangesDuringEnumeration(string change)
    {
        var profile = Connection("profile");
        var policies = new MutablePolicyProvider(Policy(159, Grant(profile)));
        IReadOnlyList<ConnectionProfile> currentProfiles = [profile];
        var repository = new StubProfileRepository { GetAllHandler = _ => Task.FromResult(currentProfiles) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<CollectionEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new StubMetadataSource
        {
            CollectionHandler = _ => { entered.TrySetResult(); return release.Task; }
        };
        var documents = new StubFindSource();
        var audit = new RecordingAuditRepository();
        var registry = Registry(repository, policies, new AgentPermissionEvaluator(policies),
            audit: audit, metadata: metadata, find: documents);
        var pending = registry.InvokeAsync(Principal(159), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListCollectionsToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "policy") policies.Current = Policy(160);
            else currentProfiles = [change == "generation"
                ? profile with { SourceGenerationId = Guid.NewGuid() }
                : profile with { ConnectionString = "mongodb://changed-profile-canary:27018" }];
        }
        finally
        {
            release.TrySetResult([new("late-collection-canary", CollectionKind.Unknown)]);
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(metadata.BoundedCollectionCalls, Is.EqualTo(1));
            Assert.That(metadata.DatabaseCalls + metadata.BoundedDatabaseCalls, Is.Zero);
            Assert.That(documents.Calls, Is.Zero);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(change == "policy"
                ? AgentAuditDecisionReason.PolicyRevisionMismatch : AgentAuditDecisionReason.ValidationRejected));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("canary"));
        });
    }

    [TestCase("-1")]
    [TestCase("10001")]
    [TestCase("1.5")]
    [TestCase("\"1\"")]
    public async Task MetadataRejectsInvalidSkipWithoutReadingSource(string value)
    {
        var profile = Connection("profile");
        var provider = new SequencePolicyProvider(Policy(71, Grant(profile)));
        var metadata = new StubMetadataSource { Databases = ["db"] };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), metadata: metadata);
        var result = await registry.InvokeAsync(Principal(71), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListDatabasesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"skip\":{value}}}");
        Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(metadata.DatabaseCalls, Is.Zero);
    }

    [Test]
    public async Task GetIndexesProjectsMetadataAndWritesDurableOutcome()
    {
        var profile = Connection("indexes");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForCollection(profile.Id, "db", "items"), LocalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(95, grant));
        var audit = new RecordingAuditRepository();
        var source = new StubIndexSource { Page = new([
            new AgentMongoIndexSummary("partial_1", ["a"], true, false, false)
            { KeyDirections = ["1"], TtlSeconds = 3600, PartialFilterFields = ["status"] }
        ], false, true) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, indexes: source);

        var result = await registry.InvokeAsync(Principal(95), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.That(result.Succeeded, Is.True);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var index = json.RootElement.GetProperty("indexes")[0];
        Assert.Multiple(() =>
        {
            Assert.That(index.EnumerateObject().Select(item => item.Name), Is.EquivalentTo(IndexSummaryPropertyNames));
            Assert.That(index.GetProperty("keyFields")[0].GetString(), Is.EqualTo("a"));
            Assert.That(index.GetProperty("keyDirections")[0].GetString(), Is.EqualTo("1"));
            Assert.That(index.GetProperty("ttlSeconds").GetInt64(), Is.EqualTo(3600));
            Assert.That(index.GetProperty("partialFilterFields")[0].GetString(), Is.EqualTo("status"));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(source.MaximumExecutionTime, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public async Task GetIndexesRequiresCollectionMetadataGrantAndClosedInput()
    {
        var profile = Connection("indexes");
        var provider = new SequencePolicyProvider(Policy(96));
        var source = new StubIndexSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), indexes: source);
        var input = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}";

        var denied = await registry.InvokeAsync(Principal(96), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName, input);
        var invalid = await registry.InvokeAsync(Principal(96), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName, input[..^1] + ",\"approved\":true}");

        Assert.Multiple(() =>
        {
            Assert.That(denied.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(source.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task GetIndexesSuppressesOutputWhenProfileChangesAfterRead()
    {
        var profile = Connection("indexes");
        var changed = profile with { Name = "changed" };
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForCollection(profile.Id, "db", "items"), LocalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(97, grant));
        var source = new StubIndexSource { Page = new([new("_id_", ["_id"], false, false, false)], false, true) };
        var profiles = new SequenceProfileRepository([profile], [changed]);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), indexes: source);

        var result = await registry.InvokeAsync(Principal(97), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task GetIndexesBoundsSerializedOutput()
    {
        var profile = Connection("indexes");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForCollection(profile.Id, "db", "items"), LocalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(98, grant));
        var longField = new string('x', 1_000);
        var source = new StubIndexSource { Page = new(Enumerable.Range(0, 200)
            .Select(number => new AgentMongoIndexSummary("idx" + number,
                Enumerable.Repeat(longField, 16).ToArray(), false, false, false)).ToArray(), false, true) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), indexes: source);

        var result = await registry.InvokeAsync(Principal(98), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(json.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("OutputLimit"));
    }

    [TestCase("mongodb://${HOST}:27017")]
    [TestCase("mongodb://${ENV.get('HOST')}:27017")]
    public async Task DynamicMongoTargetIsDeniedAcrossReadHandlersBeforePermissionEvaluation(string uri)
    {
        var profile = Connection("dynamic") with { ConnectionString = uri };
        var provider = new SequencePolicyProvider(Policy(99));
        var permissions = new RejectEvaluation();
        var metadata = new StubMetadataSource();
        var find = new StubFindSource();
        var count = new StubCountSource();
        var distinct = new StubDistinctSource();
        var indexes = new StubIndexSource();
        var explain = new StubExplainSource();
        var registry = new AgentToolRegistry(new StubProfileRepository([profile]), provider, permissions,
            new RecordingAuditRepository(), metadata: metadata, schemaSamplingConsent: new AllowSchemaConsent(),
            find: find, count: count, distinct: distinct, indexes: indexes, explain: explain, exposure: AllReadStages,
            principalAuthority: new TestAgentPrincipalAuthority());
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\"";
        var database = prefix + ",\"database\":\"db\"}";
        var collection = prefix + ",\"database\":\"db\",\"collection\":\"items\"}";
        var distinctInput = prefix + ",\"database\":\"db\",\"collection\":\"items\",\"field\":\"a\"}";
        var cases = new (string Name, string Arguments, AgentOutputDataScope Scope)[]
        {
            (AgentToolRegistry.ListDatabasesToolName, prefix + "}", Metadata),
            (AgentToolRegistry.ListCollectionsToolName, database, Metadata),
            (AgentToolRegistry.GetCollectionSchemaToolName, collection, AgentOutputDataScope.Schema),
            (AgentToolRegistry.MongoFindToolName, collection, AgentOutputDataScope.DocumentValues),
            (AgentToolRegistry.MongoCountToolName, collection, AgentOutputDataScope.DocumentValues),
            (AgentToolRegistry.MongoDistinctToolName, distinctInput, AgentOutputDataScope.DocumentValues),
            (AgentToolRegistry.GetIndexesToolName, collection, Metadata),
            (AgentToolRegistry.MongoExplainToolName, collection, AgentOutputDataScope.DocumentValues)
        };
        foreach (var (name, arguments, scope) in cases)
        {
            var result = await registry.InvokeAsync(Principal(99), Context(), LocalDestination, scope,
                name, arguments);
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"), name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(permissions.Calls, Is.Zero);
            Assert.That(metadata.BoundedDatabaseCalls + metadata.BoundedCollectionCalls, Is.Zero);
            Assert.That(find.Calls + count.Calls + distinct.Calls + indexes.Calls + explain.Calls, Is.Zero);
        });
    }

    private sealed class RejectEvaluation : IAgentPermissionEvaluator
    {
        public int Calls { get; private set; }
        public Task<AgentPermissionDecision> EvaluateAsync(AgentPermissionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new AssertionException("Dynamic target reached permission evaluation.");
        }
    }

    private sealed class AllowSchemaConsent : IAgentSchemaSamplingConsentProvider
    {
        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubIndexSource : IAgentMongoIndexSource
    {
        public AgentMongoIndexPage Page { get; init; } = new([], false, true);
        public int Calls { get; private set; }
        public TimeSpan? MaximumExecutionTime { get; private set; }
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken)
        {
            Calls++;
            MaximumExecutionTime = maximumExecutionTime;
            return Task.FromResult(Page);
        }
    }

    [Test]
    public async Task MongoExplainRequiresThreeGrantsAndAuditsSanitizedPlan()
    {
        var profile = Connection("explain");
        var grants = new[] { AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries,
                AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
                permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
                LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(100, grants));
        var audit = new RecordingAuditRepository();
        var source = new StubExplainSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, explain: source);
        var result = await registry.InvokeAsync(Principal(100), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\",\"maxTimeMs\":1200}}");

        Assert.That(result.Succeeded, Is.True);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("verbosity").GetString(), Is.EqualTo("queryPlanner"));
            Assert.That(source.LastQuery?.MaxTimeMs, Is.EqualTo(1200));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("private-canary"));
        });
    }

    [TestCase(AgentPermission.ReadDiagnostics)]
    [TestCase(AgentPermission.ExecuteReadQueries)]
    [TestCase(AgentPermission.ReadDocuments)]
    public async Task MongoExplainDeniesWhenAnyGrantIsMissing(AgentPermission missing)
    {
        var profile = Connection("explain");
        var grants = new[] { AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries,
                AgentPermission.ReadDocuments }.Where(permission => permission != missing)
            .Select(permission => new AgentPermissionGrant(PrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
                permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
                LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(101, grants));
        var source = new StubExplainSource();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), explain: source);
        var result = await registry.InvokeAsync(Principal(101), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task MongoExplainRejectsVerbosityOverrideAndUnsafePlan()
    {
        var profile = Connection("explain");
        var grants = new[] { AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries,
                AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
                permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
                LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(102, grants));
        var source = new StubExplainSource { Result = new("{\"parsedQuery\":{\"password\":\"private-canary\"}}", true, false) };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), explain: source);
        var prefix = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"";
        var overrideResult = await registry.InvokeAsync(Principal(102), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            prefix + ",\"verbosity\":\"executionStats\"}");
        var codeFilter = await registry.InvokeAsync(Principal(102), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            JsonSerializer.Serialize(new { connectionId = profile.Id, database = "db", collection = "items",
                filterEjson = "{\"$where\":\"return true\"}" }));
        var constructorFilter = await registry.InvokeAsync(Principal(102), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            JsonSerializer.Serialize(new { connectionId = profile.Id, database = "db", collection = "items",
                filterEjson = "ObjectId('507f1f77bcf86cd799439011')" }));
        var unsafeResult = await registry.InvokeAsync(Principal(102), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName, prefix + "}");

        Assert.Multiple(() =>
        {
            Assert.That(overrideResult.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(codeFilter.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(constructorFilter.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(unsafeResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(unsafeResult.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task MongoExplainServerPlanFailureIsSanitizedExecutionFailure()
    {
        var profile = Connection("explain");
        var grants = new[] { AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries,
                AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(PrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
                permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
                LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var provider = new SequencePolicyProvider(Policy(103, grants));
        var audit = new RecordingAuditRepository();
        var source = new StubExplainSource { Failure = new InvalidDataException("private-canary") };
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, explain: source);
        var result = await registry.InvokeAsync(Principal(103), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("ExecutionFailed"));
            Assert.That(result.ErrorCode, Is.Not.EqualTo("InvalidArguments"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ExecutionFailed));
            Assert.That(string.Join(" ", audit.Events), Does.Not.Contain("private-canary"));
        });
    }

    [TestCase("source-flag")]
    [TestCase("plan-bytes")]
    [TestCase("envelope-bytes")]
    public async Task MongoExplainReportsEveryOutputOverflowAsLimitExceeded(string overflow)
    {
        var plan = "{\"stage\":\"IXSCAN\"}";
        if (overflow == "plan-bytes")
            plan += new string(' ', 262_144);
        else if (overflow == "envelope-bytes")
        {
            // Valid index names stay within 1024 UTF-8 bytes; escaping the response string expands the output.
            var node = "{\"stage\":\"IXSCAN\",\"indexName\":\"" + new string('界', 341) + "\"}";
            plan = "{\"stage\":\"OR\",\"inputStages\":[" + string.Join(',', Enumerable.Repeat(node, 180)) + "]}";
            Assert.That(Encoding.UTF8.GetByteCount(plan), Is.LessThan(262_144));
            Assert.That(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { planEjson = plan, verbosity = "queryPlanner" })),
                Is.GreaterThan(262_144), "The fixture crosses the response budget only after JSON string escaping.");
        }

        var profile = Connection("explain");
        var grants = ExplainPermissions.Select(permission => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
            LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var policies = new SequencePolicyProvider(Policy(105, grants));
        var audit = new RecordingAuditRepository();
        var source = new StubExplainSource { Result = new(plan, true, overflow == "source-flag") };
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), audit: audit, explain: source);
        var result = await registry.InvokeAsync(Principal(105), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("ResultTooLarge"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1), "Overflow must not retry the explain operation.");
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.LimitExceeded));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoExplainSuppressesPlanWhenPolicyOrGenerationChangesDuringRead(bool changeGeneration)
    {
        var profile = Connection("explain");
        var grants = ExplainPermissions.Select(permission => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
            LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var policies = new MutablePolicyProvider(Policy(106, grants));
        IReadOnlyList<ConnectionProfile> currentProfiles = [profile];
        var profiles = new StubProfileRepository([profile])
        {
            GetAllHandler = _ => Task.FromResult(currentProfiles)
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new StubExplainSource
        {
            Handler = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return new("{\"stage\":\"IXSCAN\",\"indexName\":\"late-plan-canary\"}", true, false);
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(profiles, policies, new AgentPermissionEvaluator(policies),
            audit: audit, explain: source);
        var pending = registry.InvokeAsync(Principal(106), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (changeGeneration)
                currentProfiles = [profile with { SourceGenerationId = Guid.NewGuid() }];
            else
                policies.Current = Policy(107);
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await pending;

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1), "Scope invalidation must not replay explain.");
            Assert.That(source.LastQuery?.Database, Is.EqualTo("db"));
            Assert.That(source.LastQuery?.Collection, Is.EqualTo("items"));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(changeGeneration
                ? AgentAuditDecisionReason.ValidationRejected : AgentAuditDecisionReason.PolicyRevisionMismatch));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-plan-canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoExplainCancellationDoesNotPublishOrRetryLateResult(bool lateFault)
    {
        var profile = Connection("explain");
        var grants = ExplainPermissions.Select(permission => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
            LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var policies = new SequencePolicyProvider(Policy(108, grants));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<AgentMongoExplainResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceToken = CancellationToken.None;
        var source = new StubExplainSource
        {
            Handler = async token =>
            {
                sourceToken = token;
                entered.TrySetResult();
                try { return await late.Task; }
                finally { completed.TrySetResult(); }
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), audit: audit, explain: source);
        using var cancellation = new CancellationTokenSource();
        var pending = registry.InvokeAsync(Principal(108), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}", cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            if (lateFault)
                late.TrySetException(new IOException("late-plan-canary"));
            else
                late.TrySetResult(new("{\"stage\":\"IXSCAN\",\"indexName\":\"late-plan-canary\"}", true, false));
        }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(sourceToken.IsCancellationRequested, Is.True);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(audit.Events, Has.Count.EqualTo(2), "A late completion must not append another outcome.");
            Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-plan-canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoExplainExecutionDeadlineDoesNotPublishOrRetryLateResult(bool lateFault)
    {
        var profile = Connection("explain");
        var grants = ExplainPermissions.Select(permission => new AgentPermissionGrant(PrincipalId,
            AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            permission, AgentNamespaceScope.ForCollection(profile.Id, "db", "items"),
            LocalDestination, AgentOutputDataScope.DocumentValues)).ToArray();
        var policies = new SequencePolicyProvider(Policy(109, grants));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<AgentMongoExplainResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceToken = CancellationToken.None;
        var source = new StubExplainSource
        {
            Handler = async token =>
            {
                sourceToken = token;
                entered.TrySetResult();
                try { return await late.Task; }
                finally { completed.TrySetResult(); }
            }
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), policies,
            new AgentPermissionEvaluator(policies), audit: audit, explain: source,
            timeout: TimeSpan.FromMilliseconds(250));
        var pending = registry.InvokeAsync(Principal(109), Context(), LocalDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoExplainToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
            });
        }
        finally
        {
            if (lateFault)
                late.TrySetException(new IOException("late-plan-canary"));
            else
                late.TrySetResult(new("{\"stage\":\"IXSCAN\",\"indexName\":\"late-plan-canary\"}", true, false));
        }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(sourceToken.IsCancellationRequested, Is.True);
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(audit.Events, Has.Count.EqualTo(2), "A late completion must not append another outcome.");
            Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(audit.Events[1].ItemCount, Is.Zero);
            Assert.That(audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(audit.Events), Does.Not.Contain("late-plan-canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetIndexesPreservesSourceProjectionTruncation(bool projectionTruncated)
    {
        var profile = Connection("indexes");
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForCollection(profile.Id, "db", "items"), LocalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(96, grant));
        var source = new StubIndexSource
        {
            Page = new([new AgentMongoIndexSummary("partial_1", ["a"], false, false, false)
                { PartialFilterFields = ["status"] }], projectionTruncated, true)
        };
        var audit = new RecordingAuditRepository();
        var registry = Registry(new StubProfileRepository([profile]), provider,
            new AgentPermissionEvaluator(provider), audit: audit, indexes: source);

        var result = await registry.InvokeAsync(Principal(96), Context(), LocalDestination, Metadata,
            AgentToolRegistry.GetIndexesToolName,
            $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.EqualTo(projectionTruncated));
            Assert.That(json.RootElement.GetProperty("indexes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.RootElement.TryGetProperty("truncationReason", out _), Is.False,
                "Source truncation must not be misreported as registry OutputLimit.");
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[1].ItemCount, Is.EqualTo(1));
        });
    }

    private sealed class StubExplainSource : IAgentMongoExplainSource
    {
        public AgentMongoExplainResult Result { get; init; } = new("{\"stage\":\"IXSCAN\"}", true, false);
        public Exception? Failure { get; init; }
        public Func<CancellationToken, Task<AgentMongoExplainResult>>? Handler { get; init; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public int Calls { get; private set; }
        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile,
            AgentMongoFindQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            if (Handler is { } handler) return handler(cancellationToken);
            if (Failure is { } failure) throw failure;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubMetadataSource : IMongoMetadataSource
    {
        public IReadOnlyList<string> Databases { get; init; } = [];
        public IReadOnlyList<CollectionEntry> Collections { get; init; } = [];
        public bool Fail { get; init; }
        public bool Overflow { get; init; }
        public Func<CancellationToken, Task<IReadOnlyList<string>>>? DatabaseHandler { get; init; }
        public Func<CancellationToken, Task<IReadOnlyList<CollectionEntry>>>? CollectionHandler { get; init; }
        public int DatabaseCalls { get; private set; }
        public int CollectionCalls { get; private set; }
        public int BoundedDatabaseCalls { get; private set; }
        public int BoundedCollectionCalls { get; private set; }
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            DatabaseCalls++;
            if (DatabaseHandler is { } handler) return handler(cancellationToken);
            if (Fail) throw new IOException("source secret");
            return Task.FromResult(Databases);
        }
        public async Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken)
        {
            BoundedDatabaseCalls++;
            var items = await ListDatabaseNamesAsync(profile, cancellationToken);
            return new(items.Take(maximum).ToArray(), Overflow || items.Count > maximum);
        }
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken)
        {
            CollectionCalls++;
            if (CollectionHandler is { } handler) return handler(cancellationToken);
            if (Fail) throw new IOException("source secret");
            return Task.FromResult(Collections);
        }
        public async Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken)
        {
            BoundedCollectionCalls++;
            var items = await ListCollectionNamesAsync(profile, database, cancellationToken);
            return new(items.Take(maximum).ToArray(), Overflow || items.Count > maximum);
        }
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubFindSource : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public AgentMongoFindByIdQuery? LastByIdQuery { get; private set; }
        public AgentMongoFindPage Page { get; init; } = new([], false, false, true, false);
        public Func<CancellationToken, Task<AgentMongoFindPage>>? FindHandler { get; init; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            LastCancellationToken = cancellationToken;
            return FindHandler is null ? Task.FromResult(Page) : FindHandler(cancellationToken);
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastByIdQuery = query;
            return Task.FromResult(Page);
        }
    }

    private sealed class StubCountSource : IAgentMongoCountSource
    {
        public int Calls { get; private set; }
        public AgentMongoCountQuery? LastQuery { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public AgentMongoCountResult Result { get; init; } = new("{\"$numberLong\":\"0\"}", true);
        public Func<CancellationToken, Task<AgentMongoCountResult>>? Handler { get; init; }

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            LastCancellationToken = cancellationToken;
            if (Handler is not null) return Handler(cancellationToken);
            return Task.FromResult(Result);
        }
    }

    private sealed class StubDistinctSource : IAgentMongoDistinctSource
    {
        public int Calls { get; private set; }
        public AgentMongoDistinctQuery? LastQuery { get; private set; }
        public AgentMongoDistinctPage Page { get; init; } = new([], false, true, false);
        public Func<CancellationToken, Task<AgentMongoDistinctPage>>? Handler { get; init; }

        public async Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile,
            AgentMongoDistinctQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Handler is null ? Page : await Handler(cancellationToken);
        }
    }

    private sealed class RecordingAuditRepository : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];
        public Func<AgentAuditEvent, CancellationToken, Task>? AppendHandler { get; init; }

        public async Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            entry.Validate();
            if (AppendHandler is { } handler) await handler(entry, cancellationToken);
            Events.Add(entry);
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default)
        {
            var closed = Events.Where(item => item.Outcome != AgentAuditOutcome.Intent)
                .Select(item => item.InvocationId).ToHashSet();
            return Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events
                .Where(item => item.Outcome == AgentAuditOutcome.Intent && !closed.Contains(item.InvocationId))
                .Take(maximum).ToArray());
        }
    }

    private static ConnectionProfile Connection(string name) =>
        ConnectionProfile.Create(name, "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentPrincipal Principal(long revision) => new(PrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant Grant(ConnectionProfile profile) =>
        new(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForConnection(profile.Id), LocalDestination, Metadata);

    private static AgentAuthorizationPolicySnapshot Policy(long revision, params AgentPermissionGrant[] grants) =>
        AgentAuthorizationPolicySnapshot.Load(PrincipalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

    private sealed class StubProfileRepository(IReadOnlyList<ConnectionProfile>? profiles = null, bool returnNull = false) : IConnectionProfileRepository
    {
        public int GetAllCalls { get; private set; }
        public IReadOnlyList<ConnectionProfile> Profiles { get; } = profiles ?? [];
        public Func<CancellationToken, Task<IReadOnlyList<ConnectionProfile>>>? GetAllHandler { get; init; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetAllCalls++;
            if (GetAllHandler is { } handler) return handler(cancellationToken);
            if (returnNull) return Task.FromResult<IReadOnlyList<ConnectionProfile>>(null!);
            return Task.FromResult(Profiles);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SequencePolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly AgentAuthorizationPolicySnapshot?[] _snapshots;
        private readonly bool _throws;
        private int _index;

        public SequencePolicyProvider(params AgentAuthorizationPolicySnapshot?[] snapshots) => _snapshots = snapshots;

        public SequencePolicyProvider(bool throws)
        {
            _snapshots = [];
            _throws = throws;
        }

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throws) throw new InvalidOperationException("private policy store detail");
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(_snapshots[Math.Min(index, _snapshots.Length - 1)]);
        }
    }

    private sealed class CountingPolicyProvider(AgentAuthorizationPolicySnapshot policy) : IAgentAuthorizationPolicyProvider
    {
        public int LoadCalls { get; private set; }
        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult<AgentAuthorizationPolicySnapshot?>(policy);
        }
    }

    private sealed class MutablePolicyProvider(AgentAuthorizationPolicySnapshot current) : IAgentAuthorizationPolicyProvider
    {
        public AgentAuthorizationPolicySnapshot Current { get; set; } = current;

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AgentAuthorizationPolicySnapshot?>(Current);
        }
    }

    private sealed class GatePolicyProvider(TaskCompletionSource entered) : IAgentAuthorizationPolicyProvider
    {
        public async Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class SequenceProfileRepository(
        IReadOnlyList<ConnectionProfile> initial,
        IReadOnlyList<ConnectionProfile> final) : IConnectionProfileRepository
    {
        public int GetAllCalls { get; private set; }
        public Func<CancellationToken, Task<IReadOnlyList<ConnectionProfile>>>? InitialReadHandler { get; init; }
        public Func<CancellationToken, Task<IReadOnlyList<ConnectionProfile>>>? FinalReadHandler { get; init; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetAllCalls++;
            if (GetAllCalls == 1) return InitialReadHandler?.Invoke(cancellationToken) ?? Task.FromResult(initial);
            return FinalReadHandler?.Invoke(cancellationToken) ?? Task.FromResult(final);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        public async Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
