using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>Edits the persistent, provider-scoped permissions. Provider grants remain bounded by AgentModePolicy.</summary>
public sealed partial class AgentPermissionsViewModel : ObservableObject
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public const string SectionDataSending = "data";
    private readonly IAgentProviderPermissionsRepository? _repository;
    private readonly IAgentConversationRepository? _conversations;
    private readonly string _providerId;
    private long _revision;

    public AgentPermissionsViewModel(IAgentProviderPermissionsRepository? repository, IAgentConversationRepository? conversations,
        string providerId, string providerName, string? workspace, IReadOnlyList<AgentConnectionChoice> connections,
        bool productToolsAvailable, TimeProvider? clock, string? section = null)
    {
        _repository = repository;
        _conversations = conversations;
        _providerId = providerId;
        ProviderName = providerName;
        WorkspacePath = workspace ?? Text.Resolve("agentPermissionsNoWorkspace");
        ProductToolsAvailable = productToolsAvailable;
        foreach (var connection in connections.OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            Connections.Add(new AgentConnectionPermissionItem(connection.Id, connection.Name, true, OnConnectionSelectionChanged));
        foreach (var tool in AgentProductToolNames.ReadTools)
            ReadTools.Add(new AgentProductToolPermissionItem(tool, Text.Format("agentPermissionsReadTool", tool), false, OnReadToolSelectionChanged));
        _permissions = AgentProviderPermissions.Default(providerId);
        LoadTask = LoadAsync();
    }

    private AgentProviderPermissions _permissions;
    private bool _loadFailed;
    public Task LoadTask { get; }
    public string ProviderName { get; }
    public string WorkspacePath { get; }
    public bool ProductToolsAvailable { get; }
    public string Status { get; private set; } = Text.Resolve("agentPermissionsLoading");
    public bool IsBusy { get; private set; }
    private bool _canEdit;
    public bool CanEdit => _canEdit && !IsBusy;
    public bool CanSave => CanEdit && _repository is not null && _permissions.IsWellFormed;
    public bool CanDeleteHistory => CanEdit && _conversations is not null;
    public bool CanRetryLoad => _loadFailed && !IsBusy && !_canEdit && _repository is not null;
    public bool IsConfirmingHistoryDelete { get; private set; }
    public ObservableCollection<AgentConnectionPermissionItem> Connections { get; } = [];
    public bool HasConnections => Connections.Count > 0;
    public ObservableCollection<AgentProductToolPermissionItem> ReadTools { get; } = [];
    public bool AutomaticActiveFile { get => _permissions.AutomaticContext.ActiveFile; set => Change(p => p with { AutomaticContext = p.AutomaticContext with { ActiveFile = value } }); }
    public bool AutomaticTabMetadata { get => _permissions.AutomaticContext.TabMetadata; set => Change(p => p with { AutomaticContext = p.AutomaticContext with { TabMetadata = value } }); }
    public bool LimitConnections
    {
        get => _permissions.ConnectionScope == AgentConnectionScope.Selected;
        set
        {
            if (!CanEdit || value == LimitConnections) return;
            var selected = value ? Connections.Where(static item => item.IsSelected).Select(static item => item.Id).ToArray() : _permissions.SelectedConnectionIds;
            Change(p => p with { ConnectionScope = value ? AgentConnectionScope.Selected : AgentConnectionScope.All, SelectedConnectionIds = selected });
        }
    }
    public bool HasConsent => _permissions.HasExternalDestinationConsent;
    public string ConsentStatusText => Text.Resolve(HasConsent ? "agentPermissionsConsentActive" : "agentPermissionsNoConsent");
    public string ProductToolsStatus => ProductToolsAvailable
        ? Text.Format("agentPermissionsToolsReadOnly", string.Join(", ", AgentProductToolNames.ReadTools))
        : Text.Resolve("agentPermissionsToolsUnavailable");
    public string DeleteHistoryConfirmation => Text.Format("agentPermissionsConfirmDeleteHistory", ProviderName);
    public bool ActiveFile { get => _permissions.DataSending.ActiveFile; set => Change(p => p with { DataSending = p.DataSending with { ActiveFile = value } }); }
    public bool WorkspaceFiles { get => _permissions.DataSending.WorkspaceFiles; set => Change(p => p with { DataSending = p.DataSending with { WorkspaceFiles = value } }); }
    public bool ExternalAttachments { get => _permissions.DataSending.ExternalAttachments; set => Change(p => p with { DataSending = p.DataSending with { ExternalAttachments = value } }); }
    public bool TabMetadata { get => _permissions.DataSending.TabMetadata; set => Change(p => p with { DataSending = p.DataSending with { TabMetadata = value } }); }
    public bool InferredSchema { get => _permissions.DataSending.InferredSchema; set => Change(p => p with { DataSending = p.DataSending with { InferredSchema = value } }); }
    public bool UseWorkspace { get => _permissions.Workspace.UseFilesFolder; set => Change(p => p with { Workspace = p.Workspace with { UseFilesFolder = value } }); }
    public bool NativeFileRead { get => _permissions.NativeFileRead; set => Change(p => p with { NativeFileRead = value }); }
    public bool NativeCommandExecution { get => _permissions.NativeCommandExecution; set => Change(p => p with { NativeCommandExecution = value }); }
    public bool NativeFileWrite { get => _permissions.NativeFileWrite; set => Change(p => p with { NativeFileWrite = value }); }
    public bool NativeNetwork { get => _permissions.NativeNetwork; set => Change(p => p with { NativeNetwork = value }); }
    public bool EditActiveFile { get => _permissions.EditProposals.ActiveFile; set => Change(p => p with { EditProposals = p.EditProposals with { ActiveFile = value } }); }
    public bool EditOtherFiles { get => _permissions.EditProposals.OtherWorkspaceFiles; set => Change(p => p with { EditProposals = p.EditProposals with { OtherWorkspaceFiles = value } }); }
    public bool KeepHistory { get => _permissions.KeepHistory; set => Change(p => p with { KeepHistory = value }); }
    public bool ConfirmMongoReads { get => Has(AgentConfirmationCategories.MongoMetadataRead); set => Confirm(AgentConfirmationCategories.MongoMetadataRead, value); }
    public bool ConfirmWorkspaceReads { get => Has(AgentConfirmationCategories.WorkspaceContextRead); set => Confirm(AgentConfirmationCategories.WorkspaceContextRead, value); }
    public bool ConfirmNativeReads { get => Has(AgentConfirmationCategories.NativeFileRead); set => Confirm(AgentConfirmationCategories.NativeFileRead, value); }
    public bool ConfirmEdits { get => Has(AgentConfirmationCategories.EditProposal); set => Confirm(AgentConfirmationCategories.EditProposal, value); }
    public Action<AgentProviderPermissions>? Saved { get; set; }
    public Func<string, Task>? HistoryErased { get; set; }

    private bool Has(AgentConfirmationCategories category) => (_permissions.ConfirmationCategories & category) != 0;
    private void Confirm(AgentConfirmationCategories category, bool on) => Change(p => p with { ConfirmationCategories = on ? p.ConfirmationCategories | category : p.ConfirmationCategories & ~category });
    private void Change(Func<AgentProviderPermissions, AgentProviderPermissions> update)
    {
        if (!CanEdit) return;
        _permissions = update(_permissions);
        foreach (var name in new[] { nameof(ActiveFile), nameof(WorkspaceFiles), nameof(ExternalAttachments), nameof(TabMetadata), nameof(InferredSchema), nameof(UseWorkspace), nameof(NativeFileRead), nameof(NativeCommandExecution), nameof(NativeFileWrite), nameof(NativeNetwork), nameof(EditActiveFile), nameof(EditOtherFiles), nameof(KeepHistory), nameof(ConfirmMongoReads), nameof(ConfirmWorkspaceReads), nameof(ConfirmNativeReads), nameof(ConfirmEdits), nameof(AutomaticActiveFile), nameof(AutomaticTabMetadata), nameof(HasConsent), nameof(ConsentStatusText), nameof(LimitConnections), nameof(DeleteHistoryConfirmation), nameof(CanDeleteHistory), nameof(CanSave) }) OnPropertyChanged(name);
        NotifyCommandStates();
    }

    private void OnConnectionSelectionChanged(AgentConnectionPermissionItem connection, bool selected)
    {
        if (!CanEdit || !LimitConnections) return;
        var selectedIds = new HashSet<Guid>(_permissions.SelectedConnectionIds ?? []);
        if (selected) selectedIds.Add(connection.Id); else selectedIds.Remove(connection.Id);
        Change(p => p with { SelectedConnectionIds = [.. selectedIds] });
    }

    private void OnReadToolSelectionChanged(AgentProductToolPermissionItem tool, bool selected)
    {
        if (!CanEdit || !ProductToolsAvailable) return;
        var enabled = new HashSet<string>(_permissions.EnabledReadTools ?? [], StringComparer.Ordinal);
        if (selected) enabled.Add(tool.Name); else enabled.Remove(tool.Name);
        Change(p => p with { EnabledReadTools = [.. enabled] });
    }

    private void SyncConnectionSelection()
    {
        var selected = new HashSet<Guid>(_permissions.SelectedConnectionIds ?? []);
        foreach (var item in Connections) item.SetSelected(_permissions.ConnectionScope == AgentConnectionScope.All || selected.Contains(item.Id));
        var enabledTools = new HashSet<string>(_permissions.EnabledReadTools ?? [], StringComparer.Ordinal);
        foreach (var tool in ReadTools) tool.SetSelected(enabledTools.Contains(tool.Name));
        OnPropertyChanged(nameof(LimitConnections));
    }
    private async Task LoadAsync()
    {
        if (_repository is null) { SetLoadFailure(Text.Resolve("agentPermissionsUnavailable")); return; }
        try
        {
            var result = await _repository.LoadAsync(_providerId, CancellationToken.None);
            if (result.Status == AgentPersistenceStatus.Succeeded) { _permissions = result.Value!; _revision = _permissions.Revision; }
            else if (result.Status != AgentPersistenceStatus.NotFound) { SetLoadFailure(Text.Resolve("agentPermissionsLoadFailed")); return; }
            _loadFailed = false;
            _canEdit = true;
            SyncConnectionSelection();
            Status = Text.Resolve(_permissions.HasExternalDestinationConsent ? "agentPermissionsConsentActive" : "agentPermissionsNoConsent");
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanDeleteHistory));
            OnPropertyChanged(nameof(CanRetryLoad));
            NotifyCommandStates();
            OnPropertyChanged(string.Empty);
        }
        catch { SetLoadFailure(Text.Resolve("agentPermissionsLoadRetry")); }
    }
    private void SetLoadFailure(string message)
    {
        _canEdit = false;
        _loadFailed = true;
        Status = message;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDeleteHistory));
        OnPropertyChanged(nameof(CanRetryLoad));
        NotifyCommandStates();
    }
    [RelayCommand(CanExecute = nameof(CanRetryLoad))]
    private async Task RetryLoadAsync()
    {
        if (!CanRetryLoad) return;
        IsBusy = true;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRetryLoad));
        RetryLoadCommand.NotifyCanExecuteChanged();
        try { await LoadAsync(); }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanDeleteHistory));
            OnPropertyChanged(nameof(CanRetryLoad));
            RetryLoadCommand.NotifyCanExecuteChanged();
            NotifyCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteHistory))]
    private void BeginDeleteHistory()
    {
        if (!CanDeleteHistory) return;
        IsConfirmingHistoryDelete = true;
        OnPropertyChanged(nameof(IsConfirmingHistoryDelete));
        ConfirmDeleteHistoryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CancelDeleteHistory()
    {
        IsConfirmingHistoryDelete = false;
        OnPropertyChanged(nameof(IsConfirmingHistoryDelete));
        ConfirmDeleteHistoryCommand.NotifyCanExecuteChanged();
    }

    private bool CanConfirmDeleteHistory() => CanDeleteHistory && IsConfirmingHistoryDelete;

    private void NotifyCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        AuthorizeDestinationCommand.NotifyCanExecuteChanged();
        RevokeDestinationCommand.NotifyCanExecuteChanged();
        BeginDeleteHistoryCommand.NotifyCanExecuteChanged();
        ConfirmDeleteHistoryCommand.NotifyCanExecuteChanged();
        RetryLoadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmDeleteHistory))]
    private async Task ConfirmDeleteHistoryAsync()
    {
        if (!CanConfirmDeleteHistory() || _conversations is not { } conversations) return;
        IsBusy = true;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDeleteHistory));
        ConfirmDeleteHistoryCommand.NotifyCanExecuteChanged();
        try
        {
            Status = Text.Resolve("agentPermissionsDeleting");
            OnPropertyChanged(nameof(Status));
            var result = await conversations.DeleteAllAsync(_providerId, CancellationToken.None);
            if (result.Succeeded)
            {
                if (HistoryErased is { } historyErased) await historyErased(_providerId);
                Status = Text.Format("agentPermissionsHistoryDeleted", result.Value);
                IsConfirmingHistoryDelete = false;
                OnPropertyChanged(nameof(IsConfirmingHistoryDelete));
            }
            else
            {
                Status = Text.Resolve("agentPermissionsHistoryDeleteFailed");
            }
        }
        catch { Status = Text.Resolve("agentPermissionsHistoryDeleteFailed"); }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanDeleteHistory));
            ConfirmDeleteHistoryCommand.NotifyCanExecuteChanged();
        }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AuthorizeDestinationAsync()
    {
        if (!HasConsent) Change(p => p with { ExternalDestinationConsentAt = DateTimeOffset.UtcNow });
        await SaveAsync();
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RevokeDestinationAsync() { Change(p => p with { ExternalDestinationConsentAt = null }); await SaveAsync(); }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave || _repository is not { } repository) return;
        IsBusy = true; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave)); SaveCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await repository.SaveAsync(_permissions, _revision, CancellationToken.None);
            if (result.Succeeded) { _permissions = result.Value!; _revision = _permissions.Revision; Saved?.Invoke(_permissions); Status = Text.Resolve("agentPermissionsSaved"); }
            else
            {
                Status = result.Status == AgentPersistenceStatus.Conflict ? Text.Resolve("agentPermissionsConflict") : Text.Resolve("agentPermissionsSaveFailed");
                if (result.Status == AgentPersistenceStatus.Conflict)
                {
                    _canEdit = false;
                    OnPropertyChanged(nameof(CanEdit));
                }
            }
        }
        catch { Status = Text.Resolve("agentPermissionsSaveFailed"); }
        finally { IsBusy = false; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanDeleteHistory)); NotifyCommandStates(); }
    }
}

public sealed partial class AgentConnectionPermissionItem : ObservableObject
{
    private readonly Action<AgentConnectionPermissionItem, bool> _changed;
    private bool _suppressChanged;
    public AgentConnectionPermissionItem(Guid id, string name, bool isSelected, Action<AgentConnectionPermissionItem, bool> changed)
    { Id = id; Name = name; _isSelected = isSelected; _changed = changed; }
    public Guid Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) { if (!_suppressChanged) _changed(this, value); }
    internal void SetSelected(bool value)
    {
        if (IsSelected == value) return;
        _suppressChanged = true;
        try { IsSelected = value; }
        finally { _suppressChanged = false; }
    }
}

public sealed partial class AgentProductToolPermissionItem : ObservableObject
{
    private readonly Action<AgentProductToolPermissionItem, bool> _changed;
    private bool _suppressChanged;
    public AgentProductToolPermissionItem(string name, string displayName, bool enabled, Action<AgentProductToolPermissionItem, bool> changed)
    { Name = name; DisplayName = displayName; _isEnabled = enabled; _changed = changed; }
    public string Name { get; }
    public string DisplayName { get; }
    [ObservableProperty] private bool _isEnabled;
    partial void OnIsEnabledChanged(bool value) { if (!_suppressChanged) _changed(this, value); }
    internal void SetSelected(bool value)
    {
        if (IsEnabled == value) return;
        _suppressChanged = true;
        try { IsEnabled = value; }
        finally { _suppressChanged = false; }
    }
}
