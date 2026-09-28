using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> ConfirmConsoleWrite { get; set; } = (_, _) => Task.FromResult(false);
    public ObservableCollection<ConsoleHistoryEntry> ConsoleHistory { get; } = [];
    public ObservableCollection<LocalizedConsoleHistoryItem> LocalizedConsoleHistory { get; } = [];
    public ObservableCollection<ConsoleResultSet> ConsoleResults { get; } = [];
    public ObservableCollection<LocalizedConsoleResultItem> LocalizedConsoleResults { get; } = [];
    [ObservableProperty] private ConsoleResultSet? _selectedConsoleResult;
    [ObservableProperty] private ConsoleHistoryEntry? _selectedConsoleHistory;
    [ObservableProperty] private LocalizedConsoleResultItem? _selectedLocalizedConsoleResult;
    [ObservableProperty] private LocalizedConsoleHistoryItem? _selectedLocalizedConsoleHistory;

    partial void OnSelectedLocalizedConsoleHistoryChanged(LocalizedConsoleHistoryItem? value) => SelectedConsoleHistory = value?.Entry;
    partial void OnSelectedConsoleHistoryChanged(ConsoleHistoryEntry? value)
    {
        var item = LocalizedConsoleHistory.FirstOrDefault(candidate => ReferenceEquals(candidate.Entry, value) || candidate.Entry.Id == value?.Id);
        if (!ReferenceEquals(SelectedLocalizedConsoleHistory, item)) SelectedLocalizedConsoleHistory = item;
    }
    partial void OnSelectedLocalizedConsoleResultChanged(LocalizedConsoleResultItem? value) => SelectedConsoleResult = value?.Result;
    partial void OnSelectedConsoleResultChanged(ConsoleResultSet? value)
    {
        var item = LocalizedConsoleResults.FirstOrDefault(candidate => ReferenceEquals(candidate.Result, value));
        if (!ReferenceEquals(SelectedLocalizedConsoleResult, item)) SelectedLocalizedConsoleResult = item;
        if (_resultIsConsole) ShowSetDocuments(value is not null && _consoleSets.TryGetValue(value, out var set) ? set : null);
    }
    public bool IsConsole => Mode == "Console";
    public ConsoleStatement GetConsoleStatement(int caret) => _workspace.GetConsoleStatement(Text, caret);
}
