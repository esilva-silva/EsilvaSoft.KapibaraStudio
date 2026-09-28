using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class ExplorerNodeViewModel : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private readonly WorkspaceService _workspace;
    private readonly IMetadataCache? _metadata;
    private bool _loaded;
    private string _filter = "";
    private int _generation;
    private CancellationTokenSource? _cancellation;
    private Task? _loadTask;
    public ConnectionProfile Profile { get; }
    public string Database { get; }
    public string? Collection { get; }
    public string Name { get; }
    public ExplorerNodeKind Kind { get; }
    public ExplorerNodeViewModel? Parent { get; }
    public ExplorerNodeViewModel Root => Parent?.Root ?? this;
    public IndexInfo? Index { get; private set; }
    public bool IsDatabase => Kind == ExplorerNodeKind.Database;
    public bool IsCollection => Kind is ExplorerNodeKind.Collection or ExplorerNodeKind.Documents;
    public bool IsConnection => Kind == ExplorerNodeKind.Connection;
    public bool IsIndex => Kind == ExplorerNodeKind.Index;
    public bool HasCollection => Collection is not null;
    public bool CanExpand => Kind is ExplorerNodeKind.Connection or ExplorerNodeKind.Database or ExplorerNodeKind.Collection or ExplorerNodeKind.Indexes;
    public string Context => Profile.Name + " › " + Database + (Collection is null ? "" : " › " + Collection) + " · " + Profile.RoutingLabel;
    public ObservableCollection<ExplorerNodeViewModel> Children { get; } = [];
    public event EventHandler? ConnectionChanged;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _message = "";
    public string Label => Name + (IsLoading ? " …" : Message.Length > 0 ? " ⚠" : IsConnection ? IsConnected ? " · " + T("connected") : " · " + T("disconnected") : "");

    public ExplorerNodeViewModel(WorkspaceService workspace, ConnectionProfile profile, string name, string database = "", string? collection = null, bool isDatabase = false,
        ExplorerNodeKind? kind = null, ExplorerNodeViewModel? parent = null, IndexInfo? index = null, IMetadataCache? metadata = null)
    {
        _workspace = workspace; _metadata = metadata; Profile = profile; Name = name; Database = database; Collection = collection; Parent = parent; Index = index;
        Kind = kind ?? (isDatabase ? ExplorerNodeKind.Database : collection is not null ? ExplorerNodeKind.Collection : ExplorerNodeKind.Connection);
        if (CanExpand) Children.Add(new ExplorerNodeViewModel(workspace, profile, T("expandToLoad"), database, collection, kind: ExplorerNodeKind.Placeholder, parent: this, metadata: metadata));
    }

    partial void OnIsExpandedChanged(bool value) { if (value && CanExpand && !_loaded) _ = LoadAsync(); }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(Label));
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnIsConnectedChanged(bool value) { OnPropertyChanged(nameof(Label)); ConnectionChanged?.Invoke(this, EventArgs.Empty); }

    public Task LoadAsync()
    {
        if (_loadTask is { IsCompleted: false }) return _loadTask;
        return _loadTask = LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        if (!CanExpand) return;
        if (!IsConnection && !Root.IsConnected && Parent is not null) { Message = T("connectBeforeExpand"); return; }
        var generation = _generation;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        IsLoading = true; Message = "";
        try
        {
            var entries = new List<(string Name, ExplorerNodeKind Kind, string Database, string? Collection, IndexInfo? Index)>();
            if (IsConnection)
                foreach (var name in await _workspace.GetDatabasesAsync(Profile, cancellation.Token)) entries.Add((name, ExplorerNodeKind.Database, name, null, null));
            else if (IsDatabase)
                foreach (var name in await _workspace.GetCollectionsAsync(Profile, Database, cancellation.Token)) entries.Add((name, ExplorerNodeKind.Collection, Database, name, null));
            else if (Kind == ExplorerNodeKind.Collection)
            {
                entries.Add((LocalizationViewModel.Current.Resolve("documentsNode"), ExplorerNodeKind.Documents, Database, Collection, null));
                entries.Add((LocalizationViewModel.Current.Resolve("indexesNode"), ExplorerNodeKind.Indexes, Database, Collection, null));
            }
            else if (Kind == ExplorerNodeKind.Indexes)
                foreach (var index in await _workspace.GetExplorerIndexesAsync(Profile, Database, Collection!, cancellation.Token)) entries.Add((index.Name, ExplorerNodeKind.Index, Database, Collection, index));
            if (generation != _generation) return;
            // Write-through: what the explorer just loaded is fresh autocomplete metadata, never loaded twice.
            if (IsConnection) _metadata?.PutDatabases(Profile, entries.Select(entry => entry.Name).ToArray());
            else if (IsDatabase) _metadata?.PutCollections(Profile, Database, entries.Select(entry => entry.Name).ToArray());
            else if (Kind == ExplorerNodeKind.Indexes) _metadata?.PutIndexes(Profile, Database, Collection!, entries.Select(entry => entry.Index!).ToArray());
            var previous = Children.ToArray();
            Children.Clear();
            foreach (var entry in entries)
            {
                var child = previous.FirstOrDefault(c => c.Kind == entry.Kind && c.Name == entry.Name)
                    ?? new ExplorerNodeViewModel(_workspace, Profile, entry.Name, entry.Database, entry.Collection, kind: entry.Kind, parent: this, index: entry.Index, metadata: _metadata);
                child.Index = entry.Index;
                Children.Add(child);
            }
            foreach (var removed in previous.Except(Children)) removed.Invalidate();
            _loaded = true;
            if (IsConnection) IsConnected = true;
            Message = entries.Count == 0 ? LocalizationViewModel.Current.Resolve("noVisibleItems") : "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (generation == _generation) { Message = DesktopOperationErrorMessages.Describe(ex); _loaded = false; } }
        finally { if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; if (generation == _generation) { IsLoading = false; Filter(_filter); } }
    }

    public void Invalidate()
    {
        _generation++; _cancellation?.Cancel(); _loadTask = null; IsLoading = false; _loaded = false;
        foreach (var child in Children) child.Invalidate();
        Children.Clear(); IsConnected = false; IsExpanded = false;
        if (CanExpand) Children.Add(new ExplorerNodeViewModel(_workspace, Profile, T("expandToLoad"), kind: ExplorerNodeKind.Placeholder, parent: this, metadata: _metadata));
    }

    public bool Filter(string search)
    {
        _filter = search;
        var matches = Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        var childMatch = false;
        foreach (var child in Children) childMatch |= child.Filter(matches ? "" : search);
        IsVisible = matches || childMatch;
        return IsVisible;
    }
}
