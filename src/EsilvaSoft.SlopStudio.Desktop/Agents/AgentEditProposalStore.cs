using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Editing;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>Aggregated state of a registered proposal, shown as text on its card.</summary>
public enum AgentEditProposalStatus
{
    /// <summary>Registered; every hunk still waits for a decision.</summary>
    Registered,

    /// <summary>Every hunk was applied to the buffer (or kept).</summary>
    Applied,

    /// <summary>Some hunks were applied and others were not.</summary>
    PartiallyApplied,

    /// <summary>Every hunk was discarded or reverted: the buffer keeps the original lines.</summary>
    Discarded,

    /// <summary>At least one hunk can no longer be located and nothing else is pending.</summary>
    Stale,
}

/// <summary>
/// One registered proposal and the current state of its hunks. Immutable snapshot: the store replaces it on every
/// update. The proposal keeps <see cref="AgentEditProposal.OriginalText"/>, so the original is never lost.
/// </summary>
public sealed record AgentEditProposalEntry(AgentEditProposal Proposal, IReadOnlyList<AgentEditHunkState> HunkStates)
{
    public Guid Id => Proposal.Id;

    public AgentEditProposalStatus Status
    {
        get
        {
            var applied = HunkStates.Count(static state => state is AgentEditHunkState.Applied or AgentEditHunkState.Kept);
            var pending = HunkStates.Count(static state => state == AgentEditHunkState.Pending);
            var stale = HunkStates.Count(static state => state == AgentEditHunkState.Stale);
            if (HunkStates.Count == 0 || pending == HunkStates.Count)
            {
                return AgentEditProposalStatus.Registered;
            }

            if (applied == HunkStates.Count)
            {
                return AgentEditProposalStatus.Applied;
            }

            if (applied > 0)
            {
                return AgentEditProposalStatus.PartiallyApplied;
            }

            return stale > 0 && pending == 0 ? AgentEditProposalStatus.Stale
                : pending > 0 ? AgentEditProposalStatus.Registered
                : AgentEditProposalStatus.Discarded;
        }
    }

    public bool HasPending => HunkStates.Any(static state => state == AgentEditHunkState.Pending);

    public bool HasApplied => HunkStates.Any(static state => state is AgentEditHunkState.Applied or AgentEditHunkState.Kept);
}

/// <summary>
/// Desktop implementation of <see cref="IAgentEditProposalSink"/> and the in-memory store of edit proposals
/// (ADR-056). <see cref="Submit"/> is called by <c>propose_file_edit</c> on a broker thread: it validates the proposal
/// against the current buffer of the target (or the file on disk when it is not open) by
/// <see cref="AgentEditProposal.BaseTextSha256"/>, registers it and answers at once; the user's later decision is not
/// part of the answer. Nothing here writes to disk. Events are raised on the UI thread.
/// <para>
/// Extension point for the diff review in the editor (CLP-6): <see cref="ReviewRequested"/> is raised by the chat's
/// "Revisar" after it activated the target tab; hunk decisions are published back through <see cref="UpdateHunkStates"/>
/// so the card and the editor share one state.
/// </para>
/// </summary>
public sealed class AgentEditProposalStore : IAgentEditProposalSink
{
    /// <summary>Upper bound of proposals kept in memory; beyond it new proposals are rejected (visible to the agent).</summary>
    public const int MaximumProposals = 500;

    private readonly ConcurrentDictionary<Guid, AgentEditProposalEntry> _entries = new();
    private volatile Func<string, string?, string?>? _currentText;

    /// <summary>A proposal was registered (UI thread).</summary>
    public event EventHandler<AgentEditProposalEntry>? ProposalAdded;

    /// <summary>The hunk states of a proposal changed (UI thread).</summary>
    public event EventHandler<AgentEditProposalEntry>? ProposalUpdated;

    /// <summary>
    /// The user asked to review a proposal hunk by hunk ("Revisar"); the target tab is already active. Consumed by the
    /// editor diff review (CLP-6); without a subscriber the card keeps "Aplicar tudo"/"Descartar".
    /// </summary>
    public event EventHandler<AgentEditProposalEntry>? ReviewRequested;

    /// <summary>
    /// Attaches the read of the current buffer text of a target (path, tab ID) → text, or null when no tab shows it.
    /// It runs on the UI thread (marshalled with a short deadline). Returns a detach token.
    /// </summary>
    public IDisposable AttachTextResolver(Func<string, string?, string?> currentText)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        _currentText = currentText;
        return new Detacher(this, currentText);
    }

    public AgentEditProposalSubmission Submit(AgentEditProposal proposal)
    {
        if (proposal is null || proposal.Id == Guid.Empty || proposal.ConversationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(proposal.TargetPath) || proposal.Hunks is null || proposal.Hunks.Count == 0 ||
            string.IsNullOrWhiteSpace(proposal.BaseTextSha256) || proposal.OriginalText is null || proposal.ProposedText is null)
        {
            return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.Rejected, "ProposalInvalid");
        }

        if (_entries.Count >= MaximumProposals)
        {
            return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.Rejected, "ProposalLimitReached");
        }

        var current = ReadCurrentText(proposal.TargetPath, proposal.TabId);
        if (current is null)
        {
            return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.TargetUnavailable, "TargetUnavailable");
        }

        if (!string.Equals(Sha256(current), proposal.BaseTextSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.BaseChanged, "BaseChanged");
        }

        var entry = new AgentEditProposalEntry(proposal, [.. proposal.Hunks.Select(static hunk => hunk.State)]);
        if (!_entries.TryAdd(proposal.Id, entry))
        {
            return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.Rejected, "ProposalDuplicate");
        }

        AgentUiDispatch.Post(() => ProposalAdded?.Invoke(this, entry));
        return new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.Registered);
    }

    public bool TryGet(Guid proposalId, out AgentEditProposalEntry entry) =>
        _entries.TryGetValue(proposalId, out entry!);

    public IReadOnlyList<AgentEditProposalEntry> ForConversation(Guid conversationId) =>
        [.. _entries.Values.Where(entry => entry.Proposal.ConversationId == conversationId)
            .OrderBy(static entry => entry.Proposal.CreatedAt)];

    /// <summary>Publishes new hunk states (same count as the proposal's hunks). Returns the updated entry.</summary>
    public AgentEditProposalEntry? UpdateHunkStates(Guid proposalId, IReadOnlyList<AgentEditHunkState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (!_entries.TryGetValue(proposalId, out var entry) || states.Count != entry.Proposal.Hunks.Count ||
            states.Any(static state => !Enum.IsDefined(state)))
        {
            return null;
        }

        var updated = entry with { HunkStates = [.. states] };
        _entries[proposalId] = updated;
        AgentUiDispatch.Post(() => ProposalUpdated?.Invoke(this, updated));
        return updated;
    }

    /// <summary>Raises <see cref="ReviewRequested"/> for a registered proposal (the caller activated its tab first).</summary>
    public bool RequestReview(Guid proposalId)
    {
        if (!_entries.TryGetValue(proposalId, out var entry))
        {
            return false;
        }

        AgentUiDispatch.Post(() => ReviewRequested?.Invoke(this, entry));
        return true;
    }

    /// <summary>Whether the editor review (CLP-6) is listening.</summary>
    public bool HasReviewSubscriber => ReviewRequested is not null;

    /// <summary>Forgets the proposals of a deleted conversation (the buffers are not touched).</summary>
    public void ForgetConversation(Guid conversationId)
    {
        foreach (var id in _entries.Values.Where(entry => entry.Proposal.ConversationId == conversationId)
                     .Select(static entry => entry.Id).ToArray())
        {
            _entries.TryRemove(id, out _);
        }
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 text (the contract of <see cref="AgentEditProposal.BaseTextSha256"/>).</summary>
    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string? ReadCurrentText(string targetPath, string? tabId)
    {
        if (_currentText is { } resolver &&
            AgentUiDispatch.ReadOnUi(() => resolver(targetPath, tabId), null) is { } buffer)
        {
            return buffer;
        }

        // Not open in any tab: the base is the file on disk (bounded read; never written).
        try
        {
            if (!Path.IsPathFullyQualified(targetPath))
            {
                return null;
            }

            var info = new FileInfo(targetPath);
            if (!info.Exists || info.Length > LineDiff.MaximumInputChars * 4L)
            {
                return null;
            }

            return File.ReadAllText(targetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class Detacher(AgentEditProposalStore owner, Func<string, string?, string?> resolver) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(owner._currentText, resolver))
            {
                owner._currentText = null;
            }
        }
    }
}
