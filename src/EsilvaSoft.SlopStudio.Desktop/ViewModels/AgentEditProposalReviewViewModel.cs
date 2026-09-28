using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Hunk-by-hunk review of one registered proposal against its captured editor tab.</summary>
public sealed partial class AgentEditProposalReviewViewModel : ObservableObject
{
    private readonly IAgentBufferEditor _editor;
    private readonly AgentEditProposalStore _store;

    public AgentEditProposalReviewViewModel(AgentEditProposalEntry entry, IAgentBufferEditor editor, AgentEditProposalStore store, bool automatic)
    {
        Entry = entry;
        _editor = editor;
        _store = store;
        IsAutomatic = automatic;
        RefreshHunks();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasPending), nameof(HasApplied))]
    private AgentEditProposalEntry _entry;

    [ObservableProperty] private string? _status;

    public bool IsAutomatic { get; }
    public string Title => Path.GetFileName(Entry.Proposal.TargetPath);
    public ObservableCollection<AgentEditProposalHunkViewModel> Hunks { get; } = [];
    public bool HasPending => Entry.HasPending;
    public bool HasApplied => Entry.HasApplied;

    [RelayCommand]
    private void ApplyHunk(AgentEditProposalHunkViewModel? hunk) => ChangeHunk(hunk, apply: true);

    [RelayCommand]
    private void RevertHunk(AgentEditProposalHunkViewModel? hunk) => ChangeHunk(hunk, apply: false);

    private void ChangeHunk(AgentEditProposalHunkViewModel? hunk, bool apply)
    {
        if (hunk is null) return;
        var mutation = _store.Mutate(Entry.Id, current =>
        {
            var outcome = apply
                ? AgentEditProposalApplier.ApplyHunk(_editor, current, hunk.Index)
                : AgentEditProposalApplier.RevertHunk(_editor, current, hunk.Index);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null)
        {
            Status = LocalizationViewModel.Current.Resolve("agentProposalUnavailable");
            return;
        }

        var outcome = mutation.Result;
        if (!outcome.Succeeded)
        {
            Status = LocalizationViewModel.Current.Resolve(outcome.ErrorCode == "EditorUnavailable" ? "agentDiffEditorUnavailable" : "agentDiffActionFailed");
            return;
        }

        Entry = mutation.Entry;
        RefreshHunks();
        Status = outcome.Stale > 0 ? LocalizationViewModel.Current.Resolve("agentDiffHunkStale") : null;
    }

    [RelayCommand]
    private void KeepApplied()
    {
        var mutation = _store.Mutate(Entry.Id, current => (true, AgentEditProposalApplier.KeepApplied(current)));
        if (mutation is null) return;
        Entry = mutation.Entry;
        RefreshHunks();
        Status = LocalizationViewModel.Current.Resolve("agentDiffKept");
    }

    [RelayCommand]
    private void RevertApplied()
    {
        var mutation = _store.Mutate(Entry.Id, current =>
        {
            var outcome = AgentEditProposalApplier.RevertApplied(_editor, current, AgentEditHunkState.Reverted);
            return (outcome, outcome.Succeeded ? outcome.States : null);
        });
        if (mutation is null)
        {
            Status = LocalizationViewModel.Current.Resolve("agentProposalUnavailable");
            return;
        }

        var outcome = mutation.Result;
        if (!outcome.Succeeded)
        {
            Status = LocalizationViewModel.Current.Resolve("agentDiffEditorUnavailable");
            return;
        }

        Entry = mutation.Entry;
        RefreshHunks();
        Status = outcome.Stale > 0 ? LocalizationViewModel.Current.Resolve("agentDiffSomeStale") : LocalizationViewModel.Current.Resolve("agentDiffReverted");
    }

    private void RefreshHunks()
    {
        Hunks.Clear();
        for (var index = 0; index < Entry.Proposal.Hunks.Count; index++)
        {
            var hunk = Entry.Proposal.Hunks[index];
            Hunks.Add(new AgentEditProposalHunkViewModel(index, hunk,
                index < Entry.HunkStates.Count ? Entry.HunkStates[index] : AgentEditHunkState.Stale));
        }

        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasApplied));
    }
}

public sealed class AgentEditProposalHunkViewModel(int index, AgentEditHunk hunk, AgentEditHunkState state)
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public int Index { get; } = index;
    public string OriginalText { get; } = string.Join("\n", hunk.OriginalLines);
    public string ProposedText { get; } = string.Join("\n", hunk.ProposedLines);
    public AgentEditHunkState State { get; } = state;
    public string StateText => Text.Resolve("agentDiffHunkState." + State);
    public bool CanApply => State == AgentEditHunkState.Pending;
    public bool CanRevert => State is AgentEditHunkState.Applied or AgentEditHunkState.Kept;
}
