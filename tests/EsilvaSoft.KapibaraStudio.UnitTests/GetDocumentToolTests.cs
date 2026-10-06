using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class GetDocumentToolTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task GetDocumentCancellationOrDeadlineSuppressesLateByIdResultOrFault(bool deadline, bool lateFault)
    {
        var rig = new DocumentRig(TimeSpan.FromMilliseconds(deadline ? 250 : 5_000));
        using var cancellation = new CancellationTokenSource();
        var pending = rig.InvokeAsync(cancellation.Token);
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (deadline)
            {
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(cancellation.IsCancellationRequested, Is.False,
                    "The invocation deadline must not cancel the caller's token source.");
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
            if (lateFault) rig.Source.Release.TrySetException(new IOException("late-by-id-canary"));
            else rig.Source.Release.TrySetResult(new(["{\"value\":\"late-by-id-canary\"}"], false, false, true, false));
            await rig.Source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.ByIdCalls, Is.EqualTo(1));
            Assert.That(rig.Source.GeneralFindCalls, Is.Zero);
            Assert.That(rig.Source.LastCancellationToken.IsCancellationRequested, Is.True);
            Assert.That(rig.Source.LastQuery!.IdEjson, Is.EqualTo(DocumentRig.LiteralId));
            Assert.That(rig.Source.LastQuery.MaxTimeMs, Is.EqualTo(deadline ? 250 : 5_000));
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetDocumentSuppressesByIdResultWhenPolicyOrUriChangesDuringRead(bool changePolicy)
    {
        var rig = new DocumentRig(TimeSpan.FromSeconds(5));
        var pending = rig.InvokeAsync();
        try
        {
            await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (changePolicy)
                rig.Policies.Set(rig.Principal.Id, 176, []);
            else
                rig.Profiles.Current = rig.Profile with { ConnectionString = "mongodb://changed-by-id-canary:27018" };
            rig.Source.Release.TrySetResult(new(["{\"value\":\"late-by-id-canary\"}"], false, false, true, false));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Source.ByIdCalls, Is.EqualTo(1));
                Assert.That(rig.Source.GeneralFindCalls, Is.Zero);
                Assert.That(rig.Source.LastQuery!.IdEjson, Is.EqualTo(DocumentRig.LiteralId));
                Assert.That(rig.Source.LastQuery.Database, Is.EqualTo("app"));
                Assert.That(rig.Source.LastQuery.Collection, Is.EqualTo("people"));
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(changePolicy
                    ? AgentAuditDecisionReason.PolicyRevisionMismatch : AgentAuditDecisionReason.ValidationRejected));
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

    private sealed class DocumentRig : AgentSessionToolDoubles
    {
        public const string LiteralId = "\"captured-id-canary\"";
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        public AgentPrincipal Principal { get; } = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 175);
        public ConnectionProfile Profile { get; } = ConnectionProfile.Create("document", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public MutableProfiles Profiles { get; }
        public MapPolicies Policies { get; } = new();
        public MemoryAudit Audit { get; } = new();
        public GatedByIdSource Source { get; } = new();

        public DocumentRig(TimeSpan timeout)
        {
            Profiles = new MutableProfiles(Profile);
            Policies.Set(Principal.Id, 175,
            [
                new AgentPermissionGrant(Principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    Profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                    AgentNamespaceScope.ForCollection(Profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues),
                new AgentPermissionGrant(Principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    Profile.SourceGenerationId!.Value, AgentPermission.ReadDocuments,
                    AgentNamespaceScope.ForCollection(Profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues)
            ]);
            _registry = new AgentToolRegistry(Profiles, Policies, new AgentPermissionEvaluator(Policies), Audit,
                executionTimeout: timeout, find: Source,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync(CancellationToken cancellationToken = default)
            => _registry.InvokeAsync(Principal, new AgentInvocationContext(null, null, _session, _turn),
                AgentOutputDestination.Local(), AgentOutputDataScope.DocumentValues, "get_document",
                JsonSerializer.Serialize(new
                {
                    connectionId = Profile.Id, database = "app", collection = "people", idEjson = LiteralId
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

    private sealed class GatedByIdSource : IAgentMongoFindSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AgentMongoFindPage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ByIdCalls { get; private set; }
        public int GeneralFindCalls { get; private set; }
        public AgentMongoFindByIdQuery? LastQuery { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            ByIdCalls++;
            LastQuery = query;
            LastCancellationToken = cancellationToken;
            Entered.TrySetResult();
            try { return await Release.Task; } // Simulate an origin that finishes after cancellation.
            finally { Completed.TrySetResult(); }
        }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            GeneralFindCalls++;
            throw new InvalidOperationException("get_document must dispatch the typed by-ID contract.");
        }
    }
}
