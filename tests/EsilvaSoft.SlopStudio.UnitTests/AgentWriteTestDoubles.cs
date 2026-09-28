using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Test doubles for lote 10 write tests. None of them simulates MongoDB integrity; see the real-Mongo suite.</summary>
internal static class AgentWriteTestDoubles
{
    public static readonly Guid PrincipalId = Guid.Parse("7a1f0c3e-5a55-4c0b-9df4-7cb1e3a8e0a1");
    public static readonly Guid OtherPrincipalId = Guid.Parse("7a1f0c3e-5a55-4c0b-9df4-7cb1e3a8e0a2");
    public static readonly Guid SessionId = Guid.Parse("7a1f0c3e-2b35-4e47-8f71-08a1a7e2a1b1");
    public static readonly Guid TurnId = Guid.Parse("7a1f0c3e-2b35-4e47-8f71-08a1a7e2a1b2");
    public const string StateHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static AgentWriteProposal Proposal(
        Guid? approvalId = null, Guid? principalId = null, Guid? sessionId = null, Guid? turnId = null,
        Guid? invocationId = null, string payload = "{\"$set\":{\"v\":2}}",
        AgentPrincipalOrigin origin = AgentPrincipalOrigin.Internal, Guid? generation = null) =>
        new(AgentWriteOperationKind.UpdateOne, AgentToolRegistry.UpdateOneToolName, 1, AgentToolRisk.Write,
            AgentPermission.UpdateDocuments, principalId ?? PrincipalId, origin, sessionId ?? SessionId,
            turnId ?? TurnId, invocationId ?? Guid.Parse("7a1f0c3e-0000-4e47-8f71-08a1a7e2a1b3"),
            approvalId ?? Guid.Parse("7a1f0c3e-0000-4e47-8f71-08a1a7e2a1b4"), null,
            Guid.Parse("7a1f0c3e-0000-4e47-8f71-08a1a7e2a1b5"),
            generation ?? Guid.Parse("7a1f0c3e-0000-4e47-8f71-08a1a7e2a1b6"), 3, "Local", "app", "items", "1",
            payload, null, false, false, 5_000, StateHash, "{\"_id\":1,\"v\":1}");
}

internal sealed class ScriptedWritePrompt : IAgentWriteApprovalPrompt
{
    private readonly object _gate = new();
    private readonly List<AgentWriteApprovalPrompt> _prompts = [];

    public Func<AgentWriteApprovalPrompt, CancellationToken, Task<AgentApprovalOutcome?>> Handler { get; set; } =
        static (_, _) => Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);

    public IReadOnlyList<AgentWriteApprovalPrompt> Prompts
    {
        get { lock (_gate) return _prompts.ToArray(); }
    }

    public Task<AgentApprovalOutcome?> RequestDecisionAsync(AgentWriteApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        lock (_gate) _prompts.Add(prompt);
        return Handler(prompt, cancellationToken);
    }
}

/// <summary>Monotonic and wall clocks advanced manually; timers stay on the system clock.</summary>
internal sealed class ManualWriteTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _offsetTicks, delta.Ticks);

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));

    public override long GetTimestamp() =>
        base.GetTimestamp() + (long)(Interlocked.Read(ref _offsetTicks) * (TimestampFrequency / (double)TimeSpan.TicksPerSecond));
}

internal sealed class FakeAgentWriteSource : IAgentMongoWriteSource
{
    private int _reads;
    private int _writes;
    private readonly object _gate = new();
    private readonly List<object> _requests = [];

    public int Reads => Volatile.Read(ref _reads);
    public int Writes => Volatile.Read(ref _writes);

    public IReadOnlyList<object> Requests
    {
        get { lock (_gate) return _requests.ToArray(); }
    }

    public AgentMongoWriteSnapshot Snapshot { get; set; } =
        new(true, "{\"_id\":1,\"v\":1}", AgentWriteTestDoubles.StateHash, true, false);

    public Func<CancellationToken, Task<AgentMongoWriteResult>> OnWrite { get; set; } =
        static _ => Task.FromResult(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true));

    public Task<AgentMongoWriteSnapshot> ReadTargetAsync(ConnectionProfile profile, AgentMongoWriteTarget target,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        lock (_gate) _requests.Add(target);
        return Task.FromResult(Snapshot);
    }

    public Task<AgentMongoWriteResult> InsertOneAsync(ConnectionProfile profile, AgentMongoInsertRequest request,
        CancellationToken cancellationToken) => Write(request, cancellationToken);

    public Task<AgentMongoWriteResult> UpdateOneAsync(ConnectionProfile profile, AgentMongoUpdateRequest request,
        CancellationToken cancellationToken) => Write(request, cancellationToken);

    public Task<AgentMongoWriteResult> DeleteOneAsync(ConnectionProfile profile, AgentMongoDeleteRequest request,
        CancellationToken cancellationToken) => Write(request, cancellationToken);

    public Task<AgentMongoWriteResult> CreateIndexAsync(ConnectionProfile profile, AgentMongoCreateIndexRequest request,
        CancellationToken cancellationToken) => Write(request, cancellationToken);

    public Task<AgentMongoWriteResult> DropIndexAsync(ConnectionProfile profile, AgentMongoDropIndexRequest request,
        CancellationToken cancellationToken) => Write(request, cancellationToken);

    private Task<AgentMongoWriteResult> Write(object request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writes);
        lock (_gate) _requests.Add(request);
        return OnWrite(cancellationToken);
    }
}

internal sealed class ConcurrentMemoryAudit : IAgentAuditRepository
{
    private readonly object _gate = new();
    private readonly List<AgentAuditEvent> _events = [];

    public Func<AgentAuditEvent, bool> Fail { get; set; } = static _ => false;

    public IReadOnlyList<AgentAuditEvent> Events
    {
        get { lock (_gate) return _events.ToArray(); }
    }

    public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
    {
        if (Fail(entry)) throw new IOException("ledger unavailable");
        lock (_gate) _events.Add(entry.Validate());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
        CancellationToken cancellationToken = default) => Task.FromResult(Events);

    public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class MutablePolicyProvider : IAgentAuthorizationPolicyProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];

    public void Set(Guid principalId, long revision, AgentPermissionGrant[] grants)
    {
        lock (_gate)
            _policies[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId,
                AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);
    }

    public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_policies.GetValueOrDefault(principalId));
    }
}

internal sealed class MutableProfiles(ConnectionProfile profile) : IConnectionProfileRepository
{
    private volatile ConnectionProfile _profile = profile;

    public ConnectionProfile Profile
    {
        get => _profile;
        set => _profile = value;
    }

    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConnectionProfile>>([_profile]);

    public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
