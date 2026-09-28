using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Workspace-global agent panel; tab changes update captured context without replacing its conversation.</summary>
public sealed partial class WorkspaceViewModel : IAgentChatHost
{
    private readonly AgentChatServicesFactory? _agentChatServices;
    private readonly IDisposable? _agentWorkspaceContextAttachment;
    private readonly IDisposable? _agentProposalTextAttachment;
    private AgentChatViewModel? _agentChat;
    private AgentPanelPreferences? _agentPanelPreferences;
    private bool _restoringAgentPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveAgentChat))]
    private bool _isAgentPanelOpen;

    public AgentChatViewModel? ActiveAgentChat => IsAgentPanelOpen ? EnsureAgentChat() : null;

    public bool IsAgentPlatformComposed => _agentChatServices is not null;

    [RelayCommand]
    private void ToggleAgentPanel() => IsAgentPanelOpen = !IsAgentPanelOpen;

    public void InitializeAgentPanel(WorkspacePreferences preferences)
    {
        _agentPanelPreferences = preferences.AgentPanel;
        if (_agentPanelPreferences is { } saved)
        {
            try { saved.Validate(); _restoringAgentPanel = true; IsAgentPanelOpen = saved.IsOpen ?? false; }
            catch (InvalidDataException) { _agentPanelPreferences = null; IsAgentPanelOpen = false; }
        }
        _restoringAgentPanel = false;
        OnPropertyChanged(nameof(ActiveAgentChat));
    }

    partial void OnIsAgentPanelOpenChanged(bool value)
    {
        if (value) EnsureAgentChat();
        OnPropertyChanged(nameof(ActiveAgentChat));
        OnPanelPreferencesChanged();
    }

    private AgentChatViewModel EnsureAgentChat()
    {
        if (_agentChat is not null)
        {
            _agentChat.ReloadProvidersIfChanged();
            return _agentChat;
        }

        var services = _agentChatServices?.GetServices() ?? AgentChatServices.Unavailable;
        _agentChat = new AgentChatViewModel(services, this, _agentPanelPreferences);
        return _agentChat;
    }

    private void OnActiveTabChangedForAgent(WorkspaceTabViewModel? tab)
    {
        _agentChat?.OnWorkspaceContextChanged();
        OnPropertyChanged(nameof(ActiveAgentChat));
    }

    private void RefreshAgentReadScopes() => _agentChat?.RefreshReadScope();

    public AgentWorkspaceContext CaptureWorkspace()
    {
        var tab = ActiveTab;
        var path = tab?.FilePath;
        var context = new AgentWorkspaceContext(DateTimeOffset.UtcNow,
            WorkspaceRootPath,
            path,
            path is null ? null : Path.GetFileName(path),
            tab?.Id.ToString("N"),
            tab?.EditorRevision,
            tab?.Text,
            tab?.Profile?.Id.ToString("D"),
            tab?.Profile?.Name,
            string.IsNullOrWhiteSpace(tab?.Database) ? null : tab!.Database,
            tab is { IsConsole: false } && !string.IsNullOrWhiteSpace(tab.Collection) ? tab.Collection : null);
        return context;
    }

    private string? ResolveAgentProposalText(string path, string? tabId)
    {
        var tab = Tabs.FirstOrDefault(candidate =>
            string.Equals(candidate.FilePath, path, FilePathComparison) &&
            (tabId is null || string.Equals(candidate.Id.ToString("N"), tabId, StringComparison.OrdinalIgnoreCase)));
        return tab?.Text;
    }

    public string? WorkspaceFolder => WorkspaceRootPath;

    public IReadOnlyList<AgentConnectionChoice> ListConnections() =>
        Profiles.Select(static profile => new AgentConnectionChoice(profile.Id, profile.Name)).ToArray();

    public async Task<IAgentBufferEditor?> OpenEditorAsync(string targetPath, string? tabId)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath) || tabId is { Length: 0 }) return null;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var tab = tabId is null
            ? Tabs.FirstOrDefault(candidate => string.Equals(candidate.FilePath, targetPath, pathComparison))
            : Tabs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id.ToString("N"), tabId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.FilePath, targetPath, pathComparison));
        if (tab is null && tabId is not null) return null;
        if (tab is null) tab = await OpenTextFileAsync(targetPath);
        if (tab is null || !Tabs.Contains(tab)) return null;
        ActiveTab = tab;

        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is MainWindow mainWindow) mainWindow.FocusEditorForAgent(tab);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (!Tabs.Contains(tab) || !string.Equals(tab.FilePath, targetPath, pathComparison)) return null;
            var editor = await Dispatcher.UIThread.InvokeAsync(() => tab.EditorBufferProvider?.Invoke());
            if (editor is not null && Tabs.Contains(tab) && string.Equals(tab.FilePath, targetPath, pathComparison)) return editor;
            await Task.Delay(25);
        }
        return null;
    }

    public void OnPanelPreferencesChanged()
    {
        if (_restoringAgentPanel) return;
        _agentPanelPreferences = (_agentChat?.CapturePreferences() ?? _agentPanelPreferences ?? new AgentPanelPreferences()) with
        {
            IsOpen = IsAgentPanelOpen,
        };
        ScheduleSave();
    }

    private async Task DisposeAgentChatAsync()
    {
        if (_agentChat is not { } chat) return;
        _agentChat = null;
        try { await chat.DisposeAsync(); } catch { /* Runtime cleanup is best effort on workspace close. */ }
    }

    private static void ReleaseAgentChat(WorkspaceTabViewModel tab) { }
}
