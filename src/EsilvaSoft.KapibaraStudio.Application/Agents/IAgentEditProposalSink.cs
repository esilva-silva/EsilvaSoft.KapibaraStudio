using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Immediate answer of the proposal sink; the user's later decision is not part of it.</summary>
public enum AgentEditProposalSubmissionStatus
{
    /// <summary>Registered; waiting for review (or applied to the buffer in Automatic mode). Nothing was saved to disk.</summary>
    Registered = 0,

    /// <summary>The target is not open/allowed (outside the permitted targets or the workspace).</summary>
    TargetUnavailable = 1,

    /// <summary>The target buffer no longer matches <see cref="AgentEditProposal.BaseTextSha256"/>.</summary>
    BaseChanged = 2,

    /// <summary>Refused for another safe reason (limits, conversation gone).</summary>
    Rejected = 3,
}

public sealed record AgentEditProposalSubmission(AgentEditProposalSubmissionStatus Status, string? ErrorCode = null)
{
    public bool Registered => Status == AgentEditProposalSubmissionStatus.Registered;
}

/// <summary>
/// Implemented by the Desktop: receives a proposal from <c>propose_file_edit</c>, shows it in the conversation that
/// originated it and answers at once, without waiting for the user. It never writes to disk.
/// <para>
/// The agent only sees redacted text. A hunk that introduces a redaction marker absent from the original
/// (<see cref="AgentRedactionMarkers.IntroducesRedactionMarker(AgentEditHunk)"/>) would overwrite a real secret with the
/// marker: the producer (<c>propose_file_edit</c>) refuses it, and a sink must never auto-apply such a hunk
/// (<see cref="AgentProposalHandling.AutoApplyToBuffer"/> degrades to review for it).
/// </para>
/// </summary>
public interface IAgentEditProposalSink
{
    AgentEditProposalSubmission Submit(AgentEditProposal proposal);
}
