using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public enum AgentChatState
{
    Unavailable,
    NoProvider,
    ProviderUnavailable,
    NotAuthenticated,
    CredentialExpired,
    VaultUnavailable,
    Ready,
    Connecting,
    Generating,
    WaitingTool,
    WaitingApproval,
    Cancelling,
    Completed,
    Cancelled,
    OutcomeUnknown,
    TimedOut,
    Failed,

    /// <summary>The chips of the message were refused by the attachment resolver: nothing was sent.</summary>
    ContextFailed,

    /// <summary>A CLI-delegated provider has no usable working directory: sending is disabled (no fallback).</summary>
    ReadScopeUnavailable,
}

/// <summary>
/// The single, workspace-global AI Agent panel (ADR-056, P7-CLP-5). It is owned by the <c>WorkspaceViewModel</c> and
/// is not bound to any tab: switching tabs never switches the conversation. Conversations are persisted through
/// <see cref="IAgentConversationRepository"/> (respecting the "Guardar histórico" opt-out, failures visible), permissions
/// through <see cref="IAgentProviderPermissionsRepository"/>. What a turn may do comes only from
/// <see cref="AgentModePolicy"/>; the context travels as chips resolved by <see cref="AgentAttachmentResolver"/>,
/// captured synchronously on the UI thread before any await. A turn belongs to the conversation that started it (own
/// session, own CTS) and its results go only there, even when another conversation is visible. Nothing here branches
/// on a provider brand: providers are described by capabilities (e.g. <see cref="AgentProviderPresentation.SupportsTurnPlan"/>).
/// </summary>
public sealed partial class AgentChatViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AgentChatServices _services;
    private readonly IAgentChatHost _host;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, AgentChatConversation> _conversations = [];
    private readonly List<IDisposable> _attachments = [];
    private readonly AgentPanelPreferences? _initialPreferences;
    private bool _suppressProviderSwitch;
    private bool _disposed;
    private IReadOnlyList<AgentProviderPresentation>? _lastListing;

    /// <param name="services">Chat services; missing pieces make the corresponding feature unavailable.</param>
    /// <param name="host">The workspace: context capture (UI thread), connections, editors and preferences.</param>
    /// <param name="preferences">Panel state restored from the workspace session (provider, model, mode, conversation).</param>
    public AgentChatViewModel(AgentChatServices services, IAgentChatHost host, AgentPanelPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        _services = services;
        WorkspaceFilePicker = new AgentWorkspaceFilePickerViewModel(services.PathProbe, services.FileCatalog);
        _host = host;
        _initialPreferences = preferences;
        Modes = AgentModeOption.All();
        _selectedMode = Modes.FirstOrDefault(option => option.Mode == preferences?.SelectedMode) ?? Modes[0];
        _activeConversation = CreateConversation(preferences?.SelectedProviderId ?? "", null);
        Chips.CollectionChanged += (_, _) => ScheduleContextMeasurement();
        LocalizationViewModel.Current.PropertyChanged += OnLocalizationChanged;
        AttachPorts();
        LoadProviders(preferences?.SelectedProviderId, preferences?.SelectedModelId);
        ActiveConversation.ProviderId = SelectedProvider?.ProviderId ?? "";
        ActiveConversation.ModelId = _copilotModelChoiceRequired ? preferences?.SelectedModelId : SelectedModel;
        ActiveConversation.Mode = SelectedMode.Mode;
        WatchConversation(ActiveConversation);
        RefreshAutomaticChips();
        Initialization = InitializeAsync();
    }

    public event EventHandler<AgentApprovalViewModel>? ApprovalRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>The user asked to open the Permissions window (argument: section key or null).</summary>
    public event EventHandler<string?>? PermissionsRequested;

    /// <summary>Asks the view to move focus back to the composer after a user action; streaming never raises it.</summary>
    public event EventHandler? ComposerFocusRequested;

    /// <summary>A confirmation card was added to the visible conversation (the view focuses "Rejeitar").</summary>
    public event EventHandler<AgentToolConfirmationCardItem>? ConfirmationShown;

    /// <summary>"Arquivo externo…": the view opens the native file dialog and calls <see cref="AddExternalFile"/>.</summary>
    public event EventHandler? ExternalFilePickRequested;

    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    /// <summary>Restores permissions, the active conversation and starts the automatic availability check.</summary>
    public Task Initialization { get; }

    public ObservableCollection<AgentProviderOption> Providers { get; } = [];

    public ObservableCollection<string> Models { get; } = [];

    public IReadOnlyList<AgentModeOption> Modes { get; }

    /// <summary>The conversation shown by the panel. Never null (an empty one when nothing was opened).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items), nameof(ConversationTitle), nameof(IsEmpty), nameof(ShowEmptyInvitation),
        nameof(ActivePersistenceText), nameof(HasActivePersistenceText), nameof(ActivePersistenceIsError), nameof(ShowStatusLine), nameof(ShowStatusArea))]
    private AgentChatConversation _activeConversation;

    /// <summary>Rows of the visible conversation.</summary>
    public ObservableCollection<AgentChatItemViewModel> Items => ActiveConversation.Items;

    public string ConversationTitle => ActiveConversation.DisplayTitle;

    public bool IsEmpty => ActiveConversation.IsEmpty;

    /// <summary>The empty-conversation invitation is shown only when chatting is actually possible.</summary>
    public bool ShowEmptyInvitation => IsEmpty && IsProviderUsable;

    public bool IsFeatureAvailable => _services.IsComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExternalDestination), nameof(DestinationText), nameof(DestinationHint),
        nameof(HasModels), nameof(IsStatusError), nameof(ProviderSummary), nameof(ModeText), nameof(HasModeText), nameof(ReadScopeText),
        nameof(HasReadScope), nameof(ReadScopeSummary), nameof(IsReadScopeCritical), nameof(SupportsTurnPlan), nameof(AreChipsEnabled),
        nameof(ShowChipsDisabledNotice), nameof(IsCopilotSubscriptionSelected))]
    private AgentProviderOption? _selectedProvider;

    [ObservableProperty] private string? _selectedModel;
    private bool _updatingModelCatalog;
    private bool _copilotModelChoiceRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModeHint))]
    private AgentModeOption _selectedMode;

    public string SelectedModeHint => SelectedMode.Hint;

    [ObservableProperty] private string _composerText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusSummary), nameof(IsStatusError), nameof(IsBusy), nameof(IsIdle),
        nameof(CanChangeProvider), nameof(ShowStatusLine), nameof(ShowStatusArea), nameof(ShowEmptyInvitation), nameof(ShowRefreshProviders))]
    private AgentChatState _state;

    /// <summary>Idle notice of the panel (e.g. provider switched); turn details belong to the conversation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusSummary))]
    private string? _statusDetail;

    [ObservableProperty] private bool _isRefreshingProviders;

    public bool HasModels => Models.Count > 0;

    public bool IsExternalDestination => SelectedProvider?.IsExternal == true;

    public string DestinationText => SelectedProvider?.DestinationText ?? "";

    public string DestinationHint => SelectedProvider?.DestinationHint ?? "";

    /// <summary>Mode chip next to Local/Externo; CLI providers are labeled by their delegated runtime, API providers by billing mode.</summary>
    public string ProviderSummary => SelectedProvider?.ModeText ?? SelectedProvider?.Label ?? Text.Resolve("agentProvider");

    /// <summary>Shows the Copilot-specific data and plan disclosure before each send.</summary>
    public bool IsCopilotSubscriptionSelected => string.Equals(SelectedProvider?.ProviderId,
        AgentProviderIds.GitHubCopilotSubscription, StringComparison.Ordinal);

    public bool ShowStatusLine => State != AgentChatState.Generating ||
        !Items.OfType<AgentChatMessageItem>().Any(static message => message.IsStreaming);

    public bool ShowStatusArea => ShowStatusLine || HasActivePersistenceText || HistoryStatusIsError;

    public string StatusSummary => State == AgentChatState.Ready && string.IsNullOrEmpty(StatusDetail) && !IsCopilotModelSelectionRequired
        ? Text.Resolve("agentReadyCompact") : StatusText;

    public string? ModeText => SelectedProvider?.ModeText;

    public bool HasModeText => ModeText is not null;

    /// <summary>The provider honors plan, system prompt and chips (<see cref="AgentProviderCapabilities.TurnPlan"/>).</summary>
    public bool SupportsTurnPlan => SelectedProvider?.Presentation.SupportsTurnPlan == true;

    /// <summary>The visible conversation runs a turn.</summary>
    public bool IsBusy => ActiveConversation.IsBusy;

    public bool IsIdle => !IsBusy;

    public bool CanChangeProvider => IsIdle && IsFeatureAvailable;

    /// <summary>Completion of the visible conversation's running turn, for hosts and tests; null when idle.</summary>
    public Task? CurrentTurnCompletion => ActiveConversation.Turn?.Completion;

    public AgentTurnId? ActiveTurnId => ActiveConversation.Turn?.TurnId;

    /// <summary>
    /// Error styling accompanies the text of failure states; a provider whose availability was not checked yet is a
    /// neutral state with its own action, not an error.
    /// </summary>
    public bool IsStatusError => (State == AgentChatState.ProviderUnavailable && SelectedProvider?.IsNotChecked != true) ||
        State is AgentChatState.Unavailable or AgentChatState.NoProvider or AgentChatState.NotAuthenticated or AgentChatState.CredentialExpired or
        AgentChatState.VaultUnavailable or AgentChatState.OutcomeUnknown or AgentChatState.TimedOut or
        AgentChatState.Failed or AgentChatState.ContextFailed or AgentChatState.ReadScopeUnavailable;

    /// <summary>Prefix of localized, provider-reported safe error codes (looked up by code, never by provider).</summary>
    public const string ErrorCodePrefix = "agentError.";

    private bool UsesOfficialCli => SelectedProvider?.UsesOfficialCli == true;

    private string? ActiveDetail => ActiveConversation.TurnState is not null ? ActiveConversation.TurnDetail : StatusDetail;

    public string StatusText
    {
        get
        {
            if (IsIdle && IsCopilotModelSelectionRequired)
                return Text.Resolve("agentCopilotChooseEligibleModel");
            var name = SelectedProvider?.Presentation.DisplayName ?? "";
            var detail = ActiveDetail;
            var text = State switch
            {
                AgentChatState.Unavailable => Text.Resolve("agentStateUnavailable"),
                AgentChatState.NoProvider => Text.Resolve("agentStateNoProvider"),
                AgentChatState.ProviderUnavailable => Text.Format("agentStateProviderUnavailable", name,
                    SelectedProvider?.UnavailableText ?? Text.Resolve("agentSettingsUnavailable")),
                AgentChatState.NotAuthenticated => Text.Format(UsesOfficialCli ? "agentStateCliSignedOut" : "agentStateNotAuthenticated", name),
                AgentChatState.CredentialExpired => UsesOfficialCli
                    ? Text.Format("agentStateCliBlocked", name, SelectedProvider?.UnavailableText ?? "")
                    : Text.Format("agentStateCredentialExpired", name),
                AgentChatState.VaultUnavailable => Text.Format("agentStateVaultUnavailable", name),
                AgentChatState.Ready => Text.Resolve("agentStateReady"),
                AgentChatState.ReadScopeUnavailable => Text.Format("agentStateReadScopeUnavailable", name),
                AgentChatState.Connecting => Text.Format("agentStateConnecting", name),
                AgentChatState.Generating => Text.Resolve("agentStateGenerating"),
                AgentChatState.WaitingTool => Text.Resolve("agentStateWaitingTool"),
                AgentChatState.WaitingApproval => Text.Resolve("agentStateWaitingApproval"),
                AgentChatState.Cancelling => Text.Resolve("agentStateCancelling"),
                AgentChatState.Completed => Text.Resolve("agentStateCompleted"),
                AgentChatState.Cancelled => Text.Resolve("agentStateCancelled"),
                AgentChatState.OutcomeUnknown => Described(detail) is { } uncertain
                    ? Text.Format("agentStateOutcomeUnknownDescribed", uncertain)
                    : Text.Resolve("agentStateOutcomeUnknown"),
                AgentChatState.TimedOut => Text.Resolve("agentStateTimedOut"),
                AgentChatState.ContextFailed => Text.Resolve("agentStateAttachmentsRefused"),
                _ => detail is { } code && Text.HasTranslation(ErrorCodePrefix + code)
                    ? Text.Format("agentStateFailedDescribed", Text.Resolve(ErrorCodePrefix + code), code)
                    : Text.Format("agentStateFailed", detail ?? "AgentFailure"),
            };
            return State is AgentChatState.Ready && StatusDetail is { Length: > 0 } notice ? notice : text;
        }
    }

    private static string? Described(string? code) =>
        code is { Length: > 0 } && Text.HasTranslation(ErrorCodePrefix + code) ? Text.Resolve(ErrorCodePrefix + code) : null;

    // ---- Read scope of CLI-delegated providers (the folder their native reads use in a new session). ----

    private (string ProviderId, string? Candidate, AgentCliReadScope Scope, AgentCliProviderProfile Profile)? _readScopeCache;

    /// <summary>
    /// Permanent read notice of a CLI-delegated provider: the folder its native read tools may read in a new session
    /// (or none) and that what is read goes to the recipient. Empty otherwise.
    /// </summary>
    public string ReadScopeText
    {
        get
        {
            if (IsCopilotSubscriptionSelected)
            {
                // Copilot exposes product tools only. Its account handler has no native CLI read scope;
                // that absence says nothing about the folder captured for the product tools in the next turn.
                var candidate = SessionFolderCandidate();
                var description = candidate is null ? Text.Resolve("agentCopilotNoWorkspaceScope")
                    : Text.Format("agentCopilotWorkspaceScope", candidate);
                return description;
            }
            if (CurrentReadScope() is not { } current)
            {
                return "";
            }

            var (scope, profile) = current;
            var text = AgentCliReadScopeText.Describe(scope, profile);
            if (scope.BlocksSending)
            {
                return text;
            }

            var conversation = ActiveConversation;
            var pinned = conversation.HasSessionWorkingDirectory && conversation.SessionProviderId == SelectedProvider!.ProviderId &&
                !string.Equals(conversation.SessionWorkingDirectory, scope.CandidateDirectory, StringComparison.Ordinal)
                    ? " " + Text.Format("agentCliReadScopeSessionPinned", conversation.SessionWorkingDirectory ?? Text.Resolve("agentCliReadScopeNoFolder"))
                    : "";
            return text + " " + Text.Resolve("agentCliReadScopeNewSessions") + pinned;
        }
    }

    public bool HasReadScope => ReadScopeText.Length > 0;

    public string ReadScopeSummary => Text.Resolve((IsCopilotSubscriptionSelected
        ? SessionFolderCandidate() : CurrentReadScope()?.Scope.CandidateDirectory) is null
        ? "agentReadScopeNoWorkspaceSummary" : "agentReadScopeDetails");

    // Keep blocked or pinned-session scope warnings expanded; routine scope details can be disclosed on demand.
    public bool IsReadScopeCritical => !IsCopilotSubscriptionSelected && (IsReadScopeBlocked ||
        (CurrentReadScope()?.Scope.Rejection is not null and not AgentCliReadScopeRejection.None and not AgentCliReadScopeRejection.NotProvided) ||
        (CurrentReadScope() is { } current && ActiveConversation.HasSessionWorkingDirectory &&
         ActiveConversation.SessionProviderId == SelectedProvider?.ProviderId &&
         !string.Equals(ActiveConversation.SessionWorkingDirectory, current.Scope.CandidateDirectory, StringComparison.Ordinal)));

    /// <summary>Neither the chosen folder nor the dedicated folder can be used: no session can start (no fallback).</summary>
    public bool IsReadScopeBlocked => CurrentReadScope()?.Scope.BlocksSending == true;

    /// <summary>Folder captured when the visible conversation's session started.</summary>
    public string? SessionWorkingDirectory => ActiveConversation.SessionWorkingDirectory;

    public bool HasSessionWorkingDirectory => ActiveConversation.HasSessionWorkingDirectory;

    private (AgentCliReadScope Scope, AgentCliProviderProfile Profile)? CurrentReadScope()
    {
        if (SelectedProvider is not { UsesOfficialCli: true } provider || _services.CliAccountPresentation is not { } presentation)
        {
            return null;
        }

        var candidate = SessionFolderCandidate();
        if (_readScopeCache is { } cached && cached.ProviderId == provider.ProviderId && cached.Candidate == candidate)
        {
            return (cached.Scope, cached.Profile);
        }

        try
        {
            if (presentation.Describe(provider.ProviderId) is not { } profile)
            {
                return null;
            }

            var scope = presentation.DescribeReadScope(provider.ProviderId, candidate);
            _readScopeCache = (provider.ProviderId, candidate, scope, profile);
            return (scope, profile);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Files panel folder a new session would receive (none when the permissions disable it).</summary>
    private string? SessionFolderCandidate()
    {
        string? folder;
        try
        {
            folder = _host.WorkspaceFolder;
        }
        catch (Exception)
        {
            folder = null;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        return CurrentPermissions is { IsWellFormed: true, Workspace.UseFilesFolder: false } ? null : folder;
    }

    /// <summary>Called when the Files panel folder changes: the notice always shows the folder a new session would use.</summary>
    public void RefreshReadScope()
    {
        _readScopeCache = null;
        OnPropertyChanged(nameof(ReadScopeText));
        OnPropertyChanged(nameof(HasReadScope));
        OnPropertyChanged(nameof(ReadScopeSummary));
        OnPropertyChanged(nameof(IsReadScopeCritical));
        OnPropertyChanged(nameof(IsReadScopeBlocked));
        RefreshSendBlock();
        if (IsIdle)
        {
            UpdateIdleState();
        }

        NotifyCommands();
    }

    /// <summary>
    /// Executor named on native-tool cards: the official CLI profile of the turn's provider when the host describes one,
    /// otherwise its display name. Looked up by provider ID through the port, never by brand.
    /// </summary>
    private string? ObservedToolExecutor(string providerId)
    {
        try
        {
            return _services.CliAccountPresentation?.Describe(providerId)?.CliName ??
                Providers.FirstOrDefault(p => p.ProviderId == providerId)?.Presentation.DisplayName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Providers, models and modes. ----

    /// <summary>Re-reads provider availability, e.g. after the settings window closed. Keeps the conversation.</summary>
    public void ReloadProviders() => LoadProviders(SelectedProvider?.ProviderId, SelectedModel);

    /// <summary>Re-reads the catalog only when its cached listing changed since the last load.</summary>
    public void ReloadProvidersIfChanged()
    {
        if (!_services.IsComplete)
        {
            return;
        }

        IReadOnlyList<AgentProviderPresentation> current;
        try
        {
            current = _services.Catalog!.List();
        }
        catch (Exception)
        {
            return;
        }

        if (!ReferenceEquals(current, _lastListing))
        {
            ReloadProviders();
        }
    }

    public AgentSettingsViewModel CreateSettingsViewModel() =>
        new(_services.Catalog, _services.Credentials, SelectedProvider?.ProviderId, _services.AccountManager,
            _services.CliAccountPresentation, _services.Availability, () => _host.WorkspaceFolder,
            _services.CopilotCliConfiguration, _host.SaveCopilotCliExecutablePathAsync);

    private void LoadProviders(string? keepProviderId, string? keepModel)
    {
        var previous = SelectedProvider;
        _suppressProviderSwitch = true;
        try
        {
            Providers.Clear();
            IReadOnlyList<AgentProviderPresentation> listed = [];
            if (_services.IsComplete)
            {
                try
                {
                    listed = _services.Catalog!.List();
                    _lastListing = listed;
                }
                catch (Exception)
                {
                    listed = [];
                }
            }

            foreach (var item in listed.Where(static item => !string.IsNullOrWhiteSpace(item.ProviderId)))
            {
                Providers.Add(new AgentProviderOption(item));
            }

            // Only an explicit previous choice is kept; otherwise the first *available* provider is preselected so the
            // destination is visible. Preselection never grants consent to send.
            var match = keepProviderId is null ? null : Providers.FirstOrDefault(p => p.ProviderId == keepProviderId);
            SelectedProvider = match ?? Providers.FirstOrDefault(static p => p.Presentation.IsAvailable) ?? Providers.FirstOrDefault();
            LoadModels(keepModel);
        }
        finally
        {
            _suppressProviderSwitch = false;
        }

        if (previous is not null && SelectedProvider is not null && previous.ProviderId != SelectedProvider.ProviderId)
        {
            OnProviderSwitched(previous);
        }

        RefreshSendBlock();
        RefreshAutomaticChips();
        UpdateIdleState();
        OnPropertyChanged(nameof(AvailabilityText));
            OnPropertyChanged(nameof(StatusSummary));
    }

    private void LoadModels(string? keep)
    {
        // A previous unresolved choice belongs to the conversation, even though the selector is empty.
        if (keep is null && _copilotModelChoiceRequired && IsCopilotSubscriptionSelected &&
            ActiveConversation.ProviderId == SelectedProvider?.ProviderId)
            keep = ActiveConversation.ModelId;
        _updatingModelCatalog = true;
        try
        {
            _copilotModelChoiceRequired = false;
            Models.Clear();
            foreach (var model in SelectedProvider?.Presentation.Models ?? [])
            {
                Models.Add(model);
            }

            // A restored conversation may target a saved CLI model while its first account/model discovery is still
            // running. Keep that selection visible until the active check completes; then this method runs again against
            // the published list. Copilot requires an explicit choice if the model is no longer eligible.
            var selectedProviderId = SelectedProvider?.ProviderId;
            var accountProvider = SelectedProvider?.Presentation.AuthenticationMethods.Any(static method =>
                method is AgentAuthenticationMethod.OfficialCliDelegated or AgentAuthenticationMethod.OfficialAppServerDelegated) == true;
            var availability = selectedProviderId is null ? null : _services.Availability?.Current(selectedProviderId);
            var accountCheckPending = accountProvider && SelectedProvider?.Presentation.IsAvailable != true &&
                (availability is null or { State: AgentProviderAvailabilityState.Checking or AgentProviderAvailabilityState.NotChecked });
            if (accountCheckPending && keep is { Length: > 0 } && !Models.Contains(keep))
            {
                Models.Insert(0, keep);
                SelectedModel = keep;
                OnPropertyChanged(nameof(HasModels));
                return;
            }

            if (IsCopilotSubscriptionSelected && keep is not null && !Models.Contains(keep))
            {
                _copilotModelChoiceRequired = true;
                SelectedModel = null;
                OnPropertyChanged(nameof(HasModels));
                return;
            }
            var fallback = CurrentPermissions?.DefaultModel;
            SelectedModel = keep is not null && Models.Contains(keep) ? keep
                : fallback is not null && Models.Contains(fallback) ? fallback
                : Models.FirstOrDefault();
            OnPropertyChanged(nameof(HasModels));
        }
        finally
        {
            _updatingModelCatalog = false;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusSummary));
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedProviderChanged(AgentProviderOption? oldValue, AgentProviderOption? newValue)
    {
        if (_suppressProviderSwitch)
        {
            return;
        }

        LoadModels(null);
        OnProviderSwitched(oldValue);
        RefreshSendBlock();
        RefreshAutomaticChips();
        UpdateIdleState();
    }

    /// <summary>
    /// A different provider is a different recipient: the conversation is not transferred. An empty idle conversation
    /// simply changes provider; otherwise a new conversation starts (the previous one stays in the history).
    /// </summary>
    private void OnProviderSwitched(AgentProviderOption? previous)
    {
        _readScopeCache = null;
        var provider = SelectedProvider;
        if (provider is null)
        {
            return;
        }

        _ = EnsurePermissionsAsync(provider.ProviderId);
        var conversation = ActiveConversation;
        if (conversation.ProviderId == provider.ProviderId)
        {
            return;
        }

        if (conversation.IsEmpty && !conversation.IsBusy)
        {
            conversation.ProviderId = provider.ProviderId;
            conversation.ModelId = SelectedModel;
            _ = CloseSessionAsync(conversation);
        }
        else
        {
            ActivateConversation(CreateConversation(provider.ProviderId, SelectedModel));
        }

        if (previous is not null)
        {
            StatusDetail = Text.Format("agentProviderSwitched", provider.Presentation.DisplayName);
        }

        StartAutomaticAvailabilityCheck();
        _host.OnPanelPreferencesChanged();
        OnPropertyChanged(nameof(UsageDetails));
    }

    partial void OnSelectedModelChanged(string? value)
    {
        ScheduleContextMeasurement();
        if (!_updatingModelCatalog && IsCopilotSubscriptionSelected && value is not null && Models.Contains(value))
            _copilotModelChoiceRequired = false;
        if ((!_updatingModelCatalog || !IsCopilotSubscriptionSelected) && !ActiveConversation.IsBusy)
        {
            ActiveConversation.ModelId = value;
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusSummary));
        SendCommand.NotifyCanExecuteChanged();
        if (!_updatingModelCatalog && !_suppressProviderSwitch)
        {
            _host.OnPanelPreferencesChanged();
        }
    }

    partial void OnSelectedModeChanged(AgentModeOption value)
    {
        ScheduleContextMeasurement();
        if (!ActiveConversation.IsBusy)
        {
            ActiveConversation.Mode = value.Mode;
        }

        RefreshSendBlock();
        _host.OnPanelPreferencesChanged();
    }

    partial void OnComposerTextChanged(string value)
    {
        ScheduleContextMeasurement();
        // Typing again clears the terminal state of the previous turn (the rows stay).
        if (ActiveConversation is { IsBusy: false, TurnState: { } terminal } conversation && terminal is not AgentChatState.Connecting)
        {
            conversation.TurnState = null;
            conversation.TurnDetail = null;
            UpdateIdleState();
        }

        NotifyCommands();
    }

    partial void OnStateChanged(AgentChatState value) => NotifyCommands();

    partial void OnActiveConversationChanged(AgentChatConversation? oldValue, AgentChatConversation newValue)
    {
        ScheduleContextMeasurement();
        _readScopeCache = null;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanChangeProvider));
        OnPropertyChanged(nameof(ReadScopeText));
        OnPropertyChanged(nameof(HasReadScope));
        OnPropertyChanged(nameof(ReadScopeSummary));
        OnPropertyChanged(nameof(IsReadScopeCritical));
        OnPropertyChanged(nameof(UsageDetails));
        UpdateIdleState();
        NotifyCommands();
        RefreshHistoryActiveFlags();
    }

    private void NotifyCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        CancelTurnCommand.NotifyCanExecuteChanged();
        ConfigureCommand.NotifyCanExecuteChanged();
        RefreshProvidersCommand.NotifyCanExecuteChanged();
        RetryAvailabilityCommand.NotifyCanExecuteChanged();
        NewConversationCommand.NotifyCanExecuteChanged();
        AttachWorkspaceFileCommand.NotifyCanExecuteChanged();
        AttachExternalFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowRefreshProviders));
        OnPropertyChanged(nameof(ShowEmptyInvitation));
    }

    /// <summary>Shows the visible conversation's turn state, or the provider readiness when it is idle.</summary>
    private void UpdateIdleState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanChangeProvider));
        // Provider availability/auth refresh may finish while a turn is already connecting or streaming. Preserve that
        // turn's visible state; the turn completion calls this method again after clearing its run handle.
        if (ActiveConversation.Turn is not null)
        {
            return;
        }

        if (ActiveConversation.TurnState is { } turnState)
        {
            State = turnState;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsStatusError));
            return;
        }

        // A credential problem is reported before generic unavailability because it has a concrete action.
        State = !IsFeatureAvailable ? AgentChatState.Unavailable
            : SelectedProvider is not { } provider ? AgentChatState.NoProvider
            : provider.Presentation.AuthState switch
            {
                AgentProviderAuthState.NotConfigured => AgentChatState.NotAuthenticated,
                AgentProviderAuthState.Invalid or AgentProviderAuthState.Expired => AgentChatState.CredentialExpired,
                AgentProviderAuthState.VaultUnavailable => AgentChatState.VaultUnavailable,
                _ when !provider.Presentation.IsAvailable => AgentChatState.ProviderUnavailable,
                _ when IsReadScopeBlocked => AgentChatState.ReadScopeUnavailable,
                _ => AgentChatState.Ready,
            };
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(ShowRefreshProviders));
    }

    private bool IsProviderUsable =>
        IsFeatureAvailable && SelectedProvider is { Presentation: { IsAvailable: true } presentation } &&
        presentation.AuthState is AgentProviderAuthState.NotRequired or AgentProviderAuthState.Configured && !IsReadScopeBlocked;

    private bool HasEligibleCopilotModel => !IsCopilotSubscriptionSelected ||
        SelectedModel is { Length: > 0 } model && Models.Contains(model);

    private bool IsCopilotModelSelectionRequired => IsCopilotSubscriptionSelected && IsProviderUsable &&
        (_copilotModelChoiceRequired || !HasEligibleCopilotModel);

    /// <summary>"Check availability" is offered only while no usable provider is selected and nothing runs.</summary>
    public bool ShowRefreshProviders => IsFeatureAvailable && IsIdle && !IsProviderUsable && State != AgentChatState.ReadScopeUnavailable &&
        !ShowAvailabilityRetry;

    private bool CanRefreshProviders() => IsFeatureAvailable && IsIdle && !IsRefreshingProviders;

    /// <summary>
    /// Explicit user action: re-checks local configuration and vault presence of every provider (never network or
    /// authentication).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshProviders))]
    private async Task RefreshProvidersAsync()
    {
        IsRefreshingProviders = true;
        StatusDetail = null;
        var failed = false;
        try
        {
            await _services.Catalog!.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            failed = true;
        }
        finally
        {
            IsRefreshingProviders = false;
        }

        ReloadProviders();
        if (failed)
        {
            StatusDetail = Text.Resolve("agentRefreshFailed");
        }
    }

    partial void OnIsRefreshingProvidersChanged(bool value) => RefreshProvidersCommand.NotifyCanExecuteChanged();

    private bool CanConfigure() => IsFeatureAvailable;

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private void Configure() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationViewModel.Language))
        {
            return;
        }

        void RefreshLocalizedState()
        {
            if (_disposed) return;
            foreach (var mode in Modes) mode.RefreshLocalizedText();
            foreach (var conversation in _conversations.Values)
                foreach (var tool in conversation.Items.OfType<AgentToolCallItem>()) tool.RefreshLocalizedText();
            OnPropertyChanged(nameof(SelectedModeHint));
            NotifyMetricProperties();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(DestinationText));
            OnPropertyChanged(nameof(DestinationHint));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(ProviderSummary));
            OnPropertyChanged(nameof(PermissionsSummary));
            OnPropertyChanged(nameof(ContextSummary));
            OnPropertyChanged(nameof(AvailabilityText));
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(SendBlockText));
            OnPropertyChanged(nameof(ConversationTitle));
            RefreshReadScope();
        }

        if (Dispatcher.UIThread.CheckAccess()) RefreshLocalizedState();
        else Dispatcher.UIThread.Post(RefreshLocalizedState);
    }

    private static string SafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit) ? code : "AgentFailure";

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalizationViewModel.Current.PropertyChanged -= OnLocalizationChanged;
        foreach (var attachment in _attachments)
        {
            attachment.Dispose();
        }

        _attachments.Clear();
        await _lifetime.CancelAsync();
        foreach (var conversation in _conversations.Values.ToArray())
        {
            CloseConfirmations(conversation);
            await CloseSessionAsync(conversation);
        }

        _lifetime.Dispose();
    }
}
