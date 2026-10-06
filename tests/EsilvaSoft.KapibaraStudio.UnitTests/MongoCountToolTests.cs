using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class MongoCountToolTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task MongoCountSuppressesScalarWhenGenerationOrUriChangesDuringCount(bool changeGeneration)
    {
        var rig = new CountRig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentMongoCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Source.Handler = _ =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        var pending = rig.InvokeAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            rig.Profiles.Current = rig.Profile with
            {
                SourceGenerationId = changeGeneration ? Guid.NewGuid() : rig.Profile.SourceGenerationId,
                ConnectionString = changeGeneration ? rig.Profile.ConnectionString : "mongodb://changed-count-canary:27018"
            };
            release.TrySetResult(new("{\"$numberLong\":\"42\"}", true));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Source.Calls, Is.EqualTo(1), "Do not replay a completed count after an origin change.");
                Assert.That(rig.Source.LastQuery!.FilterEjson, Is.EqualTo("{\"active\":true}"));
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
            });
        }
        finally
        {
            release.TrySetResult(new("{\"$numberLong\":\"0\"}", true));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestCase("9223372036854775807", true)]
    [TestCase("9223372036854775808", false)]
    [TestCase("00", false)]
    public async Task MongoCountPreservesMaximumInt64ButRejectsOverflowAndNonCanonicalDigits(string digits, bool valid)
    {
        var rig = new CountRig();
        var canonical = "{\"$numberLong\":\"" + digits + "\"}";
        rig.Source.Handler = _ => Task.FromResult(new AgentMongoCountResult(canonical, true));

        var result = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(valid ? 1 : 0));
        });
        if (!valid)
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            });
            return;
        }
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("countEjson").GetString(), Is.EqualTo(canonical),
                "The Int64 scalar must be preserved exactly, without a floating point approximation.");
            Assert.That(json.RootElement.GetProperty("estimated").GetBoolean(), Is.False);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task MongoCountSanitizesSourceFailureAndNextInvocationRecoversWithoutReplay()
    {
        var rig = new CountRig();
        rig.Source.Handler = _ => throw new IOException("private-count-failure-canary");

        var failed = await rig.InvokeAsync();
        rig.Source.Handler = _ => Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"0\"}", true));
        var recovered = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(failed.ErrorCode, Is.EqualTo("ExecutionFailed"));
            Assert.That(failed.StructuredContentJson, Is.Null);
            Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
            Assert.That(rig.Source.Calls, Is.EqualTo(2), "Each invocation reaches the source once; the failed count is not replayed.");
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
            {
                AgentAuditOutcome.Intent, AgentAuditOutcome.Failed,
                AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded
            }));
            Assert.That(rig.Audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ExecutionFailed));
            Assert.That(rig.Audit.Events[1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("private-count-failure-canary"));
        });
    }

    private sealed class CountRig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        public ConnectionProfile Profile { get; } = ConnectionProfile.Create("count", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public AgentPrincipal Principal { get; } = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 172);
        public MutableProfiles Profiles { get; }
        public MapPolicies Policies { get; } = new();
        public MemoryAudit Audit { get; } = new();
        public CountSource Source { get; } = new();

        public CountRig()
        {
            Profiles = new MutableProfiles(Profile);
            Policies.Set(Principal.Id, 172,
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
                count: Source, exposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync() => _registry.InvokeAsync(Principal,
            new AgentInvocationContext(null, null, _session, _turn), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, "mongo_count", JsonSerializer.Serialize(new
            {
                connectionId = Profile.Id, database = "app", collection = "people", filterEjson = "{\"active\":true}"
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

    private sealed class CountSource : IAgentMongoCountSource
    {
        public int Calls { get; private set; }
        public AgentMongoCountQuery? LastQuery { get; private set; }
        public Func<CancellationToken, Task<AgentMongoCountResult>>? Handler { get; set; }
        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Handler!(cancellationToken);
        }
    }
}
