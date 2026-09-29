using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    private readonly Dictionary<Guid, IAgentBufferEditor> _proposalEditors = [];

    /// <summary>The target editor tab is active and a hunk review surface can be opened over that editor.</summary>
    public event EventHandler<AgentEditProposalReviewViewModel>? ProposalReviewRequested;
    private async Task<AgentToolConfirmationDecision> OnConfirmationRequestedAsync(
        AgentToolConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (!_conversations.TryGetValue(request.ConversationId, out var conversation) || cancellationToken.IsCancellationRequested)
            return AgentToolConfirmationDecision.Rejected;
        var card = new AgentToolConfirmationCardItem(request, cancellationToken);
        conversation.Items.Add(card);
        if (ReferenceEquals(conversation, ActiveConversation)) ConfirmationShown?.Invoke(this, card);
        return await card.Decision;
    }

    private static void CloseConfirmations(AgentChatConversation conversation)
    {
        foreach (var card in conversation.Items.OfType<AgentToolConfirmationCardItem>().Where(static card => card.IsPending))
            card.Close();
    }

    private void OnProposalAdded(object? sender, AgentEditProposalEntry entry)
    {
        if (!_conversations.TryGetValue(entry.Proposal.ConversationId, out var conversation)) return;
        var card = AgentEditProposalCardItem.From(entry);
        card.ReviewHandler = ReviewProposalAsync;
        card.ApplyAllHandler = ApplyProposalAllAsync;
        card.DiscardHandler = DiscardProposalAsync;
        card.KeepHandler = KeepProposalAsync;
        card.RevertHandler = RevertProposalAsync;
        card.IsAutomatic = conversation.Mode == AgentOperationMode.Automatic;
        conversation.Items.Add(card);
        _ = SaveConversationAsync(conversation);
        if (card.IsAutomatic) _ = ApplyAutomaticProposalAsync(card);
    }

    private void OnProposalUpdated(object? sender, AgentEditProposalEntry entry)
    {
        foreach (var conversation in _conversations.Values)
            if (conversation.Items.OfType<AgentEditProposalCardItem>().FirstOrDefault(card => card.ProposalId == entry.Id) is { } card)
                card.Entry = entry;
    }

    /// <summary>Activates the captured target tab before opening the hunk review.</summary>
    private async Task ReviewProposalAsync(AgentEditProposalCardItem card)
    {
        if (card.TargetPath is null && card.TabId is null || _services.Proposals is not AgentEditProposalStore store) return;
        var editor = await _host.OpenEditorAsync(card.TargetPath, card.TabId);
        if (editor is null) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        _proposalEditors[card.ProposalId] = editor;
        store.RequestReview(card.ProposalId);
    }

    private void OnProposalReviewRequested(object? sender, AgentEditProposalEntry entry)
    {
        if (!_proposalEditors.Remove(entry.Id, out var editor)) return;
        var automatic = _conversations.TryGetValue(entry.Proposal.ConversationId, out var conversation) &&
            conversation.Mode == AgentOperationMode.Automatic;
        ProposalReviewRequested?.Invoke(this, new AgentEditProposalReviewViewModel(entry, editor, _services.Proposals!, automatic));
    }

    private async Task ApplyAutomaticProposalAsync(AgentEditProposalCardItem card)
    {
        if (card.TargetPath is null && card.TabId is null || _services.Proposals is not { } store) return;
        var editor = await _host.OpenEditorAsync(card.TargetPath, card.TabId);
        if (editor is null) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var mutation = store.Mutate(card.ProposalId, current =>
        {
            var outcome = AgentEditProposalApplier.ApplyPending(editor, current, skipRedactionMarkers: true);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null || !mutation.Result.Succeeded) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var outcome = mutation.Result;
        if (outcome.Stale > 0) card.ErrorText = Text.Resolve("agentDiffSomeStale");
        else if (outcome.Changed > 0) card.ErrorText = Text.Resolve("agentDiffAutoApplied");
    }

    private async Task ApplyProposalAllAsync(AgentEditProposalCardItem card)
    {
        if (_services.Proposals is not { } store || card.TargetPath is null && card.TabId is null) return;
        var editor = await _host.OpenEditorAsync(card.TargetPath, card.TabId);
        if (editor is null) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var mutation = store.Mutate(card.ProposalId, current =>
        {
            var outcome = AgentEditProposalApplier.ApplyPending(editor, current);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null || !mutation.Result.Succeeded) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var outcome = mutation.Result;
        card.ErrorText = outcome.Stale > 0 ? Text.Resolve("agentDiffSomeStale") : null;
    }

    private async Task DiscardProposalAsync(AgentEditProposalCardItem card)
    {
        if (_services.Proposals is not { } store || !store.TryGet(card.ProposalId, out var current)) return;
        if (!current.HasApplied)
        {
            store.Mutate(card.ProposalId, entry => (true, AgentEditProposalApplier.DiscardPending(entry)));
            return;
        }
        if (card.TargetPath is null && card.TabId is null) return;
        var editor = await _host.OpenEditorAsync(card.TargetPath, card.TabId);
        if (editor is null) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var mutation = store.Mutate(card.ProposalId, entry =>
        {
            var outcome = entry.HasApplied
                ? AgentEditProposalApplier.RevertApplied(editor, entry, AgentEditHunkState.Discarded)
                : new AgentEditApplyOutcome(true, AgentEditProposalApplier.DiscardPending(entry), 0, 0);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null || !mutation.Result.Succeeded) card.ErrorText = Text.Resolve("agentDiffEditorUnavailable");
    }

    private Task KeepProposalAsync(AgentEditProposalCardItem card)
    {
        _services.Proposals?.Mutate(card.ProposalId, entry => (true, AgentEditProposalApplier.KeepApplied(entry)));
        return Task.CompletedTask;
    }

    private async Task RevertProposalAsync(AgentEditProposalCardItem card)
    {
        if (_services.Proposals is not { } store || card.TargetPath is null && card.TabId is null) return;
        var editor = await _host.OpenEditorAsync(card.TargetPath, card.TabId);
        if (editor is null) { card.ErrorText = Text.Resolve("agentDiffEditorUnavailable"); return; }
        var mutation = store.Mutate(card.ProposalId, entry =>
        {
            var outcome = AgentEditProposalApplier.RevertApplied(editor, entry, AgentEditHunkState.Reverted);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null || !mutation.Result.Succeeded) card.ErrorText = Text.Resolve("agentDiffEditorUnavailable");
    }
}
