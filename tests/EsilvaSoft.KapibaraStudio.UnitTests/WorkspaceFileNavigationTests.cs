using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class WorkspaceFileNavigationTests
{
    [Test]
    public async Task CreateAndRenameFolderPreservesOpenTextAndDirtyState()
    {
        var directory = SyntheticPaths.Combine("synthetic-workspace");
        var texts = new MemoryTextFiles();
        {
            using var context = new WorkspaceTestContext(textFiles: texts);
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, workspaceFiles: new NavigationFiles(texts));
            await vm.InitializeAsync();
            await vm.SetWorkspaceFolderAsync(directory);
            await vm.CreateWorkspaceEntryAsync("nested", true, vm.WorkspaceFiles.Single());
            var folder = vm.WorkspaceFiles.Single().Children.Single();
            await vm.CreateWorkspaceEntryAsync("notes.txt", false, folder);
            var tab = vm.ActiveTab!;
            Assert.That((await texts.LoadAsync(tab.FilePath)).Content, Is.Empty);
            Assert.That(tab.Mode, Is.EqualTo("Texto"));
            Assert.That(tab.Profile, Is.Null);
            tab.Text = "unsaved draft";
            await vm.RenameWorkspaceNodeAsync(folder, "renamed");
            Assert.Multiple(() =>
            {
                Assert.That(tab.FilePath, Is.EqualTo(Path.Combine(directory, "renamed", "notes.txt")));
                Assert.That(tab.Text, Is.EqualTo("unsaved draft"));
                Assert.That(tab.IsDirty, Is.True);
                Assert.That(vm.IsFilesSidebar, Is.True);
            });
            vm.CloseWorkspaceFolder();
            Assert.That(vm.Tabs, Does.Contain(tab));
            Assert.That(tab.Text, Is.EqualTo("unsaved draft"));
        }
    }

    // Only the contract used by this navigation scenario is simulated. Containment, byte encoding and native
    // filesystem semantics belong to the real adapter's integration tests.
    private sealed class NavigationFiles(MemoryTextFiles texts) : IWorkspaceFileService
    {
        private readonly Dictionary<string, bool> _entries = new(StringComparer.Ordinal);
        public Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(string rootPath, string? relativeDirectory = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = Path.Combine(rootPath, relativeDirectory ?? string.Empty);
            return Task.FromResult<IReadOnlyList<WorkspaceFileEntry>>(_entries
                .Where(entry => Path.GetDirectoryName(entry.Key) == parent)
                .Select(entry => new WorkspaceFileEntry(Path.GetFileName(entry.Key), entry.Key, entry.Value)).ToArray());
        }
        public async Task<string> CreateFileAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(rootPath, relativePath);
            await texts.SaveAsync(path, string.Empty, cancellationToken: cancellationToken);
            _entries.Add(path, false);
            return path;
        }
        public Task<string> CreateDirectoryAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(rootPath, relativePath);
            _entries.Add(path, true);
            return Task.FromResult(path);
        }
        public Task<string> RenameAsync(string rootPath, string relativePath, string newName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(rootPath, relativePath);
            var target = Path.Combine(Path.GetDirectoryName(source)!, newName);
            var descendants = _entries.Where(entry => entry.Key == source || entry.Key.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
            foreach (var entry in descendants)
            {
                _entries.Remove(entry.Key);
                _entries.Add(target + entry.Key[source.Length..], entry.Value);
            }
            return Task.FromResult(target);
        }
        public Task MoveToTrashAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Test]
    public async Task FailedOpenDoesNotAddOrActivateAnEmptyTab()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync();
        var active = vm.ActiveTab;
        var count = vm.Tabs.Count;
        Assert.ThrowsAsync<FileNotFoundException>(() => vm.OpenTextFileAsync(SyntheticPaths.Combine(Guid.NewGuid() + ".txt")));
        Assert.That(vm.Tabs.Count, Is.EqualTo(count));
        Assert.That(vm.ActiveTab, Is.SameAs(active));
    }

    [Test]
    public async Task OldEnumerationCannotReplaceNewRootEvenWhenServiceIgnoresCancellation()
    {
        using var context = new WorkspaceTestContext();
        var files = new DelayedFiles();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, workspaceFiles: files);
        await vm.InitializeAsync();
        var first = SyntheticPaths.Combine("first");
        var second = SyntheticPaths.Combine("second");
        var pending = vm.SetWorkspaceFolderAsync(first);
        await vm.SetWorkspaceFolderAsync(second);
        files.First.SetResult([new("stale.txt", Path.Combine(first, "stale.txt"), false)]);
        await pending;
        Assert.That(vm.WorkspaceRootPath, Is.EqualTo(second));
        Assert.That(vm.WorkspaceFiles.Single().FullPath, Is.EqualTo(second));
        Assert.That(vm.WorkspaceFiles.Single().Children, Is.Empty);
        Assert.That(files.FirstToken.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task SuccessfulTrashDetachesDescendantTabsWithoutDiscardingBuffers()
    {
        using var context = new WorkspaceTestContext();
        var files = new DelayedFiles { DelayFirst = false };
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, workspaceFiles: files);
        await vm.InitializeAsync();
        var root = SyntheticPaths.Combine("workspace");
        await vm.SetWorkspaceFolderAsync(root);
        var tab = vm.ActiveTab!;
        tab.FilePath = Path.Combine(root, "nested", "draft.txt"); tab.Text = "preserved";
        await vm.DeleteWorkspaceNodeAsync(new(Path.Combine(root, "nested"), root));
        Assert.That(files.Trashed, Is.EqualTo("nested"));
        Assert.That(tab.FilePath, Is.Empty);
        Assert.That(tab.Text, Is.EqualTo("preserved"));
        Assert.That(tab.IsDirty, Is.True);
    }

    private sealed class DelayedFiles : IWorkspaceFileService
    {
        public TaskCompletionSource<IReadOnlyList<WorkspaceFileEntry>> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken FirstToken { get; private set; }
        public bool DelayFirst { get; set; } = true;
        public string? Trashed { get; private set; }
        private int _calls;
        public Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(string rootPath, string? relativeDirectory = null, CancellationToken cancellationToken = default)
        {
            if (++_calls == 1 && DelayFirst) { FirstToken = cancellationToken; return First.Task; }
            return Task.FromResult<IReadOnlyList<WorkspaceFileEntry>>([]);
        }
        public Task<string> CreateFileAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> CreateDirectoryAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> RenameAsync(string rootPath, string relativePath, string newName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MoveToTrashAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default) { Trashed = relativePath; return Task.CompletedTask; }
    }
}
