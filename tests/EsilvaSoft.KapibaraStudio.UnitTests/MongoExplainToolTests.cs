using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoExplainToolTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoExplainDeadlineSuppressesLatePlanOrFaultAndKeepsCallerTokenUsable(bool lateFault)
    {
        var rig = new ExplainRig(TimeSpan.FromMilliseconds(250));
        using var caller = new CancellationTokenSource();
        var pending = rig.InvokeAsync(caller.Token);
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(caller.IsCancellationRequested, Is.False);
            });
        }
        finally
        {
            if (lateFault)
                rig.Source.Release.TrySetException(new IOException("late-explain-canary"));
            else
                rig.Source.Release.TrySetResult(new("{\"stage\":\"IXSCAN\",\"indexName\":\"late-explain-canary\"}", true, false));
            await rig.Source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(rig.Source.LastQuery!.MaxTimeMs, Is.EqualTo(250));
            Assert.That(rig.Source.LastQuery.FilterEjson, Is.EqualTo(ExplainRig.LiteralFilter));
            Assert.That(rig.Source.LastQuery.Limit, Is.EqualTo(1));
            Assert.That(rig.Source.LastQuery.Skip, Is.EqualTo(0));
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });

        var timedOutToken = rig.Source.LastCancellationToken;
        rig.Source.ImmediateResult = new("{\"stage\":\"COLLSCAN\"}", true, false);
        var healthy = await rig.InvokeAsync(caller.Token);
        Assert.That(healthy.Succeeded, Is.True, healthy.ErrorCode);
        using var output = JsonDocument.Parse(healthy.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("planEjson").GetString(), Is.EqualTo("{\"stage\":\"COLLSCAN\"}"));
            Assert.That(output.RootElement.GetProperty("verbosity").GetString(), Is.EqualTo("queryPlanner"));
            Assert.That(rig.Source.Calls, Is.EqualTo(2), "Exactly one dispatch for each explicit invocation.");
            Assert.That(rig.Source.LastCancellationToken, Is.Not.EqualTo(timedOutToken));
            Assert.That(rig.Source.LastCancellationToken.IsCancellationRequested, Is.False);
            Assert.That(caller.IsCancellationRequested, Is.False);
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(4));
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });
    }

    [Test]
    public async Task MongoExplainSuppressesPlanWhenUriChangesButGenerationIsConstantDuringRead()
    {
        var rig = new ExplainRig(TimeSpan.FromSeconds(5));
        var pending = rig.InvokeAsync();
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            rig.Profiles.Current = rig.Profile with { ConnectionString = "mongodb://changed-explain-canary:27018" };
            Assert.That(rig.Profiles.Current.SourceGenerationId, Is.EqualTo(rig.Profile.SourceGenerationId));
            rig.Source.Release.TrySetResult(new("{\"stage\":\"IXSCAN\",\"indexName\":\"late-explain-canary\"}", true, false));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Source.Calls, Is.EqualTo(1));
                Assert.That(rig.Source.LastQuery!.Database, Is.EqualTo("app"));
                Assert.That(rig.Source.LastQuery.Collection, Is.EqualTo("people"));
                Assert.That(rig.Source.LastQuery.FilterEjson, Is.EqualTo(ExplainRig.LiteralFilter));
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
            });
        }
        finally
        {
            rig.Source.Release.TrySetResult(new("{\"stage\":\"COLLSCAN\"}", true, false));
            await rig.Source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class ExplainRig : AgentSessionToolDoubles
    {
        public const string LiteralFilter = "{\"value\":\"filter-explain-canary\"}";
        private static readonly AgentPermission[] RequiredPermissions =
            [AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments];
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 178);
        public ConnectionProfile Profile { get; } = ConnectionProfile.Create("explain", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public MutableProfiles Profiles { get; }
        public MemoryAudit Audit { get; } = new();
        public GatedExplainSource Source { get; } = new();

        public ExplainRig(TimeSpan timeout)
        {
            Profiles = new MutableProfiles(Profile);
            var policies = new MapPolicies();
            policies.Set(_principal.Id, 178, RequiredPermissions.Select(permission =>
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    Profile.SourceGenerationId!.Value, permission,
                    AgentNamespaceScope.ForCollection(Profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues)).ToArray());
            _registry = new AgentToolRegistry(Profiles, policies, new AgentPermissionEvaluator(policies), Audit,
                executionTimeout: timeout, explain: Source,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync(CancellationToken cancellationToken = default)
            => _registry.InvokeAsync(_principal, new AgentInvocationContext(null, null, _session, _turn),
                AgentOutputDestination.Local(), AgentOutputDataScope.DocumentValues, "mongo_explain",
                JsonSerializer.Serialize(new
                {
                    connectionId = Profile.Id, database = "app", collection = "people", filterEjson = LiteralFilter,
                    limit = 1, maxTimeMs = 30_000
                }), cancellationToken);
    }

    private sealed class MutableProfiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public ConnectionProfile Current { get; set; } = profile;
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>([Current]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class GatedExplainSource : IAgentMongoExplainSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AgentMongoExplainResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentMongoExplainResult? ImmediateResult { get; set; }
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            LastCancellationToken = cancellationToken;
            if (ImmediateResult is { } immediate) return immediate;
            Entered.TrySetResult();
            try { return await Release.Task; } // Model an origin that ignores cancellation and completes late.
            finally { Completed.TrySetResult(); }
        }
    }
}
