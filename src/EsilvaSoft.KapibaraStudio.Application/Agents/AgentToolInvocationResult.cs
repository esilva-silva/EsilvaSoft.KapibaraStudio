using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Normalized result shared by internal chat and protocol adapters.</summary>
public sealed class AgentToolInvocationResult
{
    private AgentToolInvocationResult(bool succeeded, string? errorCode, string? structuredContentJson,
        AgentAuditDecisionReason? auditReason = null, IReadOnlyList<ConnectionProfile>? releaseProfiles = null,
        AgentWorkspaceContext? releaseWorkspaceContext = null)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
        StructuredContentJson = structuredContentJson;
        AuditReason = auditReason;
        ReleaseProfiles = releaseProfiles;
        ReleaseWorkspaceContext = releaseWorkspaceContext;
    }

    public bool Succeeded { get; }
    public string? ErrorCode { get; }
    public string? StructuredContentJson { get; }
    internal AgentAuditDecisionReason? AuditReason { get; }
    internal IReadOnlyList<ConnectionProfile>? ReleaseProfiles { get; }
    internal AgentWorkspaceContext? ReleaseWorkspaceContext { get; }

    internal static AgentToolInvocationResult Success(string structuredContentJson) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)));

    internal static AgentToolInvocationResult Success(string structuredContentJson, ConnectionProfile profile) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)),
            releaseProfiles: [profile ?? throw new ArgumentNullException(nameof(profile))]);

    internal static AgentToolInvocationResult Success(string structuredContentJson,
        IReadOnlyList<ConnectionProfile> profiles) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)),
            releaseProfiles: profiles?.ToArray() ?? throw new ArgumentNullException(nameof(profiles)));

    internal static AgentToolInvocationResult SuccessWorkspaceContext(string structuredContentJson,
        AgentWorkspaceContext snapshot) =>
        new(true, null, structuredContentJson ?? throw new ArgumentNullException(nameof(structuredContentJson)),
            releaseWorkspaceContext: snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    internal static AgentToolInvocationResult Failure(string errorCode) =>
        new(false, errorCode ?? throw new ArgumentNullException(nameof(errorCode)), null);

    internal static AgentToolInvocationResult Failure(string errorCode, AgentAuditDecisionReason auditReason) =>
        // A source failure is not fixed by granting more permissions. Keep the audit reason and expose only
        // its safe category, without propagating the driver's exception or connection credentials.
        new(false, errorCode == "PermissionDenied" && auditReason == AgentAuditDecisionReason.ExecutionFailed
            ? "ExecutionFailed" : errorCode ?? throw new ArgumentNullException(nameof(errorCode)), null, auditReason);
}
