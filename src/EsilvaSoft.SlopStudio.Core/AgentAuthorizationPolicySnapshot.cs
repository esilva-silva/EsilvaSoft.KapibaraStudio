using System.Collections.ObjectModel;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Validated immutable effective grants for one principal at one monotonically versioned policy revision.</summary>
public sealed class AgentAuthorizationPolicySnapshot
{
    public const int CurrentSchemaVersion = 1;

    private AgentAuthorizationPolicySnapshot(Guid principalId, int schemaVersion, long revision, bool isValid, IReadOnlyList<AgentPermissionGrant> grants)
    {
        PrincipalId = principalId;
        SchemaVersion = schemaVersion;
        Revision = revision;
        IsValid = isValid;
        Grants = grants;
    }

    public Guid PrincipalId { get; }
    public int SchemaVersion { get; }
    public long Revision { get; }
    public bool IsValid { get; }
    public IReadOnlyList<AgentPermissionGrant> Grants { get; }

    internal static AgentAuthorizationPolicySnapshot Load(Guid principalId, int schemaVersion, long revision, IEnumerable<AgentPermissionGrant?>? grants)
    {
        if (principalId == Guid.Empty || grants is null || schemaVersion != CurrentSchemaVersion || revision < 1)
            return Invalid(principalId, schemaVersion, revision);

        var items = grants.ToArray();
        if (items.Any(static grant => grant is null)) return Invalid(principalId, schemaVersion, revision);
        var concrete = items.Cast<AgentPermissionGrant>().ToArray();
        if (concrete.Any(grant =>
                !Enum.IsDefined(grant.Permission) || grant.PrincipalId != principalId ||
                grant.InvocationScope is null || !Enum.IsDefined(grant.InvocationScope.Kind) ||
                grant.InvocationScope.SessionId == Guid.Empty ||
                (grant.InvocationScope.Kind == AgentInvocationScopeKind.Session && grant.InvocationScope.TurnId is not null) ||
                (grant.InvocationScope.Kind == AgentInvocationScopeKind.Turn &&
                    (grant.InvocationScope.TurnId is null || grant.InvocationScope.TurnId == Guid.Empty)) ||
                grant.SourceGenerationId == Guid.Empty ||
                grant.Scope is null || grant.Destination is null || !Enum.IsDefined(grant.Destination.Kind) ||
                !Enum.IsDefined(grant.OutputDataScope)))
            return Invalid(principalId, schemaVersion, revision);

        var duplicate = concrete.GroupBy(static grant => (
            grant.PrincipalId,
            grant.InvocationScope,
            grant.SourceGenerationId,
            grant.Permission,
            grant.Scope,
            grant.Destination,
            grant.OutputDataScope)).Any(static group => group.Count() > 1);
        return duplicate
            ? Invalid(principalId, schemaVersion, revision)
            : new AgentAuthorizationPolicySnapshot(principalId, schemaVersion, revision, true, new ReadOnlyCollection<AgentPermissionGrant>(concrete));
    }

    private static AgentAuthorizationPolicySnapshot Invalid(Guid principalId, int schemaVersion, long revision) =>
        new(principalId, schemaVersion, revision, false, Array.Empty<AgentPermissionGrant>());
}
