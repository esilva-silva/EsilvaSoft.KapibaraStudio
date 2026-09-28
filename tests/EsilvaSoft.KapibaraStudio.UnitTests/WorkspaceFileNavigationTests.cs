using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class WorkspaceFileNavigationTests
{
    [Test]
    public async Task CreateAndRenameFolderPreservesOpenTextAndDirtyState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-workspace-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var context = new WorkspaceTestContext();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, workspaceFiles: new LocalWorkspaceFileService());
            await vm.InitializeAsync();
            await vm.SetWorkspaceFolderAsync(directory);
            await vm.CreateWorkspaceEntryAsync("nested", true, vm.WorkspaceFiles.Single());
            var folder = vm.WorkspaceFiles.Single().Children.Single();
            await vm.CreateWorkspaceEntryAsync("notes.txt", false, folder);
            var tab = vm.ActiveTab!;
            Assert.That(File.ReadAllBytes(tab.FilePath), Is.Empty);
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
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task FailedOpenDoesNotAddOrActivateAnEmptyTab()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync();
        var active = vm.ActiveTab;
        var count = vm.Tabs.Count;
        Assert.ThrowsAsync<FileNotFoundException>(() => vm.OpenTextFileAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt")));
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
        var first = Path.Combine(Path.GetTempPath(), "first");
        var second = Path.Combine(Path.GetTempPath(), "second");
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
        var root = Path.Combine(Path.GetTempPath(), "workspace");
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
