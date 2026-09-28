namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Authorization result containing no arguments, credentials or sensitive diagnostic text.</summary>
public sealed class AgentPermissionDecision
{
    internal AgentPermissionDecision(bool isAllowed, long policyRevision, AgentPermissionDenialReason reason)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (isAllowed != (reason == AgentPermissionDenialReason.None)) throw new ArgumentException("A decisão e o motivo são inconsistentes.");
        IsAllowed = isAllowed;
        PolicyRevision = policyRevision;
        Reason = reason;
    }

    public bool IsAllowed { get; }
    public long PolicyRevision { get; }
    public AgentPermissionDenialReason Reason { get; }
}
