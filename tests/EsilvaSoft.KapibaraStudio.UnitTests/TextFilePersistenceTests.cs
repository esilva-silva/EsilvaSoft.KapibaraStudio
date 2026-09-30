using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class TextFilePersistenceTests
{
    private readonly string _directory = SyntheticPaths.Combine("synthetic-text");
    private MemoryTextFiles _files = null!;
    [SetUp] public void SetUp() => _files = new MemoryTextFiles();
    [Test]
    public async Task TabRestoresEncodingDirtyBaselineAndRevisionWithoutReplacingBufferFromDisk()
    {
        using var context = new WorkspaceTestContext(textFiles: _files);
        var path = Path.Combine(_directory, "unicode.txt");
        await _files.SaveAsync(path, "original", TextFileEncoding.Utf16BigEndian, true);
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = "Texto" };
        await tab.OpenAsync(path);
        tab.Text = "draft";
        var snapshot = tab.Snapshot();
        await context.Repository.SaveSessionAsync(new WorkspaceSession { WorkspaceRootPath = _directory, ActiveSidebar = "Files", Tabs = [snapshot] });
        await _files.SaveAsync(path, "external");
        var session = await context.Repository.LoadSessionAsync();
        using var restored = new WorkspaceTabViewModel(context.Workspace);
        restored.Restore(session.Tabs.Single(), null);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Text, Is.EqualTo("draft"));
            Assert.That(restored.FileEncoding, Is.EqualTo(TextFileEncoding.Utf16BigEndian));
            Assert.That(restored.FileHasBom, Is.True);
            Assert.That(restored.FileRevision, Is.EqualTo(tab.FileRevision));
            Assert.That(restored.IsDirty, Is.True);
        });
        Assert.ThrowsAsync<TextFileConflictException>(() => restored.SaveAsync(path));
        restored.Text = "original";
        Assert.That(restored.IsDirty, Is.False, "Undo to the saved baseline clears the marker after recovery.");
    }

    [Test]
    public async Task TabSavePreservesEncodingAndUndoClearsDirty()
    {
        using var context = new WorkspaceTestContext(textFiles: _files);
        var path = Path.Combine(_directory, "tab.txt");
        await _files.SaveAsync(path, "base", TextFileEncoding.Utf32BigEndian, true);
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = "Texto" };
        await tab.OpenAsync(path); tab.Text = "changed"; await tab.SaveAsync(path);
        var loaded = await _files.LoadAsync(path);
        Assert.That((loaded.Content, loaded.Encoding, loaded.HasBom), Is.EqualTo(("changed", TextFileEncoding.Utf32BigEndian, true)));
        tab.Text = "edited"; Assert.That(tab.IsDirty, Is.True);
        tab.Text = "changed"; Assert.That(tab.IsDirty, Is.False);
    }

    [Test]
    public async Task EditingWhileSavingKeepsNewBufferDirtyAndSavesCapturedContent()
    {
        var controlled = new DelayedTextFiles();
        using var context = new WorkspaceTestContext(textFiles: controlled);
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = "Texto", Text = "captured" };
        var path = Path.Combine(_directory, "pending.txt");
        var save = tab.SaveAsync(path);
        await controlled.Started.Task;
        tab.Text = "edited during save";
        controlled.Release.SetResult();
        await save;
        Assert.Multiple(() =>
        {
            Assert.That(controlled.CapturedText, Is.EqualTo("captured"));
            Assert.That(tab.Text, Is.EqualTo("edited during save"));
            Assert.That(tab.IsDirty, Is.True);
            Assert.That(tab.FilePath, Is.EqualTo(path));
        });
        tab.Text = "captured";
        Assert.That(tab.IsDirty, Is.False);
    }

    private sealed class DelayedTextFiles : ITextFileService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? CapturedText { get; private set; }
        public Task<TextFileDocument> LoadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<TextFileRevision> SaveAsync(string path, string content, TextFileEncoding encoding = TextFileEncoding.Utf8,
            bool hasBom = false, TextFileRevision? expectedRevision = null, CancellationToken cancellationToken = default)
        {
            CapturedText = content; Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new TextFileRevision(content.Length, DateTime.UtcNow, new string('A', 64));
        }
    }

}
