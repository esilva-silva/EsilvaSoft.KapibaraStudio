using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

/// <summary>Production registry over synthetic context and memory sources for explicit official SDK checks.</summary>
internal sealed class CopilotProductToolTestRig : AgentSessionToolDoubles, IDisposable
{
    private readonly SyntheticDirectory _directory = new();
    public string WorkspaceFolder => _directory.Path;
    public ConnectionProfile Profile { get; } = ConnectionProfile.Create("Synthetic profile", "mongodb://synthetic.invalid:27017")
        with { SourceGenerationId = Guid.NewGuid() };
    public MapPolicies Policies { get; } = new();
    public MemoryAudit Audit { get; } = new();
    public AgentNativeChatTurnScopeRegistry NativeChatScopes { get; } = new();
    public AgentToolRegistry Registry { get; }
    public FakeConfirmation Confirmation { get; } = new();

    public CopilotProductToolTestRig(IMongoMetadataSource? metadata = null, IAgentMongoFindSource? find = null,
        IAgentMongoCountSource? count = null, IAgentMongoDistinctSource? distinct = null,
        IAgentMongoExplainSource? explain = null)
    {
        Registry = new AgentToolRegistry(new FixedProfiles(Profile), Policies, new AgentPermissionEvaluator(Policies), Audit,
            metadata: metadata ?? new ThrowingMetadataSource(), find: find, count: count, distinct: distinct, explain: explain,
            exposure: AgentToolExposure.Through(find is null ? AgentToolExposureStage.Metadata : AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority(), indexes: new FakeIndexes(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                MetadataCache = new FakeMetadataCache(), LearnedSchemas = new FakeLearned(),
                WorkspaceContext = new FakeWorkspace { Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder) },
                NativeChatTurnScopes = NativeChatScopes, ProposalSink = new FakeSink(), ConfirmationPrompt = Confirmation,
                FileCreator = new EsilvaSoft.KapibaraStudio.SystemAdapters.LocalAgentWorkspaceFileCreator(),
                PathProbe = new EsilvaSoft.KapibaraStudio.SystemAdapters.LocalAgentWorkspacePathProbe()
            });
    }

    public void Dispose() => _directory.Dispose();

    private sealed class FixedProfiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
