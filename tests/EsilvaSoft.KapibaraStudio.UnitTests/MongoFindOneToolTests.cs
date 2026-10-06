using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class MongoFindOneToolTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MongoFindOneDeadlineSuppressesLateDocumentOrFaultWithoutRetry(bool lateFault)
    {
        var rig = new FindOneRig(TimeSpan.FromMilliseconds(250));
        var pending = rig.InvokeAsync();
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
        }
        finally
        {
            if (lateFault) rig.Source.Release.TrySetException(new IOException("late-find-one-deadline-canary"));
            else rig.Source.Release.TrySetResult(new(["{\"value\":\"late-find-one-deadline-canary\"}"],
                false, false, true, false));
            await rig.Source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(rig.Source.LastQuery!.Limit, Is.EqualTo(1));
            Assert.That(rig.Source.LastQuery.MaxTimeMs, Is.EqualTo(250), "The model cannot extend the execution budget.");
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });

        var timedOutToken = rig.Source.LastCancellationToken;
        const string healthyDocument = "{\"healthy\":true}";
        rig.Source.ImmediatePage = new([healthyDocument], false, false, true, false);
        var nextResult = await rig.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(nextResult.Succeeded, Is.True, nextResult.ErrorCode);
        using var json = JsonDocument.Parse(nextResult.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("documentEjson").GetString(), Is.EqualTo(healthyDocument));
            Assert.That(rig.Source.Calls, Is.EqualTo(2), "Each invocation dispatches once, including recovery after deadline.");
            Assert.That(rig.Source.LastCancellationToken, Is.Not.EqualTo(timedOutToken));
            Assert.That(rig.Source.LastCancellationToken.IsCancellationRequested, Is.False,
                "A subsequent call must not inherit the expired invocation's token.");
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(4));
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(1));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });
    }

    [Test]
    public async Task MongoFindOneSuppressesDocumentWhenUriChangesWithoutGenerationChangeDuringRead()
    {
        var rig = new FindOneRig(TimeSpan.FromSeconds(5));
        var pending = rig.InvokeAsync();
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            rig.Profiles.Current = rig.Profile with { ConnectionString = "mongodb://changed-find-one-canary:27018" };
            rig.Source.Release.TrySetResult(new(["{\"value\":\"late-find-one-uri-canary\"}"],
                false, false, true, false));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Source.Calls, Is.EqualTo(1));
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
            });
        }
        finally
        {
            rig.Source.Release.TrySetResult(new([], false, false, true, false));
            await rig.Source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class FindOneRig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 174);
        public ConnectionProfile Profile { get; } = ConnectionProfile.Create("find-one", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public MutableProfiles Profiles { get; }
        public MemoryAudit Audit { get; } = new();
        public GatedSource Source { get; } = new();

        public FindOneRig(TimeSpan timeout)
        {
            Profiles = new MutableProfiles(Profile);
            var policies = new MapPolicies();
            policies.Set(_principal.Id, 174,
            [
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    Profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                    AgentNamespaceScope.ForCollection(Profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues),
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    Profile.SourceGenerationId!.Value, AgentPermission.ReadDocuments,
                    AgentNamespaceScope.ForCollection(Profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues)
            ]);
            _registry = new AgentToolRegistry(Profiles, policies, new AgentPermissionEvaluator(policies), Audit,
                executionTimeout: timeout, find: Source,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync() => _registry.InvokeAsync(_principal,
            new AgentInvocationContext(null, null, _session, _turn), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, "mongo_find_one", JsonSerializer.Serialize(new
            {
                connectionId = Profile.Id, database = "app", collection = "people", maxTimeMs = 30_000
            }));
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

    private sealed class GatedSource : IAgentMongoFindSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AgentMongoFindPage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public AgentMongoFindPage? ImmediatePage { get; set; }

        public async Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            LastCancellationToken = cancellationToken;
            if (ImmediatePage is { } page) return page;
            Entered.TrySetResult();
            try { return await Release.Task; } // Intentionally ignores cancellation to simulate a late origin.
            finally { Completed.TrySetResult(); }
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
