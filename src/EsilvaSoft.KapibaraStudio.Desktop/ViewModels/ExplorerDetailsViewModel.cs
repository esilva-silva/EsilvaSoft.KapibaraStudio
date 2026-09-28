using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class ExplorerDetailsViewModel(WorkspaceService workspace) : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private CancellationTokenSource? _cancellation;
    private int _generation;
    public ObservableCollection<InstanceInfo> Instances { get; } = [];
    [ObservableProperty] private InstanceInfo? _selectedInstance;
    [ObservableProperty] private string _title = T("detailsTitle");
    [ObservableProperty] private string _text = T("selectTreeItem");
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isConnection;
    public ExplorerNodeViewModel? Node { get; private set; }
    public Task SelectionTask { get; private set; } = Task.CompletedTask;

    public Task SelectAsync(ExplorerNodeViewModel? node) => SelectionTask = SelectCoreAsync(node);
    public Task SelectServerAsync(ExplorerNodeViewModel node) => SelectionTask = SelectCoreAsync(node, true);
    private async Task SelectCoreAsync(ExplorerNodeViewModel? node, bool serverStatus = false)
    {
        Clear(); Node = node;
        if (node is null) return;
        var generation = _generation;
        Title = node.Name;
        IsConnection = node.IsConnection;
        Text = node.Context + (node.IsConnection ? "\nServidor(es): " + node.Profile.Endpoint : "");
        if (node.IsIndex)
        {
            var index = node.Index!;
            Text += "\n" + F("indexDetailText", index.Name, index.Keys, index.Unique ? T("yes") : T("no"), index.Sparse ? T("yes") : T("no"), index.Ttl, index.PartialFilter, index.Definition);
            return;
        }
        if (!node.Root.IsConnected) { Text += "\n" + T("disconnectedMetadata"); return; }
        IsLoading = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        try
        {
            string details;
            TopologyInfo? topology = null;
            if (node.IsConnection && serverStatus) details = await workspace.GetServerStatusAsync(node.Profile, cancellation.Token);
            else if (node.IsConnection)
            {
                topology = await workspace.GetExplorerTopologyAsync(node.Profile, cancellation.Token);
                details = F("serverDetails", node.Profile.Endpoint, node.Profile.DefaultDatabase ?? T("notDefined"), node.Profile.IsReadOnly ? T("yes") : T("no"), topology.Kind, topology.ReplicaSet ?? T("noReplicaSet"), topology.Server, topology.Definition);
            }
            else if (node.IsDatabase) details = await workspace.GetDatabaseStatsAsync(node.Profile, node.Database, cancellation.Token);
            else if (node.HasCollection) details = await workspace.GetExplorerCollectionDetailsAsync(node.Profile, node.Database, node.Collection!, cancellation.Token);
            else details = node.Message;
            if (generation != _generation) return;
            Text = node.Context + "\n\n" + details;
            if (topology is not null) foreach (var instance in topology.Instances) Instances.Add(instance);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (generation == _generation) Text = node.Context + "\n" + F("loadDetailsFailed", DesktopOperationErrorMessages.Describe(ex)); }
        finally
        {
            if (generation == _generation) { IsLoading = false; _cancellation = null; }
        }
    }

    public void Clear()
    {
        _generation++; _cancellation?.Cancel(); _cancellation = null;
        Instances.Clear(); SelectedInstance = null; IsLoading = false; IsConnection = false;
        Node = null; Title = T("detailsTitle"); Text = T("selectTreeItem");
    }

    public void RefreshLanguage()
    {
        if (Node is null)
        {
            Title = T("detailsTitle");
            Text = T("selectTreeItem");
        }
    }
}
