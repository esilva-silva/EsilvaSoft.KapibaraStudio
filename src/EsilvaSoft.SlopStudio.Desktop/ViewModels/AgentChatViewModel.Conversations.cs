using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class AgentChatViewModel
{
    /// <summary>Same bound as the store (per provider): beyond it new conversations are not kept.</summary>
    public const int MaximumStoredConversations = 200;

    private IReadOnlyList<AgentHistoryItemViewModel> _history = [];

    // ---- Lifecycle. ----

    private async Task InitializeAsync()
    {
        await Task.Yield();
        if (_disposed)
        {
            return;
        }

        if (SelectedProvider is { } provider)
        {
            await EnsurePermissionsAsync(provider.ProviderId);
        }

        if (_initialPreferences?.ActiveConversationId is { } conversationId && _services.Conversations is not null)
        {
            await OpenConversationAsync(conversationId, restoring: true);
        }

        StartAutomaticAvailabilityCheck();
    }

    private void AttachPorts()
    {
        if (_services.Confirmations is { } confirmations)
        {
            _attachments.Add(confirmations.Attach(OnConfirmationRequestedAsync));
        }

        if (_services.Proposals is { } proposals)
        {
            proposals.ProposalAdded += OnProposalAdded;
            proposals.ProposalUpdated += OnProposalUpdated;
            _attachments.Add(new Unsubscriber(() =>
            {
                proposals.ProposalAdded -= OnProposalAdded;
                proposals.ProposalUpdated -= OnProposalUpdated;
            }));
        }

        if (_services.Availability is { } availability)
        {
            availability.Changed += OnAvailabilityChanged;
            _attachments.Add(new Unsubscriber(() => availability.Changed -= OnAvailabilityChanged));
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    /// <summary>Panel state for the workspace session (the host adds open/width).</summary>
    public AgentPanelPreferences CapturePreferences() => new()
    {
        SelectedProviderId = SelectedProvider?.ProviderId,
        SelectedModelId = SelectedModel,
        SelectedMode = SelectedMode.Mode,
        ActiveConversationId = ActiveConversation.Revision > 0 ? ActiveConversation.Id : null,
    };

    private AgentChatConversation CreateConversation(string providerId, string? modelId)
    {
        var conversation = new AgentChatConversation(Guid.NewGuid(), providerId, modelId,
            _selectedMode?.Mode ?? AgentOperationMode.Agent, _services.Clock.GetUtcNow());
        _conversations[conversation.Id] = conversation;
        return conversation;
    }

    private void WatchConversation(AgentChatConversation conversation) =>
        conversation.PropertyChanged += (sender, e) =>
        {
            if (!ReferenceEquals(sender, ActiveConversation))
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(AgentChatConversation.DisplayTitle):
                    OnPropertyChanged(nameof(ConversationTitle));
                    break;
                case nameof(AgentChatConversation.PersistenceText) or nameof(AgentChatConversation.PersistenceIsError):
                    OnPropertyChanged(nameof(ActivePersistenceText));
                    OnPropertyChanged(nameof(HasActivePersistenceText));
                    OnPropertyChanged(nameof(ActivePersistenceIsError));
                    break;
                case nameof(AgentChatConversation.IsEmpty):
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(ShowEmptyInvitation));
                    NewConversationCommand.NotifyCanExecuteChanged();
                    break;
            }
        };

    /// <summary>Shows another conversation. The previous one keeps running if it has a turn (its results stay there).</summary>
    private void ActivateConversation(AgentChatConversation conversation)
    {
        if (ReferenceEquals(conversation, ActiveConversation))
        {
            return;
        }

        var previous = ActiveConversation;
        if (!_conversations.ContainsKey(conversation.Id))
        {
            _conversations[conversation.Id] = conversation;
        }

        WatchConversation(conversation);
        ActiveConversation = conversation;
        // An idle, empty conversation that is left behind is dropped (it was never stored).
        if (previous is { IsEmpty: true, IsBusy: false, Revision: 0 })
        {
            _conversations.Remove(previous.Id);
            _ = CloseSessionAsync(previous);
        }

        // Selectors follow the conversation shown (its own provider, model and mode).
        _suppressProviderSwitch = true;
        try
        {
            if (Providers.FirstOrDefault(p => p.ProviderId == conversation.ProviderId) is { } provider &&
                !ReferenceEquals(provider, SelectedProvider))
            {
                SelectedProvider = provider;
                LoadModels(conversation.ModelId);
            }
            else if (conversation.ModelId is { } model && Models.Contains(model))
            {
                SelectedModel = model;
            }

            if (Modes.FirstOrDefault(option => option.Mode == conversation.Mode) is { } mode)
            {
                SelectedMode = mode;
            }
        }
        finally
        {
            _suppressProviderSwitch = false;
        }

        StatusDetail = null;
        _readScopeCache = null;
        if (SelectedProvider is { } selected)
        {
            _ = EnsurePermissionsAsync(selected.ProviderId);
        }

        RefreshSendBlock();
        RefreshAutomaticChips();
        UpdateIdleState();
        _host.OnPanelPreferencesChanged();
    }

    private bool CanStartNewConversation() => IsFeatureAvailable && !(ActiveConversation.IsEmpty && ActiveConversation.Revision == 0);

    /// <summary>"Nova conversa": the current one stays in the history (and keeps its running turn, if any).</summary>
    [RelayCommand(CanExecute = nameof(CanStartNewConversation))]
    private void NewConversation()
    {
        ActivateConversation(CreateConversation(SelectedProvider?.ProviderId ?? "", SelectedModel));
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- History flyout. ----

    public ObservableCollection<AgentHistoryItemViewModel> HistoryItems { get; } = [];

    [ObservableProperty] private string _historySearch = "";

    [ObservableProperty] private bool _isHistoryLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryStatus))]
    private string _historyStatus = "";

    [ObservableProperty] private bool _historyStatusIsError;

    public bool HasHistoryStatus => HistoryStatus.Length > 0;

    public bool HasHistoryItems => HistoryItems.Count > 0;

    partial void OnHistorySearchChanged(string value) => ApplyHistoryFilter();

    /// <summary>Loads the list when the flyout opens (never on startup).</summary>
    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        HistoryStatusIsError = false;
        if (_services.Conversations is not { } repository)
        {
            _history = [];
            ApplyHistoryFilter();
            HistoryStatus = Text.Resolve("agentHistoryNoStore");
            HistoryStatusIsError = true;
            return;
        }

        IsHistoryLoading = true;
        try
        {
            var result = await repository.ListAsync(null, _lifetime.Token);
            if (!result.Succeeded)
            {
                HistoryStatus = Text.Format("agentHistoryListFailed", SafeCode(result.ErrorCode ?? result.Status.ToString()));
                HistoryStatusIsError = true;
                return;
            }

            var names = Providers.ToDictionary(static p => p.ProviderId, static p => p.Presentation.DisplayName, StringComparer.Ordinal);
            _history = [.. result.Value!.Select(summary => new AgentHistoryItemViewModel(summary,
                summary.ProviderId is { } id && names.TryGetValue(id, out var name) ? name : summary.ProviderId))];
            ApplyHistoryFilter();
            var perProvider = _history.Count(item => item.Summary.ProviderId == SelectedProvider?.ProviderId);
            HistoryStatus = perProvider >= MaximumStoredConversations
                ? Text.Format("agentHistoryLimit", MaximumStoredConversations)
                : _history.Count == 0 ? Text.Resolve("agentHistoryEmpty") : "";
            HistoryStatusIsError = perProvider >= MaximumStoredConversations;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            HistoryStatus = Text.Format("agentHistoryListFailed", "StoreFailed");
            HistoryStatusIsError = true;
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    private void ApplyHistoryFilter()
    {
        HistoryItems.Clear();
        var term = HistorySearch.Trim();
        string? lastGroup = null;
        var today = _services.Clock.GetLocalNow().Date;
        foreach (var item in _history.Where(item => term.Length == 0 ||
                     item.DisplayTitle.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
        {
            var date = item.Summary.UpdatedAt?.ToLocalTime().Date;
            item.DateGroup = date is not { } day ? Text.Resolve("agentHistoryNoDate")
                : day == today ? Text.Resolve("agentHistoryToday")
                : day == today.AddDays(-1) ? Text.Resolve("agentHistoryYesterday")
                : day.ToString("d", CultureInfo.CurrentCulture);
            item.ShowsDateGroup = item.DateGroup != lastGroup;
            lastGroup = item.DateGroup;
            item.IsActive = item.Id == ActiveConversation.Id;
            HistoryItems.Add(item);
        }

        OnPropertyChanged(nameof(HasHistoryItems));
    }

    private void RefreshHistoryActiveFlags()
    {
        foreach (var item in HistoryItems)
        {
            item.IsActive = item.Id == ActiveConversation.Id;
        }
    }

    /// <summary>Opens a readable conversation of the history. Unreadable rows cannot be opened.</summary>
    [RelayCommand]
    private Task OpenHistoryItemAsync(AgentHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        if (!item.IsReadable)
        {
            HistoryStatus = Text.Resolve(item.Kind == AgentHistoryItemKind.UnsupportedVersion
                ? "agentHistoryOpenNewerVersion" : "agentHistoryOpenUnreadable");
            HistoryStatusIsError = true;
            return Task.CompletedTask;
        }

        return OpenConversationAsync(item.Id, restoring: false);
    }

    private async Task OpenConversationAsync(Guid id, bool restoring)
    {
        if (_conversations.TryGetValue(id, out var loaded))
        {
            ActivateConversation(loaded);
            return;
        }

        AgentPersistenceResult<AgentConversation> result;
        try
        {
            result = await _services.Conversations!.GetAsync(id, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            result = AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Failed, "StoreFailed");
        }

        if (!result.Succeeded)
        {
            // Restoring a conversation that no longer exists is silent (it was erased); anything else is visible.
            if (restoring && result.Status == AgentPersistenceStatus.NotFound)
            {
                return;
            }

            var message = result.Status switch
            {
                AgentPersistenceStatus.Unreadable => Text.Resolve("agentHistoryOpenUnreadable"),
                AgentPersistenceStatus.UnsupportedVersion => Text.Resolve("agentHistoryOpenNewerVersion"),
                AgentPersistenceStatus.NotFound => Text.Resolve("agentHistoryOpenMissing"),
                _ => Text.Format("agentHistoryOpenFailed", SafeCode(result.ErrorCode ?? result.Status.ToString())),
            };
            HistoryStatus = message;
            HistoryStatusIsError = true;
            if (restoring)
            {
                StatusDetail = message;
                OnPropertyChanged(nameof(StatusText));
            }

            return;
        }

        if (_conversations.TryGetValue(id, out loaded))
        {
            ActivateConversation(loaded); // Opened meanwhile (double click).
            return;
        }

        var conversation = AgentChatConversation.FromRecord(result.Value!,
            proposalId => _services.Proposals is { } store && store.TryGet(proposalId, out var entry) ? entry : null);
        foreach (var card in conversation.Items.OfType<AgentEditProposalCardItem>())
        {
            card.ReviewHandler = ReviewProposalAsync;
        }

        _conversations[conversation.Id] = conversation;
        ActivateConversation(conversation);
        if (!restoring)
        {
            ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Confirms an inline rename (Enter).</summary>
    [RelayCommand]
    private async Task CommitRenameAsync(AgentHistoryItemViewModel? item)
    {
        if (item is not { IsRenaming: true, IsReadable: true })
        {
            return;
        }

        var title = AgentChatConversation.TitleFrom(item.RenameText);
        item.IsRenaming = false;
        if (title.Length == 0 || title == item.Title)
        {
            return;
        }

        if (_conversations.TryGetValue(item.Id, out var loaded))
        {
            loaded.Title = title;
            item.Title = title;
            await SaveConversationAsync(loaded);
            return;
        }

        var repository = _services.Conversations!;
        try
        {
            var stored = await repository.GetAsync(item.Id, _lifetime.Token);
            if (!stored.Succeeded)
            {
                HistoryStatus = Text.Format("agentHistoryRenameFailed", SafeCode(stored.ErrorCode ?? stored.Status.ToString()));
                HistoryStatusIsError = true;
                return;
            }

            var saved = await repository.SaveAsync(stored.Value! with { Title = title, UpdatedAt = _services.Clock.GetUtcNow() },
                stored.Value!.Revision, _lifetime.Token);
            if (saved.Succeeded)
            {
                item.Title = title;
                return;
            }

            HistoryStatus = saved.Status == AgentPersistenceStatus.Conflict
                ? Text.Resolve("agentHistoryRenameConflict")
                : Text.Format("agentHistoryRenameFailed", SafeCode(saved.ErrorCode ?? saved.Status.ToString()));
            HistoryStatusIsError = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    /// <summary>Confirmed delete of one conversation (inline confirmation first; unreadable rows included).</summary>
    [RelayCommand]
    private async Task ConfirmDeleteAsync(AgentHistoryItemViewModel? item)
    {
        if (item is not { IsConfirmingDelete: true })
        {
            return;
        }

        item.IsConfirmingDelete = false;
        if (_conversations.TryGetValue(item.Id, out var loaded) && loaded.IsBusy)
        {
            HistoryStatus = Text.Resolve("agentHistoryDeleteBusy");
            HistoryStatusIsError = true;
            return;
        }

        AgentPersistenceOutcome outcome;
        try
        {
            // Readable rows are deleted by revision; unreadable/newer ones only by this explicit, unconditional delete.
            outcome = await _services.Conversations!.DeleteAsync(item.Id,
                item.IsReadable ? loaded?.Revision is > 0 ? loaded.Revision : item.Summary.Revision : null, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            outcome = new AgentPersistenceOutcome(AgentPersistenceStatus.Failed, "StoreFailed");
        }

        if (!outcome.Succeeded && outcome.Status != AgentPersistenceStatus.NotFound)
        {
            HistoryStatus = outcome.Status == AgentPersistenceStatus.Conflict
                ? Text.Resolve("agentHistoryDeleteConflict")
                : Text.Format("agentHistoryDeleteFailed", SafeCode(outcome.ErrorCode ?? outcome.Status.ToString()));
            HistoryStatusIsError = true;
            return;
        }

        _services.Proposals?.ForgetConversation(item.Id);
        if (loaded is not null)
        {
            _conversations.Remove(item.Id);
            await CloseSessionAsync(loaded);
            if (ReferenceEquals(loaded, ActiveConversation))
            {
                ActivateConversation(CreateConversation(SelectedProvider?.ProviderId ?? "", SelectedModel));
            }
        }

        _history = [.. _history.Where(existing => existing.Id != item.Id)];
        ApplyHistoryFilter();
        HistoryStatus = Text.Resolve("agentHistoryDeleted");
        HistoryStatusIsError = false;
    }

    /// <summary>"Apagar histórico" in the Permissions window succeeded: stored copies of that provider are gone.</summary>
    private void OnHistoryErased(string providerId)
    {
        foreach (var conversation in _conversations.Values.Where(c => c.ProviderId == providerId).ToArray())
        {
            if (conversation.IsBusy)
            {
                // Keeps running in memory; it is stored again only as a new conversation.
                conversation.Revision = 0;
                continue;
            }

            _conversations.Remove(conversation.Id);
            _ = CloseSessionAsync(conversation);
            _services.Proposals?.ForgetConversation(conversation.Id);
        }

        if (!_conversations.ContainsKey(ActiveConversation.Id))
        {
            ActivateConversation(CreateConversation(SelectedProvider?.ProviderId ?? "", SelectedModel));
        }

        _history = [.. _history.Where(item => item.Summary.ProviderId != providerId)];
        ApplyHistoryFilter();
    }

    // ---- Persistence (IAgentConversationRepository; failures visible, conversation kept in memory). ----

    public string? ActivePersistenceText => IsHistoryDisabled && !ActiveConversation.IsEmpty
        ? Text.Resolve("agentHistoryDisabled")
        : ActiveConversation.PersistenceText;

    public bool HasActivePersistenceText => !string.IsNullOrEmpty(ActivePersistenceText);

    public bool ActivePersistenceIsError => !IsHistoryDisabled && ActiveConversation.PersistenceIsError;

    /// <summary>"Guardar histórico" is off for the selected provider.</summary>
    public bool IsHistoryDisabled => CurrentPermissions is { KeepHistory: false };

    /// <summary>Completion of the latest save of a conversation (for tests); saves of one conversation are serialized.</summary>
    internal Task LastSave { get; private set; } = Task.CompletedTask;

    private Task SaveConversationAsync(AgentChatConversation conversation)
    {
        var task = SaveConversationCoreAsync(conversation);
        LastSave = task;
        return task;
    }

    private async Task SaveConversationCoreAsync(AgentChatConversation conversation)
    {
        if (_disposed || conversation.IsEmpty || string.IsNullOrWhiteSpace(conversation.ProviderId))
        {
            return;
        }

        if (_services.Conversations is not { } repository)
        {
            SetPersistence(conversation, Text.Resolve("agentHistoryNoStore"), isError: true);
            return;
        }

        if (!_permissions.TryGetValue(conversation.ProviderId, out var slot) || slot.Value is not { } permissions)
        {
            if (slot?.Failure is not null || _services.Permissions is null)
            {
                // Without readable permissions the opt-out is unknown: nothing is written (privacy first), visibly.
                SetPersistence(conversation, Text.Resolve("agentHistoryPermissionsUnknown"), isError: true);
            }

            return; // Still loading: saved when the permissions arrive (OnPermissionsChanged).
        }

        if (!permissions.KeepHistory)
        {
            SetPersistence(conversation, null, isError: false);
            OnPropertyChanged(nameof(ActivePersistenceText));
            OnPropertyChanged(nameof(HasActivePersistenceText));
            return;
        }

        if (conversation.IsWriteBlocked)
        {
            return;
        }

        await conversation.SaveGate.WaitAsync();
        try
        {
            var result = await repository.SaveAsync(conversation.ToRecord(_services.Clock.GetUtcNow()), conversation.Revision, _lifetime.Token);
            if (result.Status == AgentPersistenceStatus.Conflict)
            {
                // Someone else wrote this conversation: adopt the stored revision and keep this window's rows (the
                // in-memory conversation is the one the user sees), with a visible notice.
                var stored = await repository.GetAsync(conversation.Id, _lifetime.Token);
                if (stored.Succeeded)
                {
                    result = await repository.SaveAsync(conversation.ToRecord(_services.Clock.GetUtcNow()), stored.Value!.Revision, _lifetime.Token);
                    if (result.Succeeded)
                    {
                        Adopt(conversation, result.Value!);
                        SetPersistence(conversation, Text.Resolve("agentPersistenceConflictMerged"), isError: false);
                        return;
                    }
                }
                else if (stored.Status is AgentPersistenceStatus.Unreadable or AgentPersistenceStatus.UnsupportedVersion)
                {
                    result = AgentPersistenceResult.Failure<AgentConversation>(stored.Status, stored.ErrorCode);
                }
            }

            switch (result.Status)
            {
                case AgentPersistenceStatus.Succeeded:
                    var first = conversation.Revision == 0;
                    Adopt(conversation, result.Value!);
                    SetPersistence(conversation, null, isError: false);
                    if (first && ReferenceEquals(conversation, ActiveConversation))
                    {
                        _host.OnPanelPreferencesChanged(); // The active conversation can now be restored.
                    }

                    break;
                case AgentPersistenceStatus.Unreadable or AgentPersistenceStatus.UnsupportedVersion:
                    conversation.IsWriteBlocked = true; // Never overwrite a stored copy this version cannot read.
                    SetPersistence(conversation, Text.Resolve("agentPersistenceStoredUnreadable"), isError: true);
                    break;
                case AgentPersistenceStatus.Invalid when result.ErrorCode == "ConversationLimitReached":
                    SetPersistence(conversation, Text.Format("agentPersistenceLimit", MaximumStoredConversations), isError: true);
                    break;
                case AgentPersistenceStatus.Conflict:
                    SetPersistence(conversation, Text.Resolve("agentPersistenceConflict"), isError: true);
                    break;
                default:
                    SetPersistence(conversation, Text.Format("agentPersistenceFailed", SafeCode(result.ErrorCode ?? result.Status.ToString())), isError: true);
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetPersistence(conversation, Text.Format("agentPersistenceFailed", "StoreFailed"), isError: true);
        }
        finally
        {
            if (!_disposed)
            {
                conversation.SaveGate.Release();
            }
        }
    }

    private static void Adopt(AgentChatConversation conversation, AgentConversation stored)
    {
        conversation.Revision = stored.Revision;
        conversation.UpdatedAt = stored.UpdatedAt;
    }

    private static void SetPersistence(AgentChatConversation conversation, string? text, bool isError)
    {
        conversation.PersistenceIsError = isError;
        conversation.PersistenceText = text;
    }

    /// <summary>The provider reported its own session ID (resume after restart); persisted with the conversation.</summary>
    internal void ReportProviderSessionId(AgentChatConversation conversation, string? providerSessionId)
    {
        if (string.IsNullOrWhiteSpace(providerSessionId) || providerSessionId.Length > 256 || providerSessionId.Any(char.IsControl) ||
            conversation.ProviderSessionId == providerSessionId)
        {
            return;
        }

        conversation.ProviderSessionId = providerSessionId;
    }
}
