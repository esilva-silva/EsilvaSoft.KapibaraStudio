namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Trusted internal authorization envelope; it is never part of a model/tool argument schema.</summary>
public sealed class AgentPermissionRequest
{
    internal AgentPermissionRequest(
        AgentPrincipal? principal,
        AgentPermission? permission,
        AgentToolRisk? risk,
        AgentNamespaceScope? scope,
        long expectedPolicyRevision,
        bool connectionIsReadOnly,
        AgentInvocationContext? invocationContext = null,
        Guid? sourceGenerationId = null,
        AgentOutputDestination? destination = null,
        AgentOutputDataScope? outputDataScope = null)
    {
        Principal = principal;
        Permission = permission;
        Risk = risk;
        Scope = scope;
        ExpectedPolicyRevision = expectedPolicyRevision;
        ConnectionIsReadOnly = connectionIsReadOnly;
        InvocationContext = invocationContext;
        SourceGenerationId = sourceGenerationId;
        Destination = destination;
        OutputDataScope = outputDataScope;
    }

    public AgentPrincipal? Principal { get; }
    public AgentPermission? Permission { get; }
    public AgentToolRisk? Risk { get; }
    public AgentNamespaceScope? Scope { get; }
    public long ExpectedPolicyRevision { get; }
    public bool ConnectionIsReadOnly { get; }
    public AgentInvocationContext? InvocationContext { get; }
    public Guid? SourceGenerationId { get; }
    public AgentOutputDestination? Destination { get; }
    public AgentOutputDataScope? OutputDataScope { get; }
}
