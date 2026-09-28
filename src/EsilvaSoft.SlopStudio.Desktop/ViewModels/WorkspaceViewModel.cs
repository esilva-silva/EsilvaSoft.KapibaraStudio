using EsilvaSoft.SlopStudio.LocalAi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.SchemaLearning;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    partial void DisposeWorkspaceFiles();
    private readonly WorkspaceService _workspace;
    private readonly IWorkspaceSessionRepository _sessions;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _debounce;
    private bool _initialized;
    private bool _disposed;
    private bool _updatingRecovery;
    private AutocompleteSettings _autocompleteSettings = new();
    // Persisted form kept as loaded (null = absent) so autosave never drops or materializes custom shortcuts.
    private EditorKeyBindings? _keyBindings;
    private readonly HashSet<Guid> _excludedProfiles = [];
    /// <summary>
    /// L15 opt-out (per-connection): null when no host wires one, in which case applying preferences and
    /// <see cref="SetLearnedSchemaExcludedAsync"/> are no-ops and the general flag alone still governs serving,
    /// exactly the null-conditional convention already used for the other optional collaborators below.
    /// </summary>
    private readonly ILearnedSchemaOptOut? _learnedSchemaOptOut;
    private readonly Dictionary<Guid, UuidRepresentation> _profileUuidRepresentations = [];
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly bool _ownsMetadata;
    private readonly LocalizationViewModel _localization = LocalizationViewModel.Current;
    public WorkspaceService Workspace => _workspace;
    internal IWorkspaceFileService? WorkspaceFileService { get; }
    /// <summary>Autocomplete metadata; explorer loads write into it and connected roots allow refreshes.</summary>
    public IMetadataCache Metadata { get; }
    public ApplicationStatusViewModel Operations { get; }
    public AppUpdateViewModel Updates { get; }
    public UuidPreferenceViewModel UuidPreferences { get; }
    public IdentifierPreferenceViewModel IdentifierPreferences { get; }
    public IAutocompleteService AutocompleteService { get; }
    /// <summary>Deterministic catalog completion shared by all editor tabs.</summary>
    public ICompletionProvider? TraditionalCompletion { get; }
    /// <summary>Gerador determinístico da sugestão automática; independe de modelo de IA e de conexão aberta.</summary>
    public ICompletionProvider? InlinePreemptiveCompletion { get; }
    /// <summary>
    /// Provider da IA explícita (<c>Ctrl+;</c>), compartilhado por todas as abas — uma instância, um pipeline, um
    /// serviço de modelo. Nulo quando a composição não o registrou: o atalho então só abre a lista com o motivo.
    /// </summary>
    public IAiCompletionProvider? AiCompletion { get; }
    /// <summary>
    /// Sinal de uso da sessão, compartilhado por todas as abas: quem aprende é o usuário, não a aba, e uma sugestão
    /// aceita em uma aba deve subir na seguinte. É só memória — nomes, sem valores — e nunca chega ao LiteDB nem à
    /// sessão persistida. Compartilhar isto não é compartilhar cancelamento: o CancellationTokenSource segue por aba.
    /// </summary>
    public CompletionUsageTracker CompletionUsage { get; } = new();
    public AutocompleteSettingsViewModel AutocompletePreferences { get; }
    /// <summary>Effective editor shortcuts per command id; defaults until a readable session is loaded.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> KeyBindings { get; private set; } = EditorKeyBindings.Resolve(null);
    /// <summary>
    /// Single dispatcher instance built from <see cref="KeyBindings"/>, shared by every tab. Immutable and stateless,
    /// so the desktop reuses this one instance across every key event instead of constructing a dispatcher per keystroke.
    /// </summary>
    public EditorCommandDispatcher Commands { get; private set; } = new(EditorKeyBindings.Resolve(null));
    public ExplorerDetailsViewModel Details { get; }
    public LocalizationViewModel Localization => _localization;
    public IReadOnlyList<string> Themes { get; } = ["Sistema", "Claro", "Escuro"];
    public IReadOnlyList<ApplicationLanguage> Languages { get; } = ApplicationLanguages.All;
    [ObservableProperty] private string _theme = "Sistema";
    [ObservableProperty] private string _language = ApplicationLanguages.DefaultCode;
    [ObservableProperty] private double _codeFontSize = 14;
    [ObservableProperty] private double _explorerWidth = 260;
    [ObservableProperty] private double _editorRatio = .6;
    [ObservableProperty] private bool _recoverDrafts = true;
    [ObservableProperty] private bool _recoverActiveProfile = true;
    [ObservableProperty] private string _sessionStatus = "";
    public event EventHandler? ThemeChanged;
    public event EventHandler? LanguageChanged;
    public event EventHandler? LayoutChanged;

    public WorkspaceViewModel(WorkspaceService workspace, IWorkspaceSessionRepository sessions, IAutocompleteService? autocomplete = null, ILocalModelCatalog? modelCatalog = null,
        IKnowledgeCatalog? knowledgeCatalog = null,
        ILocalAiModelService? localModels = null, IAppUpdateService? updates = null, IRemoteModelSource? remoteModels = null, IMetadataCache? metadata = null,
        ILearnedSchemaOptOut? learnedSchemaOptOut = null, IAiCompletionProvider? aiCompletion = null, IWorkspaceFileService? workspaceFiles = null,
        Agents.AgentChatServicesFactory? agentChat = null, IConnectionProfileCredentialStatusProvider? credentialStatus = null)
    {
        _workspace = workspace;
        // P7-L06-HOST: both optional. The chat factory is only invoked when the agent panel is opened; the credential
        // status is read once in the background after startup, through the operation coordinator.
        _agentChatServices = agentChat;
        _credentialStatus = credentialStatus;
        WorkspaceFileService = workspaceFiles;
        _workspace.OperationLocalizer = LocalizationViewModel.Current.ResolveOperationText;
        _workspace.SetLocalization(LocalizationViewModel.Current.Resolve);
        _localization.Language = _language;
        // Provider da IA explícita (Ctrl+;), opcional: sem ele — e é o caso enquanto o registro do pipeline ONNX não
        // existir na composição — o atalho continua reconhecido e cai no fallback da lista tradicional com o motivo,
        // nunca em silêncio e nunca inserindo texto.
        AiCompletion = aiCompletion;
        _sessions = sessions; Operations = new(workspace.Operations); Details = new ExplorerDetailsViewModel(workspace);
        _learnedSchemaOptOut = learnedSchemaOptOut;
        // Without a registered driver source, explorer write-through still feeds highlighting and names; remote loads stay unavailable.
        _ownsMetadata = metadata is null;
        Metadata = metadata ?? new MetadataCache(UnavailableMetadataSource.Instance);
        Metadata.SetLocalization(LocalizationViewModel.Current.Resolve);
        Metadata.Changed += OnMetadataChanged;
        Updates = new(updates, workspace.Operations);
        AutocompleteService = autocomplete ?? new AutocompleteService();
        var effectiveCatalog = knowledgeCatalog ?? new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(Metadata)]);
        // Um único CompletionService serve a lista explícita e a sugestão automática: mesmo catálogo, mesmo
        // ranqueamento, mesmo sinal de uso. O que difere entre os dois provedores é apenas a política de disparo,
        // de confiança e de acesso a metadados.
        var completionService = new CompletionService(effectiveCatalog, new CompletionRanker(usage: CompletionUsage),
            new WorkspaceCompletionProfileResolver(() => Profiles));
        TraditionalCompletion = new TraditionalCompletionProvider(completionService);
        InlinePreemptiveCompletion = new TraditionalPreemptiveCompletionProvider(completionService);
        AutocompletePreferences = new(AutocompleteService, modelCatalog, async settings =>
        {
            var previous = _autocompleteSettings;
            _autocompleteSettings = settings;
            try { await SaveSessionAsync(); }
            catch { _autocompleteSettings = previous; throw; }
            await AutocompleteService.ConfigureAsync(settings);
        }, localModels, remoteModels, workspace.Operations);
        Roots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoConnections));
        UuidPreferences = new(LocalizationViewModel.Current.Resolve("uuidBsonTitle"), allowInherit: false, () => UuidRepresentation, value => SetUuidRepresentationAsync(value ?? UuidRepresentation.Standard));
        IdentifierPreferences = new(() => UuidRepresentation, SetIdentifierModeAsync);
    }

    public async Task InitializeAsync()
    {
        try
        {
            WorkspaceSession session;
            try
            {
                session = await _sessions.LoadSessionAsync();
            }
            catch
            {
                // Keep the explorer usable even when the session snapshot is unreadable, but do not start
                // profile-loading operations under an unknown language before the valid preference is restored.
                await ReloadProfilesAsync();
                throw;
            }
            Language = ApplicationLanguages.Normalize(session.Preferences.Language);
            await ReloadProfilesAsync();
            // Not awaited: the count is reported in the status bar whenever it arrives; startup never waits for it.
            CredentialRecoveryCheck = ReportCredentialRecoveryAsync();
            _autocompleteSettings = session.Preferences.Autocomplete.Validate();
            // Invalid shortcuts fail here too, before _initialized, so no save path can replace the snapshot.
            KeyBindings = EditorKeyBindings.Resolve(session.Preferences.EditorKeyBindings);
            Commands = new EditorCommandDispatcher(KeyBindings);
            _keyBindings = session.Preferences.EditorKeyBindings;
            // Startup only reads preferences; the model is validated and loaded on the first AI request.
            await AutocompleteService.ConfigureAsync(_autocompleteSettings);
            AutocompletePreferences.Load(_autocompleteSettings);
            Theme = Themes.Contains(session.Preferences.Theme) ? session.Preferences.Theme : "Sistema";
            CodeFontSize = Math.Clamp(session.Preferences.CodeFontSize, 12, 20);
            ExplorerWidth = Math.Clamp(session.Preferences.ExplorerWidth, 200, 420);
            EditorRatio = Math.Clamp(session.Preferences.EditorRatio, .25, .75);
            RecoverDrafts = session.Preferences.RecoverDrafts;
            _excludedProfiles.UnionWith(session.Preferences.ExcludedProfileIds);
            foreach (var profileId in session.Preferences.SchemaSamplingProfileIds) Metadata.SetSchemaSamplingAllowed(profileId, true);
            _learnedSchemaOptOut?.ApplyPreferences(session.Preferences);
            foreach (var entry in session.Preferences.ProfileUuidRepresentations) _profileUuidRepresentations[entry.Key] = entry.Value;
            UuidRepresentation = session.Preferences.UuidRepresentation;
            IdentifierMode = session.Preferences.IdentifierMode;
            WorkspaceRootPath = session.WorkspaceRootPath;
            SelectedSidebar = session.ActiveSidebar is "Files" ? "Files" : "Connections";
            if (!string.IsNullOrWhiteSpace(WorkspaceRootPath) && WorkspaceFileService is not null)
            {
                try { await SetWorkspaceFolderAsync(WorkspaceRootPath); }
                catch (Exception ex) { WorkspaceStatus = ex.Message; WorkspaceRootPath = session.WorkspaceRootPath; }
            }
            UuidPreferences.Load(UuidRepresentation);
            IdentifierPreferences.Load(IdentifierMode);
            if (RecoverDrafts)
                foreach (var draft in session.Tabs.Where(d => !d.ContainsResultData && (d.ProfileId is null || !_excludedProfiles.Contains(d.ProfileId.Value))))
                {
                    var tab = CreateTab();
                    tab.Restore(draft, Profiles.FirstOrDefault(p => p.Id == draft.ProfileId));
                    Register(tab);
                }
            ActiveTab = Tabs.FirstOrDefault(t => t.Id == session.ActiveTabId) ?? Tabs.FirstOrDefault();
            InitializeAgentPanel(session.Preferences);
            _initialized = true;
            if (Tabs.Count == 0) NewTab();
            SessionStatus = session.Tabs.Length > 0 ? LocalizationViewModel.Current.Resolve("draftsRecovered") : string.Empty;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Keep the unreadable snapshot intact; never silently overwrite it with an empty session.
            SessionStatus = LocalizationViewModel.Current.Format("sessionRestoreFailed", ex.Message);
            if (Tabs.Count == 0) NewTab();
        }
    }

    partial void OnThemeChanged(string value) { ThemeChanged?.Invoke(this, EventArgs.Empty); ScheduleSave(); }
    partial void OnLanguageChanged(string value)
    {
        var normalized = ApplicationLanguages.Normalize(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            Language = normalized;
            return;
        }
        Localization.Language = normalized;
        Details.RefreshLanguage();
        foreach (var tab in Tabs) tab.RefreshLanguage();
        foreach (var tab in Tabs)
        {
            foreach (var item in tab.LocalizedConsoleHistory) item.RefreshLanguage();
            foreach (var item in tab.LocalizedConsoleResults) item.RefreshLanguage();
        }
        AutocompletePreferences.RefreshLanguage();
        if (SelectedNode is null) ExplorerStatus = Roots.Count == 0 ? T("explorerOpenConnection") : T("expandDatabaseCollections");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }
    partial void OnCodeFontSizeChanged(double value) { foreach (var tab in Tabs) tab.CodeFontSize = value; ScheduleSave(); }
    partial void OnExplorerWidthChanged(double value) => ScheduleSave();
    partial void OnEditorRatioChanged(double value) => ScheduleSave();
    partial void OnRecoverDraftsChanged(bool value) => ScheduleSave();

    private void ScheduleSave()
    {
        if (!_initialized || _disposed) return;
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_debounce.Token);
    }
    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(750, cancellationToken); await SaveSessionAsync(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SessionStatus = LocalizationViewModel.Current.Format("draftNotSaved", ex.Message); }
    }

    public async Task SaveSessionAsync()
    {
        if (!_initialized) throw new InvalidOperationException(LocalizationViewModel.Current.Resolve("sessionNotLoaded"));
        await _saveGate.WaitAsync();
        try
        {
            var session = new WorkspaceSession
            {
                ActiveTabId = ActiveTab?.Id,
                WorkspaceRootPath = WorkspaceRootPath,
                ActiveSidebar = SelectedSidebar,
                Preferences = new WorkspacePreferences { Autocomplete = _autocompleteSettings, Theme = Theme, Language = ApplicationLanguages.Normalize(Language), CodeFontSize = CodeFontSize, ExplorerWidth = ExplorerWidth, EditorRatio = EditorRatio, RecoverDrafts = RecoverDrafts, ExcludedProfileIds = _excludedProfiles.ToArray(),
                    UuidRepresentation = UuidRepresentation, ProfileUuidRepresentations = new(_profileUuidRepresentations), IdentifierMode = IdentifierMode,
                    SchemaSamplingProfileIds = Metadata.SchemaSamplingProfiles.ToArray(),
                    LearnedSchemaExcludedProfileIds = _learnedSchemaOptOut?.ExcludedProfiles.ToArray() ?? [], EditorKeyBindings = _keyBindings,
                    AgentPanel = _agentPanelPreferences },
                Tabs = Tabs.Select(t => t.Snapshot()).ToArray()
            };
            await _sessions.SaveSessionAsync(session);
            SessionStatus = RecoverDrafts ? LocalizationViewModel.Current.Resolve("draftsUpdated") : LocalizationViewModel.Current.Resolve("draftRecoveryDisabled");
        }
        catch (Exception ex) { SessionStatus = LocalizationViewModel.Current.Format("draftNotSaved", ex.Message); throw; }
        finally { _saveGate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCredentialRecoveryCheck();
        Operations.Dispose();
        Updates.Dispose();
        _debounce?.Cancel(); _debounce?.Dispose();
        foreach (var root in Roots) root.Invalidate();
        Details.Clear();
        foreach (var tab in Tabs) { tab.DraftChanged -= OnDraftChanged; tab.CancelCommand.Execute(null); ReleaseAgentChat(tab); tab.Dispose(); }
        _ = DisposeAgentChatAsync();
        Metadata.Changed -= OnMetadataChanged;
        if (_ownsMetadata && Metadata is IDisposable metadata) metadata.Dispose();
        _saveGate.Dispose();
        DisposeWorkspaceFiles();
    }
}
