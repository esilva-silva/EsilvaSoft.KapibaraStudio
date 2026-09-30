using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Uma entrada do workspace local. A árvore é deliberadamente independente do explorer MongoDB.</summary>
public sealed partial class WorkspaceFileNodeViewModel : ObservableObject
{
    public WorkspaceFileNodeViewModel(string path, string workspaceRoot, bool isDirectory = true, Func<WorkspaceFileNodeViewModel, Task>? expand = null, bool placeholder = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(workspaceRoot))
            throw new ArgumentException("Os caminhos dos itens do workspace precisam ser absolutos.");
        FullPath = Path.GetFullPath(path);
        Name = Path.GetFileName(FullPath);
        if (string.IsNullOrEmpty(Name)) Name = FullPath;
        IsDirectory = isDirectory;
        IsExpanded = false;
        Children = [];
        WorkspaceRoot = workspaceRoot;
        ExpandAsync = expand;
        IsPlaceholder = placeholder;
        if (isDirectory && !placeholder) Children.Add(new(path, workspaceRoot, false, placeholder: true) { Name = "Carregando…" });
    }

    public string FullPath { get; private set; }
    public string WorkspaceRoot { get; }
    public Func<WorkspaceFileNodeViewModel, Task>? ExpandAsync { get; }
    public bool IsPlaceholder { get; }
    public bool IsActionable => !IsPlaceholder;
    public bool CanModify => !IsPlaceholder && !string.Equals(FullPath, WorkspaceRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    partial void OnIsExpandedChanged(bool value) { if (value && ExpandAsync is not null) _ = ExpandAsync(this); }
    public bool IsLoaded { get; private set; }
    public int LoadVersion { get; set; }
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    public ObservableCollection<WorkspaceFileNodeViewModel> Children { get; }
    public string Icon => IsDirectory ? "▾" : "·";

    public void UpdatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("O arquivo precisa ter caminho absoluto.", nameof(path));
        FullPath = Path.GetFullPath(path);
        Name = Path.GetFileName(FullPath);
    }
    public void ReplaceChildren(IEnumerable<WorkspaceFileNodeViewModel> children)
    {
        Children.Clear();
        foreach (var child in children.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) Children.Add(child);
        IsLoaded = true; IsLoading = false;
    }
}
