using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// One operation mode offered in the composer. Label and hint only: what each mode allows comes exclusively from
/// <c>AgentModePolicy</c>, never from the UI.
/// </summary>
public sealed partial class AgentModeOption : ObservableObject
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    private readonly string _labelKey;
    private readonly string _hintKey;

    private AgentModeOption(AgentOperationMode mode, string labelKey, string hintKey)
    {
        Mode = mode;
        _labelKey = labelKey;
        _hintKey = hintKey;
        _label = Text.Resolve(labelKey);
        _hint = Text.Resolve(hintKey);
    }

    public AgentOperationMode Mode { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _hint;

    internal void RefreshLocalizedText()
    {
        Label = Text.Resolve(_labelKey);
        Hint = Text.Resolve(_hintKey);
    }

    public static IReadOnlyList<AgentModeOption> All() =>
    [
        new(AgentOperationMode.Agent, "agentModeAgent", "agentModeAgentHint"),
        new(AgentOperationMode.Planning, "agentModePlanning", "agentModePlanningHint"),
        new(AgentOperationMode.Automatic, "agentModeAutomatic", "agentModeAutomaticHint"),
        new(AgentOperationMode.AskConfirmations, "agentModeAskConfirmations", "agentModeAskConfirmationsHint"),
    ];

    public override string ToString() => Label;
}

/// <summary>State of a history row: readable conversations open; the others are listed but never opened or overwritten.</summary>
public enum AgentHistoryItemKind
{
    Readable,
    Unreadable,
    UnsupportedVersion,
}

/// <summary>
/// One row of the History flyout. Rename is inline (edit, Enter confirms, Escape cancels); delete asks for an inline
/// confirmation first. Unreadable or newer-format rows are shown as "Conversa ilegível" and cannot be opened.
/// </summary>
public sealed partial class AgentHistoryItemViewModel : ObservableObject
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public AgentHistoryItemViewModel(AgentConversationSummary summary, string? providerName)
    {
        Summary = summary;
        ProviderName = providerName;
        _title = summary.Title ?? "";
    }

    public AgentConversationSummary Summary { get; }

    public Guid Id => Summary.Id;

    public string? ProviderName { get; }

    public AgentHistoryItemKind Kind => Summary.State switch
    {
        AgentConversationSummaryState.Readable => AgentHistoryItemKind.Readable,
        AgentConversationSummaryState.UnsupportedVersion => AgentHistoryItemKind.UnsupportedVersion,
        _ => AgentHistoryItemKind.Unreadable,
    };

    public bool IsReadable => Kind == AgentHistoryItemKind.Readable;

    public bool IsUnreadable => !IsReadable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    public string DisplayTitle => Kind switch
    {
        AgentHistoryItemKind.Unreadable => Text.Resolve("agentHistoryUnreadable"),
        AgentHistoryItemKind.UnsupportedVersion => Text.Resolve("agentHistoryNewerVersion"),
        _ => string.IsNullOrWhiteSpace(Title) ? Text.Resolve("agentConversationUntitled") : Title,
    };

    /// <summary>"Hoje", "Ontem" or the date — the group header of the row.</summary>
    public string DateGroup { get; internal set; } = "";

    public bool ShowsDateGroup { get; internal set; }

    public string MetaText => ProviderName is { Length: > 0 } name
        ? Text.Format("agentHistoryMeta", name, Summary.UpdatedAt?.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture) ?? "—")
        : Summary.UpdatedAt?.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture) ?? "";

    [ObservableProperty] private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRenaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConfirmingDelete;

    [ObservableProperty] private string _renameText = "";

    public bool IsIdle => !IsRenaming && !IsConfirmingDelete;

    public string OpenLabel => Text.Format("agentHistoryOpen", DisplayTitle);

    public string RenameLabel => Text.Format("agentHistoryRename", DisplayTitle);

    public string DeleteLabel => Text.Format("agentHistoryDelete", DisplayTitle);

    [RelayCommand]
    private void BeginRename()
    {
        if (!IsReadable)
        {
            return;
        }

        RenameText = DisplayTitle;
        IsConfirmingDelete = false;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private void BeginDelete()
    {
        IsRenaming = false;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;
}
