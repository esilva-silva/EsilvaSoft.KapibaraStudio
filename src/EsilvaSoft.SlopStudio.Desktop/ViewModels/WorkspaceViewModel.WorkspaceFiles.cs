using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private CancellationTokenSource _workspaceEnumeration = new();
    private readonly SemaphoreSlim _workspaceMutation = new(1, 1);
    private readonly SemaphoreSlim _openTextFile = new(1, 1);
    private static StringComparison FilePathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public ObservableCollection<WorkspaceFileNodeViewModel> WorkspaceFiles { get; } = [];
    [ObservableProperty] private string? _workspaceRootPath;
    [ObservableProperty] private WorkspaceFileNodeViewModel? _selectedWorkspaceFile;
    [ObservableProperty] private string _selectedSidebar = "Connections";
    [ObservableProperty] private string _workspaceStatus = "Abra uma pasta para começar.";
    public bool HasWorkspace => !string.IsNullOrWhiteSpace(WorkspaceRootPath);
    public bool IsFilesSidebar => SelectedSidebar == "Files";
    public bool IsConnectionsSidebar => !IsFilesSidebar;
    public string OpenWorkspaceLabel => HasWorkspace ? "Trocar pasta" : "Abrir pasta";
    partial void OnWorkspaceRootPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasWorkspace)); OnPropertyChanged(nameof(OpenWorkspaceLabel));
        RefreshAgentReadScopes();
    }
    partial void OnSelectedSidebarChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilesSidebar)); OnPropertyChanged(nameof(IsConnectionsSidebar)); ScheduleSave();
    }
    private void CancelWorkspaceEnumeration()
    {
        _workspaceEnumeration.Cancel(); _workspaceEnumeration.Dispose(); _workspaceEnumeration = new();
    }
    partial void DisposeWorkspaceFiles()
    {
        CancelWorkspaceEnumeration();
        _workspaceMutation.Dispose();
        _openTextFile.Dispose();
    }
    public async Task SetWorkspaceFolderAsync(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (WorkspaceFileService is null) throw new InvalidOperationException("Serviço de arquivos indisponível.");
        CancelWorkspaceEnumeration(); WorkspaceRootPath = root; WorkspaceFiles.Clear(); SelectedWorkspaceFile = null; SelectedSidebar = "Files";
        var node = CreateWorkspaceNode(root, root, true);
        WorkspaceFiles.Add(node);
        var load = LoadChildrenAsync(node);
        node.IsExpanded = true;
        await load; ScheduleSave();
    }
    private WorkspaceFileNodeViewModel CreateWorkspaceNode(string path, string root, bool directory) => new(path, root, directory, ExpandWorkspaceNodeAsync);
    public async Task RefreshWorkspaceFolderAsync()
    {
        if (WorkspaceFiles.FirstOrDefault() is not { } root) return;
        var node = SelectedWorkspaceFile is { IsDirectory: true, IsPlaceholder: false } selected ? selected : root;
        await LoadChildrenAsync(node);
    }
    private async Task LoadChildrenAsync(WorkspaceFileNodeViewModel node)
    {
        if (WorkspaceFileService is null || node.IsPlaceholder || !node.IsDirectory || !string.Equals(node.WorkspaceRoot, WorkspaceRootPath, FilePathComparison)) return;
        var token = _workspaceEnumeration.Token;
        var request = ++node.LoadVersion;
        node.IsLoading = true; node.Status = "Carregando…"; WorkspaceStatus = "Carregando " + node.Name + "…";
        try
        {
            var relative = Path.GetRelativePath(node.WorkspaceRoot, node.FullPath);
            var entries = await WorkspaceFileService.ListAsync(node.WorkspaceRoot, relative == "." ? null : relative, token);
            if (token.IsCancellationRequested || request != node.LoadVersion || _disposed) return;
            node.ReplaceChildren(entries.Select(e => CreateWorkspaceNode(e.FullPath, node.WorkspaceRoot, e.IsDirectory)));
            node.Status = entries.Count == 0 ? "Pasta vazia." : "";
            WorkspaceStatus = entries.Count == 0 ? "Pasta vazia: " + node.FullPath : node.FullPath;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || request != node.LoadVersion || _disposed) return;
            node.Status = "Não foi possível carregar. Use Atualizar. " + DesktopOperationErrorMessages.Describe(ex); WorkspaceStatus = node.Status;
        }
        finally { if (request == node.LoadVersion) node.IsLoading = false; }
    }
    public Task ExpandWorkspaceNodeAsync(WorkspaceFileNodeViewModel node) => node.IsLoading || node.IsLoaded ? Task.CompletedTask : LoadChildrenAsync(node);
    public void CloseWorkspaceFolder()
    {
        CancelWorkspaceEnumeration(); WorkspaceRootPath = null; WorkspaceFiles.Clear(); SelectedWorkspaceFile = null;
        WorkspaceStatus = "Nenhuma pasta aberta."; ScheduleSave();
    }
    public async Task<WorkspaceTabViewModel> OpenTextFileAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        await _openTextFile.WaitAsync();
        try
        {
            var existing = Tabs.FirstOrDefault(t => string.Equals(t.FilePath, fullPath, FilePathComparison));
            if (existing is not null) { ActiveTab = existing; return existing; }
            var tab = CreateTab();
            tab.Restore(new Core.WorkspaceDraft { Mode = "Texto", Text = "", IsDirty = false }, null);
            try { await tab.OpenAsync(fullPath); } catch { tab.Dispose(); throw; }
            if (_disposed) { tab.Dispose(); throw new ObjectDisposedException(nameof(WorkspaceViewModel)); }
            Register(tab); ActiveTab = tab; ScheduleSave(); return tab;
        }
        finally { _openTextFile.Release(); }
    }
    public void CreateUntitledTextFile()
    {
        var tab = CreateTab(); tab.Restore(new Core.WorkspaceDraft { Mode = "Texto", Text = "", IsDirty = true }, null);
        Register(tab); ActiveTab = tab;
    }
    public bool IsInsideWorkspace(string path)
    {
        if (WorkspaceRootPath is null) return false;
        var root = Path.TrimEndingDirectorySeparator(WorkspaceRootPath);
        var full = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return !string.Equals(root, full, FilePathComparison) && full.StartsWith(prefix, FilePathComparison);
    }
    public async Task CreateWorkspaceEntryAsync(string name, bool directory, WorkspaceFileNodeViewModel? selected)
    {
        if (WorkspaceRootPath is not { } root || WorkspaceFileService is null) return;
        ValidateEntryName(name);
        var parent = selected is { IsPlaceholder: false } ? selected.IsDirectory ? selected.FullPath : Path.GetDirectoryName(selected.FullPath)! : root;
        if (selected is not null && !string.Equals(selected.WorkspaceRoot, root, FilePathComparison)) throw new IOException("A pasta selecionada não pertence ao workspace atual.");
        var relative = Path.GetRelativePath(root, Path.Combine(parent, name));
        await _workspaceMutation.WaitAsync();
        try
        {
            var path = directory ? await WorkspaceFileService.CreateDirectoryAsync(root, relative) : await WorkspaceFileService.CreateFileAsync(root, relative);
            if (string.Equals(root, WorkspaceRootPath, FilePathComparison) && FindWorkspaceNode(parent) is { } parentNode)
            { await LoadChildrenAsync(parentNode); parentNode.IsExpanded = true; }
            if (!directory) await OpenTextFileAsync(path);
        }
        finally { _workspaceMutation.Release(); }
    }
    private WorkspaceFileNodeViewModel? FindWorkspaceNode(string path)
    {
        var pending = new Stack<WorkspaceFileNodeViewModel>(WorkspaceFiles);
        while (pending.TryPop(out var node))
        {
            if (string.Equals(node.FullPath, path, FilePathComparison)) return node;
            foreach (var child in node.Children.Where(c => !c.IsPlaceholder)) pending.Push(child);
        }
        return null;
    }
    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("Informe apenas um nome válido para o item.");
    }
    private string RequireMutableNode(WorkspaceFileNodeViewModel node)
    {
        if (WorkspaceRootPath is not { } root || node.IsPlaceholder || !string.Equals(node.WorkspaceRoot, root, FilePathComparison) || !IsInsideWorkspace(node.FullPath))
            throw new IOException("Selecione um item dentro da raiz do workspace. A própria raiz não pode ser alterada.");
        return root;
    }
    private static bool AffectsFile(WorkspaceFileNodeViewModel node, string path) => string.Equals(path, node.FullPath, FilePathComparison) ||
        (node.IsDirectory && path.StartsWith(Path.TrimEndingDirectorySeparator(node.FullPath) + Path.DirectorySeparatorChar, FilePathComparison));
    public async Task RenameWorkspaceNodeAsync(WorkspaceFileNodeViewModel node, string name)
    {
        ValidateEntryName(name); var root = RequireMutableNode(node);
        if (WorkspaceFileService is null) throw new InvalidOperationException("Serviço de arquivos indisponível.");
        await _workspaceMutation.WaitAsync();
        try
        {
            var target = await WorkspaceFileService.RenameAsync(root, Path.GetRelativePath(root, node.FullPath), name);
            foreach (var tab in Tabs.Where(t => AffectsFile(node, t.FilePath)))
            {
                var dirty = tab.IsDirty; tab.FilePath = target + tab.FilePath[node.FullPath.Length..]; tab.IsDirty = dirty;
            }
            if (string.Equals(root, WorkspaceRootPath, FilePathComparison) && FindWorkspaceNode(Path.GetDirectoryName(node.FullPath)!) is { } parent)
            {
                await LoadChildrenAsync(parent);
                SelectedWorkspaceFile = parent.Children.FirstOrDefault(child => string.Equals(child.FullPath, target, FilePathComparison)) ?? parent;
            }
            ScheduleSave();
        }
        finally { _workspaceMutation.Release(); }
    }
    public async Task DeleteWorkspaceNodeAsync(WorkspaceFileNodeViewModel node)
    {
        var root = RequireMutableNode(node);
        if (WorkspaceFileService is null) throw new InvalidOperationException("Serviço de arquivos indisponível.");
        await _workspaceMutation.WaitAsync();
        try
        {
            await WorkspaceFileService.MoveToTrashAsync(root, Path.GetRelativePath(root, node.FullPath));
            foreach (var tab in Tabs.Where(t => AffectsFile(node, t.FilePath)))
            {
                tab.FilePath = ""; tab.IsDirty = true;
                tab.Status = "Arquivo enviado para a lixeira. Use Salvar como para preservar este texto.";
            }
            if (string.Equals(root, WorkspaceRootPath, FilePathComparison) && FindWorkspaceNode(Path.GetDirectoryName(node.FullPath)!) is { } parent)
            {
                await LoadChildrenAsync(parent); SelectedWorkspaceFile = parent;
            }
            ScheduleSave();
        }
        finally { _workspaceMutation.Release(); }
    }
}
