namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>State of one hunk of an edit proposal.</summary>
public enum AgentEditHunkState
{
    /// <summary>Not decided yet; the buffer still has the original lines.</summary>
    Pending = 0,

    /// <summary>The proposed lines were applied to the editor buffer (never saved by the agent).</summary>
    Applied = 1,

    /// <summary>Rejected; the buffer keeps the original lines.</summary>
    Discarded = 2,

    /// <summary>Applied automatically (Automatic mode) and confirmed by the user.</summary>
    Kept = 3,

    /// <summary>Was applied and then reverted to the original lines.</summary>
    Reverted = 4,

    /// <summary>The buffer changed around the hunk; it can no longer be applied or reverted safely.</summary>
    Stale = 5,
}

/// <summary>
/// One contiguous change. Line numbers are zero-based line indexes. Lines carry no terminator: the editor's own line
/// ending is used when applying. <see cref="AnchorBefore"/>/<see cref="AnchorAfter"/> are up to
/// <c>LineDiff.ContextLines</c> unchanged lines around the change; fewer means the change touches the start/end of the
/// text. They locate the hunk in a buffer that changed elsewhere; a mismatch makes the hunk stale.
/// </summary>
public sealed record AgentEditHunk(
    int Index,
    int OriginalStartLine,
    IReadOnlyList<string> OriginalLines,
    IReadOnlyList<string> ProposedLines,
    AgentEditHunkState State = AgentEditHunkState.Pending)
{
    public int ProposedStartLine { get; init; }

    public IReadOnlyList<string> AnchorBefore { get; init; } = [];

    public IReadOnlyList<string> AnchorAfter { get; init; } = [];

    public int AddedLineCount => ProposedLines.Count;

    public int RemovedLineCount => OriginalLines.Count;
}

/// <summary>
/// An edit proposed by the agent through <c>propose_file_edit</c>. Target path and tab are captured when the proposal
/// is created; <see cref="BaseTextSha256"/> is the lowercase hex SHA-256 of the UTF-8 <see cref="OriginalText"/>.
/// Runtime object: conversations persist only its <see cref="Id"/>. <see cref="ToString"/> omits the texts.
/// </summary>
public sealed record AgentEditProposal(
    Guid Id,
    Guid ConversationId,
    string TargetPath,
    string? TabId,
    string BaseTextSha256,
    string OriginalText,
    string ProposedText,
    IReadOnlyList<AgentEditHunk> Hunks,
    DateTimeOffset CreatedAt)
{
    public int AddedLineCount => Hunks.Sum(static hunk => hunk.AddedLineCount);

    public int RemovedLineCount => Hunks.Sum(static hunk => hunk.RemovedLineCount);

    public override string ToString() =>
        $"{nameof(AgentEditProposal)} {{ Id = {Id}, ConversationId = {ConversationId}, Hunks = {Hunks.Count} }}";
}
