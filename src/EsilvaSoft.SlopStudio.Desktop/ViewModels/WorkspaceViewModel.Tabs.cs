using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    // Merged pipeline input schema per scope, memoized so PipelineInputSchema always returns a stable reference
    // while the sampled schema and the declared validator underneath it stay the same (CompletionContextCache
    // compares CollectionSchema by reference; a fresh instance on every call would defeat that cache).
    private readonly Dictionary<(ConnectionIdentity Connection, string Database, string Collection), (CollectionSchema? Sampled, CollectionSchema? Validator, CollectionSchema? Merged)> _pipelineInputSchemas = [];
    private readonly object _pipelineInputSchemaGate = new();

    /// <summary>Reads the pipeline input shape without I/O; used only while typing (<see cref="MetadataAccess.Peek"/>).</summary>
    private CollectionSchema? ResolvePipelineInputSchema(CatalogScope scope)
    {
        if (scope.Database.Length == 0 || scope.Collection.Length == 0) return null;
        var sampled = Metadata.GetSampledSchema(scope.Connection, scope.Database, scope.Collection, MetadataAccess.Peek).Value;
        var validator = Metadata.GetDefinition(scope.Connection, scope.Database, scope.Collection, MetadataAccess.Peek).Value?.Validator;
        if (sampled is null) return validator;
        if (validator is null) return sampled;
        var key = (scope.Connection, scope.Database, scope.Collection);
        lock (_pipelineInputSchemaGate)
        {
            if (_pipelineInputSchemas.TryGetValue(key, out var cached) && ReferenceEquals(cached.Sampled, sampled) && ReferenceEquals(cached.Validator, validator))
                return cached.Merged;
            var merged = CollectionSchema.Merge([sampled, validator]);
            _pipelineInputSchemas[key] = (sampled, validator, merged);
            return merged;
        }
    }

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];
    [ObservableProperty] private WorkspaceTabViewModel? _activeTab;
    public bool HasActiveTab => ActiveTab is not null;
    public bool CanSetProfileRecovery => ActiveTab?.Profile is not null;

    public WorkspaceTabViewModel OpenScript(ExplorerNodeViewModel node, ExplorerScriptOperation operation)
    {
        var database = string.IsNullOrWhiteSpace(node.Database) ? node.Profile.DefaultDatabase ?? "admin" : node.Database;
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node.Profile.Id, Database = database, Collection = node.Collection ?? "", Mode = "Console", HistoryEnabled = true,
            Text = ExplorerScripts.Create(operation, database, node.Collection, node.Index, CaptureUuidPolicy().ResolveOptions(node.Profile.Id)), IsDirty = true }, node.Profile);
        tab.IsConnected = IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
        return tab;
    }

    public void OpenConsoleHistory(ConsoleHistoryEntry entry)
    {
        var root = Roots.FirstOrDefault(r => r.Profile.Id == entry.ProfileId);
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = entry.ProfileId, TargetHost = entry.TargetHost, Database = entry.Database, Collection = entry.Collection, Mode = entry.Mode, Limit = entry.DocumentLimit, Text = entry.Script, HistoryEnabled = true }, root?.Profile is { } profile ? profile with { TargetHost = entry.TargetHost } : null);
        tab.IsConnected = tab.Profile is not null && IsProfileConnected(tab.Profile);
        tab.Messages = F("historyEnvironment", entry.Environment);
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void OpenDocumentInEditor(WorkspaceTabViewModel source, string json)
    {
        var result = source.IsConsole ? source.SelectedConsoleResult : null;
        var profile = result?.SourceProfile ?? source.Profile;
        if (profile is null) return;
        var database = result?.Database ?? source.Database;
        var collection = result?.Collection ?? source.Collection;
        var node = new ExplorerNodeViewModel(_workspace, profile, collection, database, collection);
        var tab = OpenScript(node, ExplorerScriptOperation.Shell);
        tab.ContainsResultData = true;
        tab.Messages = T("editorDocumentRecoveryOff");
        tab.Text = "// " + T("documentEditorIntro") + "\nconst document = EJSON.parse(" + System.Text.Json.JsonSerializer.Serialize(json) + ");\ndocument;\n";
    }

    public void BindActiveTab(ConnectionProfile profile, string database, string collection)
    {
        if (ActiveTab is null || ActiveTab.IsRunning) return;
        ActiveTab.Profile = profile; ActiveTab.Database = database; ActiveTab.Collection = collection;
        ActiveTab.IsConnected = IsProfileConnected(profile);
        UpdateProfileRecovery();
    }

    [RelayCommand]
    private void NewTab()
    {
        var node = SelectedNode;
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node?.Profile.Id, Database = node?.Database ?? "", Text = "// " + T("consoleDraftText") + "\n", Mode = "Console", HistoryEnabled = true }, node?.Profile);
        tab.IsConnected = node is not null && IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void OpenCollection(ExplorerNodeViewModel node)
    {
        if (!node.IsCollection) return;
        var existing = Tabs.FirstOrDefault(t => t.Profile?.Id == node.Profile.Id && t.Profile.TargetHost == node.Profile.TargetHost && t.Database == node.Database && t.Collection == node.Collection && t.IsConsole);
        if (existing is not null) { ActiveTab = existing; return; }
        var tab = CreateTab();
        tab.Restore(new WorkspaceDraft { ProfileId = node.Profile.Id, Database = node.Database, Collection = node.Collection!, Mode = "Console", HistoryEnabled = true, Text = ConsoleScripts.Find(node.Collection!) }, node.Profile);
        tab.IsConnected = IsProfileConnected(node.Profile);
        Register(tab); ActiveTab = tab; ScheduleSave();
    }

    public void RemoveTab(WorkspaceTabViewModel tab)
    {
        if (tab.IsRunning) throw new InvalidOperationException(T("waitOperationClose"));
        tab.DraftChanged -= OnDraftChanged;
        Tabs.Remove(tab);
        if (ActiveTab == tab) ActiveTab = Tabs.LastOrDefault();
        ReleaseAgentChat(tab);
        tab.Dispose();
        ScheduleSave();
    }

    private void Register(WorkspaceTabViewModel tab)
    {
        tab.KnownSyntaxNamespaces = KnownSyntaxNamespaces;
        tab.Autocomplete = AutocompleteService;
        tab.TraditionalCompletion = TraditionalCompletion;
        tab.InlinePreemptiveCompletion = InlinePreemptiveCompletion;
        tab.AiCompletion = AiCompletion;
        tab.PipelineInputSchema = ResolvePipelineInputSchema;
        tab.CompletionUsage = CompletionUsage;
        tab.Commands = Commands;
        tab.Shortcuts = KeyBindings;
        tab.KnownAutocompleteNames = () => KnownAutocompleteNames(tab);
        tab.UuidPolicy = CaptureUuidPolicy(); Tabs.Add(tab); tab.DraftChanged += OnDraftChanged;
    }

    private WorkspaceTabViewModel CreateTab() => new(_workspace) { CodeFontSize = CodeFontSize };

    private void OnDraftChanged(object? sender, EventArgs e) => ScheduleSave();

    partial void OnActiveTabChanged(WorkspaceTabViewModel? value)
    {
        OnPropertyChanged(nameof(HasActiveTab)); OnPropertyChanged(nameof(CanSetProfileRecovery));
        UpdateProfileRecovery(); ScheduleSave();
        OnActiveTabChangedForAgent(value);
    }
    private void UpdateProfileRecovery()
    {
        var enabled = ActiveTab?.Profile is not { } profile || !_excludedProfiles.Contains(profile.Id);
        _updatingRecovery = true;
        RecoverActiveProfile = enabled;
        _updatingRecovery = false;
        OnPropertyChanged(nameof(CanSetProfileRecovery));
    }
    partial void OnRecoverActiveProfileChanged(bool value)
    {
        if (_updatingRecovery || ActiveTab?.Profile is not { } profile) return;
        if (value) _excludedProfiles.Remove(profile.Id); else _excludedProfiles.Add(profile.Id);
        ScheduleSave();
    }
}
