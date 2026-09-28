namespace EsilvaSoft.SlopStudio.Core;

/// <summary>An explicit capability for one principal and namespace scope.</summary>
public sealed class AgentPermissionGrant
{
    internal AgentPermissionGrant(
        Guid principalId,
        AgentInvocationScope invocationScope,
        Guid sourceGenerationId,
        AgentPermission permission,
        AgentNamespaceScope scope,
        AgentOutputDestination destination,
        AgentOutputDataScope outputDataScope)
    {
        if (principalId == Guid.Empty) throw new ArgumentException("A concessão precisa de um principal.", nameof(principalId));
        ArgumentNullException.ThrowIfNull(invocationScope);
        if (sourceGenerationId == Guid.Empty) throw new ArgumentException("A concessão precisa da geração de origem do perfil.", nameof(sourceGenerationId));
        if (!Enum.IsDefined(permission)) throw new ArgumentOutOfRangeException(nameof(permission));
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(outputDataScope)) throw new ArgumentOutOfRangeException(nameof(outputDataScope));
        PrincipalId = principalId;
        InvocationScope = invocationScope;
        SourceGenerationId = sourceGenerationId;
        Permission = permission;
        Scope = scope;
        Destination = destination;
        OutputDataScope = outputDataScope;
    }

    public Guid PrincipalId { get; }
    public AgentInvocationScope InvocationScope { get; }
    public Guid SourceGenerationId { get; }
    public AgentPermission Permission { get; }
    public AgentNamespaceScope Scope { get; }
    public AgentOutputDestination Destination { get; }
    public AgentOutputDataScope OutputDataScope { get; }
}
