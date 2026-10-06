using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class SampleDocumentsToolTests
{
    [TestCase(0)]
    [TestCase(20)]
    [TestCase(21)]
    public async Task SampleDocumentsReturnsEmptyOrMaximumPageButRejectsSourceAboveTwenty(int count)
    {
        var documents = Enumerable.Range(0, count)
            .Select(number => "{\"number\":{\"$numberInt\":\"" + number + "\"}}").ToArray();
        var rig = new SampleRig(schemaGrantInsteadOfDocuments: false,
            new AgentMongoFindPage(documents, false, false, true, false));

        var result = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Calls, Is.EqualTo(1));
            Assert.That(rig.Source.LastQuery!.Limit, Is.EqualTo(20));
            Assert.That(rig.Source.LastQuery.Skip, Is.Zero);
            Assert.That(rig.Source.LastQuery.FilterEjson, Is.EqualTo("{}"));
            Assert.That(rig.Source.LastQuery.SortEjson, Is.Null);
            Assert.That(rig.Metadata.Calls, Is.Zero);
            Assert.That(rig.Cache.Reads + rig.Cache.ForbiddenCalls, Is.Zero);
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
        });
        if (count > 20)
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
            Assert.That(json.RootElement.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(count));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SampleDocumentsDoesNotTradeSchemaGrantForDocumentPermissionOrStartSchemaSampling(bool schemaOnly)
    {
        const string document = "{\"value\":{\"$numberLong\":\"9007199254740993\"}}";
        var rig = new SampleRig(schemaOnly, new AgentMongoFindPage([document], false, false, true, false));

        var result = await rig.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.EqualTo(!schemaOnly));
            Assert.That(rig.Source.Calls, Is.EqualTo(schemaOnly ? 0 : 1));
            Assert.That(rig.Metadata.Calls, Is.Zero, "Do not invoke the MongoDB schema sampling source.");
            Assert.That(rig.SamplingConsent.Calls, Is.Zero, "Document reads neither require nor request schema sampling consent.");
            Assert.That(rig.Cache.Reads + rig.Cache.ForbiddenCalls, Is.Zero, "Do not load, refresh or publish sampled schema.");
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.EqualTo(schemaOnly ? 0 : 1));
        });
        if (schemaOnly)
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            return;
        }
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(json.RootElement.GetProperty("documentsEjson")[0].GetString(), Is.EqualTo(document));
    }

    private sealed class SampleRig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentToolRegistry _registry;
        private readonly ConnectionProfile _profile = ConnectionProfile.Create("sample", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 173);
        public MemoryAudit Audit { get; } = new();
        public FakeMetadataCache Cache { get; } = new();
        public ThrowingMetadataSource Metadata { get; } = new();
        public DeniedSamplingConsent SamplingConsent { get; } = new();
        public SampleSource Source { get; }

        public SampleRig(bool schemaGrantInsteadOfDocuments, AgentMongoFindPage page)
        {
            Source = new SampleSource(page);
            var policies = new MapPolicies();
            policies.Set(_principal.Id, 173,
            [
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    _profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                    AgentNamespaceScope.ForCollection(_profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues),
                new AgentPermissionGrant(_principal.Id, AgentInvocationScope.ForTurn(_session, _turn),
                    _profile.SourceGenerationId!.Value,
                    schemaGrantInsteadOfDocuments ? AgentPermission.ReadSchema : AgentPermission.ReadDocuments,
                    AgentNamespaceScope.ForCollection(_profile.Id, "app", "people"), AgentOutputDestination.Local(),
                    schemaGrantInsteadOfDocuments ? AgentOutputDataScope.Schema : AgentOutputDataScope.DocumentValues)
            ]);
            _registry = new AgentToolRegistry(new Mcp.McpFixedProfiles(_profile), policies,
                new AgentPermissionEvaluator(policies), Audit, metadata: Metadata, find: Source,
                schemaSamplingConsent: SamplingConsent,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestAgentPrincipalAuthority(),
                sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry()) { MetadataCache = Cache });
        }

        public Task<AgentToolInvocationResult> InvokeAsync() => _registry.InvokeAsync(_principal,
            new AgentInvocationContext(null, null, _session, _turn), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, "sample_documents", JsonSerializer.Serialize(new
            {
                connectionId = _profile.Id, database = "app", collection = "people", limit = 20
            }));
    }

    private sealed class DeniedSamplingConsent : IAgentSchemaSamplingConsentProvider
    {
        public int Calls { get; private set; }
        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(false);
        }
    }

    private sealed class SampleSource(AgentMongoFindPage page) : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(page);
        }
        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
