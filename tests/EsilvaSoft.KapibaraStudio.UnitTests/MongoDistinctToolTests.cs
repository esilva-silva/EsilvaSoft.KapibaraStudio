using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoDistinctToolTests
{
    [TestCase(-1, false)]
    [TestCase(42, false)]
    [TestCase((int)AgentMongoDistinctTruncationReason.OutputLimit, true)]
    public async Task MongoDistinctAcceptsOnlyDeclaredSourceTruncationReasons(int reason, bool valid)
    {
        var rig = new DistinctRig(new(["\"distinct-value-canary\""], true, true, false,
            (AgentMongoDistinctTruncationReason)reason));

        var result = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.EqualTo(valid));
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Source.LastQuery!.Field, Is.EqualTo("nested.value"));
            Assert.That(rig.Source.LastQuery.MaximumValues, Is.EqualTo(20));
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(valid ? 1 : 0));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
        });
        if (valid)
        {
            using var output = JsonDocument.Parse(result.StructuredContentJson!);
            Assert.Multiple(() =>
            {
                Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
                Assert.That(output.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("OutputLimit"));
                Assert.That(output.RootElement.GetProperty("valuesEjson")[0].GetString(),
                    Is.EqualTo("\"distinct-value-canary\""));
                Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ValidationRejected));
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            });
        }
    }

    private sealed class DistinctRig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 177);
        private readonly ConnectionProfile _profile = ConnectionProfile.Create("distinct", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        public MemoryAudit Audit { get; } = new();
        public DistinctSource Source { get; }

        public DistinctRig(AgentMongoDistinctPage page)
        {
            Source = new DistinctSource(page);
            var policies = new MapPolicies();
            policies.Set(_principal.Id, 177,
            [
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    _profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                    AgentNamespaceScope.ForCollection(_profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues),
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    _profile.SourceGenerationId!.Value, AgentPermission.ReadDocuments,
                    AgentNamespaceScope.ForCollection(_profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues)
            ]);
            _registry = new AgentToolRegistry(new Profiles(_profile), policies, new AgentPermissionEvaluator(policies),
                Audit, distinct: Source, exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestAgentPrincipalAuthority());
        }

        public Task<AgentToolInvocationResult> InvokeAsync()
            => _registry.InvokeAsync(_principal, new AgentInvocationContext(null, null, _session, _turn),
                AgentOutputDestination.Local(), AgentOutputDataScope.DocumentValues, "mongo_distinct",
                JsonSerializer.Serialize(new
                {
                    connectionId = _profile.Id, database = "app", collection = "people", field = "nested.value"
                }));
    }

    private sealed class Profiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class DistinctSource(AgentMongoDistinctPage page) : IAgentMongoDistinctSource
    {
        public int Calls { get; private set; }
        public AgentMongoDistinctQuery? LastQuery { get; private set; }
        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(page);
        }
    }
}
