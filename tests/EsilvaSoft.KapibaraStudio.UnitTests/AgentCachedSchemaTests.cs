using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentCachedSchemaTests
{
    [Test]
    public async Task CachedSchemaTruncatesHeterogeneousTypesAtTheDeclaredPerFieldLimit()
    {
        using var rig = new AgentSessionToolsTestRig();
        var key = LearnedSchemaKey.Create(rig.Profile.Id, "app", "people");
        var observed = DateTimeOffset.UtcNow;
        var typeCounts = Enumerable.Range(0, 17).ToDictionary(
            static index => $"type-{index:D2}", static _ => 1L, StringComparer.Ordinal);
        rig.Learned.Result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available,
            new LearnedSchemaSnapshot(key, 1, 3, rig.Profile.SourceGenerationId,
                observed, observed, 17, 1, 0, false,
                [new LearnedFieldStatistics(new LearnedFieldPath(["heterogeneous"]), 17, 17,
                    typeCounts, observed, observed)]), null);

        var result = await rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "people" });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var field = json.RootElement.GetProperty("fields").EnumerateArray().Single();
        var emittedTypes = field.GetProperty("types").EnumerateArray()
            .Select(static item => item.GetProperty("type").GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(emittedTypes, Has.Length.EqualTo(16));
            Assert.That(emittedTypes, Is.EquivalentTo(Enumerable.Range(0, 16).Select(static index => $"type-{index:D2}")));
            Assert.That(emittedTypes, Does.Not.Contain("type-16"));
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("available").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CachedSchemaTruncatesAtTheSerializedUtf8BudgetBetweenWholeFields()
    {
        using var rig = new AgentSessionToolsTestRig();
        var key = LearnedSchemaKey.Create(rig.Profile.Id, "app", "people");
        var observed = DateTimeOffset.UtcNow;
        var fields = Enumerable.Range(0, 200).Select(index =>
            new LearnedFieldStatistics(new LearnedFieldPath([$"f{index:D3}_" + new string('界', 450)]), 1, 1,
                new Dictionary<string, long> { ["string"] = 1 }, observed, observed)).ToArray();
        rig.Learned.Result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available,
            new LearnedSchemaSnapshot(key, 1, 3, rig.Profile.SourceGenerationId,
                observed, observed, 1, 1, 0, false, fields), null);

        var result = await rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "people" });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var jsonText = result.StructuredContentJson!;
        var serializedBytes = Encoding.UTF8.GetByteCount(jsonText);
        using var json = JsonDocument.Parse(jsonText);
        var emittedPaths = json.RootElement.GetProperty("fields").EnumerateArray()
            .Select(static field => field.GetProperty("path").GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(serializedBytes, Is.GreaterThan(250 * 1024), "The fixture reaches the byte budget, rather than only the field-count limit.");
            Assert.That(serializedBytes, Is.LessThanOrEqualTo(256 * 1024), "The complete UTF-8 response stays within the output ceiling.");
            Assert.That(emittedPaths.Length, Is.GreaterThan(50).And.LessThan(200));
            Assert.That(emittedPaths.All(path => fields.Any(field => string.Join('.', field.Path.Segments) == path)), Is.True,
                "Every retained path is a complete source field; truncation never emits a partial field.");
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("available").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CachedSchemaDeadlineSuppressesLateLearnedSnapshotAndAuditsCancellation()
    {
        using var rig = new AgentSessionToolsTestRig(executionTimeout: TimeSpan.FromMilliseconds(400));
        var key = LearnedSchemaKey.Create(rig.Profile.Id, "app", "people");
        var observed = DateTimeOffset.UtcNow;
        var snapshot = new LearnedSchemaSnapshot(key, 1, 3, rig.Profile.SourceGenerationId,
            observed, observed, 10, 1, 0, false,
            [new LearnedFieldStatistics(new LearnedFieldPath(["late-schema-canary"]), 1, 1,
                new Dictionary<string, long> { ["string"] = 1 }, observed, observed)]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Learned.ReadAvailability = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            sourceCompleted.TrySetResult();
            return new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available, snapshot, null);
        };

        var pending = rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "people" });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Learned.ReadAvailabilityCalls, Is.EqualTo(1));
                Assert.That(rig.Metadata.Calls, Is.Zero, "Cached schema must not fall back to MongoDB.");
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Cancelled));
                Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.Cancelled));
                Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
                Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("late-schema-canary"));
            });
        }
        finally
        {
            release.TrySetResult();
            await sourceCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestCase("generation")]
    [TestCase("connection-string")]
    [TestCase("policy")]
    public async Task CachedSchemaRevalidatesOriginBeforePeekWhenPermissionCompletesLate(string change)
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.Cache.Schema = new SchemaBuilder().AddDocuments(["{\"private-field-canary\":1}"]).Build();
        var profiles = new MutableProfiles(rig.Profile);
        var permissions = new GatedPermissions(new AgentPermissionEvaluator(rig.Policies));
        var registry = new AgentToolRegistry(profiles, rig.Policies, permissions, rig.Audit,
            metadata: rig.Metadata, exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata),
            principalAuthority: rig.Authority, sessionTools: new AgentSessionToolPorts(rig.Sessions)
            {
                MetadataCache = rig.Cache, LearnedSchemas = rig.Learned
            });
        var arguments = JsonSerializer.Serialize(new
        {
            connectionId = rig.Profile.Id, database = "app", collection = "people"
        });
        var pending = registry.InvokeAsync(rig.Principal,
            new AgentInvocationContext(AgentBrokerProtocol.McpProviderId, rig.ChannelId, rig.ChannelId, Guid.NewGuid()),
            AgentSessionToolsTestRig.Destination, AgentOutputDataScope.Schema, "get_cached_schema", arguments);
        try
        {
            await permissions.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "policy")
                rig.Policies.Set(rig.PrincipalId, 2, []);
            else
                profiles.Current = rig.Profile with
                {
                    SourceGenerationId = change == "generation" ? Guid.NewGuid() : rig.Profile.SourceGenerationId,
                    ConnectionString = change == "connection-string"
                        ? "mongodb://changed-origin-canary:27018" : rig.Profile.ConnectionString
                };
            permissions.Release.TrySetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(rig.Cache.Reads, Is.Zero, "Reject the stale authorization before reading schema fields.");
                Assert.That(rig.Cache.ForbiddenCalls, Is.Zero);
                Assert.That(rig.Metadata.Calls, Is.Zero);
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
            permissions.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
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

    private sealed class GatedPermissions(IAgentPermissionEvaluator inner) : IAgentPermissionEvaluator
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AgentPermissionDecision> EvaluateAsync(AgentPermissionRequest request,
            CancellationToken cancellationToken)
        {
            var decision = await inner.EvaluateAsync(request, cancellationToken);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return decision;
        }
    }
}
