using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class MongoFindToolTests
{
    [TestCase("generation", false)]
    [TestCase("connection-string", false)]
    [TestCase("policy", false)]
    [TestCase("generation", true)]
    [TestCase("connection-string", true)]
    [TestCase("policy", true)]
    public async Task MongoFindSuppressesLatePageWhenOriginOrPolicyChanges(string change, bool emptyPage)
    {
        using var rig = new FindRig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentMongoFindPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Source.Handler = _ =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        var pending = rig.InvokeAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "policy")
                rig.Policies.Set(rig.Principal.Id, 171, []);
            else
                rig.Profiles.Current = rig.Profile with
                {
                    SourceGenerationId = change == "generation" ? Guid.NewGuid() : rig.Profile.SourceGenerationId,
                    ConnectionString = change == "connection-string"
                        ? "mongodb://changed-profile-canary:27018" : rig.Profile.ConnectionString
                };
            release.TrySetResult(new(emptyPage ? [] : ["{\"value\":\"late-find-canary\"}"],
                false, false, true, false));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Source.Calls, Is.EqualTo(1), "The original read is not repeated.");
                Assert.That(rig.Source.LastQuery!.FilterEjson, Is.EqualTo("{}"));
                Assert.That(rig.Source.LastQuery.Limit, Is.EqualTo(100));
                Assert.That(rig.Source.LastQuery.Skip, Is.EqualTo(10_000));
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(change == "policy"
                    ? AgentAuditDecisionReason.PolicyRevisionMismatch : AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
            });
        }
        finally
        {
            release.TrySetResult(new([], false, false, true, false));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestCase(0, false)]
    [TestCase(100, false)]
    [TestCase(100, true)]
    [TestCase(101, false)]
    public async Task MongoFindReturnsEmptyOrExactPageButRejectsSourceBeyondRequestedLimit(int count, bool hasMore)
    {
        using var rig = new FindRig();
        var documents = Enumerable.Range(0, count)
            .Select(number => "{\"number\":{\"$numberInt\":\"" + number + "\"}}").ToArray();
        rig.Source.Handler = _ => Task.FromResult(new AgentMongoFindPage(documents, hasMore, false, true, false));

        var result = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Source.LastQuery!.Limit, Is.EqualTo(100));
            Assert.That(rig.Source.LastQuery.Skip, Is.EqualTo(10_000));
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
        });
        if (count > 100)
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            });
            return;
        }
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("documentsEjson").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(documents));
            Assert.That(json.RootElement.GetProperty("returnedCount").GetInt32(), Is.EqualTo(count));
            Assert.That(json.RootElement.GetProperty("hasMore").GetBoolean(), Is.EqualTo(hasMore),
                "The requested limit alone does not prove another page.");
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(count));
        });
    }

    [Test]
    public async Task MongoFindSanitizesSourceFailureAndNextInvocationRecoversWithoutReplay()
    {
        using var rig = new FindRig();
        rig.Source.Handler = _ => throw new IOException("private-find-failure-canary");

        var failed = await rig.InvokeAsync();
        rig.Source.Handler = _ => Task.FromResult(new AgentMongoFindPage(
            ["{\"value\":{\"$numberInt\":\"7\"}}"], false, false, true, false));
        var recovered = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(failed.ErrorCode, Is.EqualTo("ExecutionFailed"));
            Assert.That(failed.StructuredContentJson, Is.Null);
            Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
            Assert.That(rig.Source.Calls, Is.EqualTo(2), "Each invocation reaches the source once; the failed read is not replayed.");
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
            {
                AgentAuditOutcome.Intent, AgentAuditOutcome.Failed,
                AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded
            }));
            Assert.That(rig.Audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ExecutionFailed));
            Assert.That(rig.Audit.Events[1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("private-find-failure-canary"));
        });
    }

    private sealed class FindRig : AgentSessionToolDoubles, IDisposable
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        public ConnectionProfile Profile { get; } = ConnectionProfile.Create("find", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public AgentPrincipal Principal { get; } = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 170);
        public MutableProfiles Profiles { get; }
        public MapPolicies Policies { get; } = new();
        public MemoryAudit Audit { get; } = new();
        public FindSource Source { get; } = new();

        public FindRig()
        {
            Profiles = new MutableProfiles(Profile);
            Policies.Set(Principal.Id, 170,
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
                find: Source, exposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync() => _registry.InvokeAsync(Principal,
            new AgentInvocationContext(null, null, _session, _turn), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, "mongo_find", JsonSerializer.Serialize(new
            {
                connectionId = Profile.Id, database = "app", collection = "people", limit = 100, skip = 10_000
            }));

        public void Dispose() { }
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

    private sealed class FindSource : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public Func<CancellationToken, Task<AgentMongoFindPage>>? Handler { get; set; }
        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Handler!(cancellationToken);
        }
        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
