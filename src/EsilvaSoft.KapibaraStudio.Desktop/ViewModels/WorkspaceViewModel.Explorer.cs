using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = [];
    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    [ObservableProperty] private ExplorerNodeViewModel? _selectedNode;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _explorerStatus = T("explorerOpenConnection");
    public bool HasNoConnections => Roots.Count == 0;
    public bool SnapshotAllowsRecovery(Guid profileId) => !_excludedProfiles.Contains(profileId);

    private int _environmentRevision;
    public void InvalidateEnvironment()
    {
        _environmentRevision++;
        foreach (var root in Roots) root.Invalidate(); SelectedNode = null;
        foreach (var tab in Tabs) tab.IsConnected = false;
        ExplorerStatus = T("environmentUpdatedReopen");
    }

    public async Task ReloadProfilesAsync()
    {
        var profiles = await _workspace.GetProfilesAsync();
        Profiles.Clear(); foreach (var profile in profiles) Profiles.Add(profile);
        foreach (var root in Roots.ToArray())
        {
            if (profiles.FirstOrDefault(p => p.Id == root.Profile.Id) != (root.Profile with { TargetHost = null }))
            {
                root.Invalidate(); Roots.Remove(root);
                if (SelectedNode?.Profile.Id == root.Profile.Id) SelectedNode = null;
                ExplorerStatus = T("profileChangedReopen");
            }
        }
        foreach (var profile in profiles)
            if (!Roots.Any(r => r.Profile.Id == profile.Id)) Roots.Add(CreateRoot(profile));
        foreach (var tab in Tabs)
        {
            var id = tab.Profile?.Id ?? tab.MissingProfileId;
            var profile = Profiles.FirstOrDefault(p => p.Id == id);
            // Re-evaluate access restrictions for future operations; an in-flight operation keeps its snapshot.
            tab.Profile = profile is null ? null : profile with { TargetHost = tab.Profile?.TargetHost ?? tab.MissingTargetHost };
            tab.IsConnected = profile is not null && IsProfileConnected(tab.Profile!);
        }
    }

    private ExplorerNodeViewModel CreateRoot(ConnectionProfile profile)
    {
        var root = new ExplorerNodeViewModel(_workspace, profile, profile.Name, metadata: Metadata);
        root.ConnectionChanged += (_, _) =>
        {
            // Only connected roots may refresh metadata remotely; disconnecting drops everything cached for the profile.
            if (root.IsConnected) Metadata.Connect(root.Profile); else Metadata.Disconnect(root.Profile.Id);
            foreach (var tab in Tabs.Where(t => t.Profile?.Id == profile.Id)) tab.IsConnected = IsProfileConnected(tab.Profile!);
        };
        return root;
    }

    private void OnMetadataChanged(object? sender, MetadataChangedEventArgs e)
    {
        if (_disposed) return;
        void Refresh()
        {
            RefreshSyntaxContexts();
            foreach (var tab in Tabs) tab.NotifyMetadataChanged(e);
        }
        if (_context is null || SynchronizationContext.Current == _context) Refresh();
        else _context.Post(_ => Refresh(), null);
    }

    private void RefreshSyntaxContexts()
    {
        if (_disposed) return;
        foreach (var tab in Tabs) tab.RefreshSyntaxContext();
    }

    /// <summary>Per-connection opt-in for automatic schema sampling (names and types only), persisted additively.</summary>
    public async Task SetSchemaSamplingAllowedAsync(Guid profileId, bool allowed)
    {
        Metadata.SetSchemaSamplingAllowed(profileId, allowed);
        await SaveSessionAsync();
    }

    /// <summary>
    /// Per-connection opt-out of <em>serving</em> already-learned schema to the autocomplete catalog (L15), mirroring
    /// <see cref="SetSchemaSamplingAllowedAsync"/> exactly: update the in-memory decision first, then persist. A
    /// null <see cref="_learnedSchemaOptOut"/> (no host wired it) makes this a no-op besides the save, matching
    /// <see cref="SaveSessionAsync"/> writing an empty list in that case. There is no dedicated per-connection UI for
    /// this toggle yet — only the general <see cref="AutocompleteSettings.LearnedSchemaEnabled"/> switch has a
    /// screen — so today this is reachable only from tests and future callers, not from a menu.
    /// </summary>
    public async Task SetLearnedSchemaExcludedAsync(Guid profileId, bool excluded)
    {
        _learnedSchemaOptOut?.SetServingExcluded(profileId, excluded);
        await SaveSessionAsync();
    }

    public bool IsProfileConnected(ConnectionProfile profile) => Roots.Any(r => r.Profile.Id == profile.Id && r.Profile.TargetHost == profile.TargetHost && r.IsConnected);

    public async Task OpenConnectionAsync(ConnectionProfile profile)
    {
        var revision = _environmentRevision;
        var root = Roots.FirstOrDefault(r => r.Profile.Id == profile.Id);
        if (root is null) { root = CreateRoot(profile); Roots.Add(root); }
        await root.LoadAsync();
        if (revision != _environmentRevision || !Roots.Contains(root)) throw new InvalidOperationException(T("targetChangedReopen"));
        if (!root.IsConnected) throw new InvalidOperationException(root.Message.Length > 0 ? root.Message : T("connectionNotOpen"));
        root.IsExpanded = true;
        SelectedNode = root;
        ExplorerStatus = F("explorerDatabasesLoaded", root.Children.Count);
        if (ActiveTab is { Profile: null, IsDirty: false }) BindActiveTab(root.Profile, root.Profile.DefaultDatabase ?? "", "");
        ApplySearch();
    }

    public void Disconnect(ExplorerNodeViewModel node)
    {
        node.Root.Invalidate();
        foreach (var tab in Tabs.Where(t => t.Profile?.Id == node.Profile.Id)) tab.IsConnected = false;
        Details.Clear();
        ExplorerStatus = T("connectionDisconnected");
    }

    public async Task SelectInstanceAsync(ExplorerNodeViewModel node, string? host)
    {
        var root = Roots.FirstOrDefault(r => r.Profile.Id == node.Profile.Id);
        if (root is null) throw new InvalidOperationException(T("profileRemovedReopen"));
        var profile = root.Profile with { TargetHost = host };
        var index = Roots.IndexOf(root);
        if (index < 0) return;
        Disconnect(root);
        Roots[index] = CreateRoot(profile);
        await OpenConnectionAsync(profile);
    }

    public async Task DropExplorerIndexAsync(ExplorerNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Index is null || node.Collection is null || node.Parent?.Kind != ExplorerNodeKind.Indexes)
            throw new InvalidOperationException(T("openIndexRequired"));

        // Snapshot all operation inputs before the first await. Explorer selection may change while metadata loads.
        var profile = node.Profile with { };
        var database = node.Database;
        var collection = node.Collection;
        var indexName = node.Index.Name;
        var indexesNode = node.Parent!;
        var request = new IndexDropRequest(database, collection, indexName).Validate();
        var dropAttempted = false;
        var auditAttempted = false;
        IReadOnlyList<string>? before = null;

        try
        {
            profile.EnsureWriteAllowed();
            EnsureExplorerIndexTargetIsLive(node, profile);

            before = await _workspace.GetIndexesAsync(profile, database, collection);
            if (!ContainsIndexName(before, indexName))
                throw new InvalidOperationException(T("indexRefreshFailed"));

            // Disconnect/profile replacement during the pre-read cancels the pending action before dispatch.
            EnsureExplorerIndexTargetIsLive(node, profile);
            dropAttempted = true;
            await _workspace.DropIndexAsync(profile, request);

            IReadOnlyList<string>? after = null;
            string? refreshError = null;
            try
            {
                // Re-read through the direct service for the captured namespace; do not follow current selection.
                after = await _workspace.GetIndexesAsync(profile, database, collection);
            }
            catch (Exception exception)
            {
                refreshError = DesktopOperationErrorMessages.Describe(exception);
            }

            auditAttempted = true;
            var auditRecorded = await RecordExplorerIndexDropAuditAsync(profile, database, collection, indexName, before, after);
            if (after is null)
            {
                ExplorerStatus = auditRecorded
                    ? F("indexMutationRefreshFailed", indexName, refreshError ?? T("indexRefreshFailed"))
                    : F("indexRefreshAndAuditFailed", indexName, refreshError ?? T("indexRefreshFailed"));
                return;
            }

            if (ContainsIndexName(after, indexName))
            {
                ExplorerStatus = auditRecorded
                    ? F("indexMutationRefreshFailed", indexName, T("indexRefreshFailed"))
                    : F("indexRefreshAndAuditFailed", indexName, T("indexRefreshFailed"));
                return;
            }

            // Update the originating tree node only; another selected node/collection is left untouched.
            if (indexesNode.Children.Remove(node))
            {
                node.Invalidate();
            }

            if (!auditRecorded)
            {
                ExplorerStatus = F("auditNotRecorded", T("indexAudit"));
                return;
            }

            ExplorerStatus = F("indexRemovedExplorer", indexName);
        }
        catch (Exception exception)
        {
            if (dropAttempted && !auditAttempted)
            {
                var auditRecorded = await RecordExplorerIndexDropAuditAsync(profile, database, collection, indexName, before, null);
                var detail = DesktopOperationErrorMessages.Describe(exception);
                ExplorerStatus = auditRecorded
                    ? F("indexMutationRefreshFailed", indexName, detail)
                    : F("indexRefreshAndAuditFailed", indexName, detail);
            }
            else
            {
                ExplorerStatus = DesktopOperationErrorMessages.Describe(exception);
            }

            throw;
        }
    }

    private void EnsureExplorerIndexTargetIsLive(ExplorerNodeViewModel node, ConnectionProfile capturedProfile)
    {
        if (!node.Root.IsConnected
            || !Roots.Any(root => ReferenceEquals(root, node.Root) && root.Profile == capturedProfile && root.IsConnected))
        {
            throw new InvalidOperationException(T("targetChangedReopen"));
        }
    }

    private async Task<bool> RecordExplorerIndexDropAuditAsync(
        ConnectionProfile profile,
        string database,
        string collection,
        string indexName,
        IReadOnlyList<string>? before,
        IReadOnlyList<string>? after)
    {
        try
        {
            static string State(IReadOnlyList<string>? definitions, string name) => definitions is null
                ? "unknown"
                : definitions.Any(definition => HasIndexName(definition, name)) ? "present=true" : "present=false";

            await _workspace.SaveAuditAsync(AuditEntry.Create(
                "index.drop",
                profile.Id,
                database,
                collection,
                F("indexAuditSummary", indexName, State(before, indexName), State(after, indexName))));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsIndexName(IEnumerable<string> definitions, string name) =>
        definitions.Any(definition => HasIndexName(definition, name));

    private static bool HasIndexName(string definition, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(definition);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("name", out var indexName)
                && string.Equals(indexName.GetString(), name, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    partial void OnSelectedNodeChanged(ExplorerNodeViewModel? value) => _ = Details.SelectAsync(value);

    [RelayCommand]
    private async Task RefreshExplorerAsync()
    {
        try
        {
            if (SelectedNode is { } node)
            {
                await (node.CanExpand ? node : node.Parent ?? node).LoadAsync();
                await Details.SelectAsync(node);
            }
            else await ReloadProfilesAsync();
            ApplySearch();
        }
        catch (Exception ex) { ExplorerStatus = DesktopOperationErrorMessages.Describe(ex); }
    }

    partial void OnSearchChanged(string value) => ApplySearch();
    private void ApplySearch()
    {
        foreach (var root in Roots) root.Filter(Search.Trim());
        if (!string.IsNullOrWhiteSpace(Search)) ExplorerStatus = Roots.Any(r => r.IsVisible) ? T("loadedSearchLimited") : T("loadedSearchNone");
        else if (Roots.Count > 0) ExplorerStatus = T("expandDatabaseCollections");
    }

    // Loaded metadata only (Peek): typing never schedules a remote refresh in this path.
    private EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxNamespace[] KnownSyntaxNamespaces()
    {
        var names = new Dictionary<Guid, string>();
        foreach (var profile in Profiles) names.TryAdd(profile.Id, profile.Name);
        var result = Profiles.Select(profile => new EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxNamespace(profile.Name)).ToList();
        foreach (var item in Metadata.SnapshotNamespaces(4096))
            if (names.TryGetValue(item.ProfileId, out var name)) result.Add(new(name, item.Database, item.Collection, item.Index));
        return result.Take(4096).ToArray();
    }

    private string[] KnownAutocompleteNames(WorkspaceTabViewModel tab)
    {
        var names = Profiles.Select(profile => profile.Name).ToList();
        foreach (var root in Roots.Where(root => root.Profile.Id == tab.Profile?.Id))
        {
            var identity = ConnectionIdentity.From(root.Profile);
            foreach (var database in Metadata.GetDatabases(identity, MetadataAccess.Peek).Value ?? [])
            {
                names.Add(database);
                if (database == tab.Database)
                    names.AddRange((Metadata.GetCollections(identity, database, MetadataAccess.Peek).Value ?? []).Select(collection => collection.Name));
            }
        }
        return names.Distinct(StringComparer.Ordinal).Take(256).ToArray();
    }
}
