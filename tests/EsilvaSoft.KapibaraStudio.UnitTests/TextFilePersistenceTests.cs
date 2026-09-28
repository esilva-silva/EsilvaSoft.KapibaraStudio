using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class TextFilePersistenceTests
{
    private string _directory = null!;
    private readonly ITextFileService _files = new LocalScriptFileService();
    [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "slop-text-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TearDown] public void TearDown() => Directory.Delete(_directory, true);

    private static IEnumerable<TestCaseData> Encodings()
    {
        yield return new(new UTF8Encoding(false, true), TextFileEncoding.Utf8, false);
        yield return new(new UTF8Encoding(true, true), TextFileEncoding.Utf8, true);
        yield return new(new UnicodeEncoding(false, true, true), TextFileEncoding.Utf16LittleEndian, true);
        yield return new(new UnicodeEncoding(true, true, true), TextFileEncoding.Utf16BigEndian, true);
        yield return new(new UTF32Encoding(false, true, true), TextFileEncoding.Utf32LittleEndian, true);
        yield return new(new UTF32Encoding(true, true, true), TextFileEncoding.Utf32BigEndian, true);
    }

    [TestCaseSource(nameof(Encodings))]
    public async Task ExistingBytesRoundTripIncludingBomUnicodeAndMixedNewlines(Encoding codec, TextFileEncoding expectedEncoding, bool hasBom)
    {
        const string text = "ação 中文 🐱\r\nsegunda\rterceira\nfim";
        var path = Path.Combine(_directory, "document.unknown");
        var expected = codec.GetPreamble().Concat(codec.GetBytes(text)).ToArray();
        await File.WriteAllBytesAsync(path, expected);
        var loaded = await _files.LoadAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Content, Is.EqualTo(text));
            Assert.That(loaded.Encoding, Is.EqualTo(expectedEncoding));
            Assert.That(loaded.HasBom, Is.EqualTo(hasBom));
        });
        await _files.SaveAsync(path, loaded.Content, loaded.Encoding, loaded.HasBom, loaded.Revision);
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Encodings))]
    public async Task BomOnlyOrEmptyFilesRoundTrip(Encoding codec, TextFileEncoding expectedEncoding, bool hasBom)
    {
        var path = Path.Combine(_directory, "empty");
        await File.WriteAllBytesAsync(path, codec.GetPreamble());
        var loaded = await _files.LoadAsync(path);
        Assert.That(loaded.Content, Is.Empty);
        await _files.SaveAsync(path, loaded.Content, loaded.Encoding, loaded.HasBom, loaded.Revision);
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(codec.GetPreamble()));
    }

    [TestCase(new byte[] { 0xC3, 0x28 })]
    [TestCase(new byte[] { 0x41, 0x00, 0x42 })]
    [TestCase(new byte[] { 0x01, 0x02, 0x03 })]
    [TestCase(new byte[] { 0xFF, 0xFE, 0x41 })]
    public async Task RejectsMalformedOrBinaryInput(byte[] bytes)
    {
        var path = Path.Combine(_directory, "binary");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.ThrowsAsync<InvalidDataException>(() => _files.LoadAsync(path));
    }

    [Test]
    public async Task SameLengthAndTimestampExternalEditStillConflictsAndKeepsDisk()
    {
        var path = Path.Combine(_directory, "conflict.txt");
        await File.WriteAllTextAsync(path, "first");
        var document = await _files.LoadAsync(path);
        await File.WriteAllTextAsync(path, "other");
        File.SetLastWriteTimeUtc(path, document.Revision.LastWriteTimeUtc);
        Assert.ThrowsAsync<TextFileConflictException>(() => _files.SaveAsync(path, "local", expectedRevision: document.Revision));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("other"));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task ConcurrentSavesFromSameRevisionCannotBothOverwrite()
    {
        var path = Path.Combine(_directory, "race.txt");
        var revision = await _files.SaveAsync(path, "initial");
        var tasks = new[] { _files.SaveAsync(path, "alpha", expectedRevision: revision), _files.SaveAsync(path, "bravo", expectedRevision: revision) };
        try { await Task.WhenAll(tasks); } catch (TextFileConflictException) { }
        Assert.That(tasks.Count(task => task.IsCompletedSuccessfully), Is.EqualTo(1));
        Assert.That(tasks.Count(task => task.Exception?.InnerException is TextFileConflictException), Is.EqualTo(1));
    }

    [Test]
    public async Task CancelledSaveDoesNotTruncateOrLeaveTemporaryFiles()
    {
        var path = Path.Combine(_directory, "cancel.txt");
        await File.WriteAllTextAsync(path, "keep");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => _files.SaveAsync(path, "new", cancellationToken: cancellation.Token));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("keep"));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task MissingOrNewDestinationRevisionRequiresExplicitOverwrite()
    {
        var path = Path.Combine(_directory, "missing.txt");
        var revision = await _files.SaveAsync(path, "initial", expectedRevision: TextFileRevision.Missing);
        Assert.ThrowsAsync<TextFileConflictException>(() => _files.SaveAsync(path, "replace", expectedRevision: TextFileRevision.Missing));
        File.Delete(path);
        Assert.ThrowsAsync<TextFileConflictException>(() => _files.SaveAsync(path, "replace", expectedRevision: revision));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task CharacterLimitRejectsOversizedSaveAndLoadWithoutReplacingFile()
    {
        var path = Path.Combine(_directory, "large.txt");
        await File.WriteAllTextAsync(path, "keep");
        var tooLarge = new string('a', 16_000_001);
        Assert.ThrowsAsync<InvalidDataException>(() => _files.SaveAsync(path, tooLarge));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("keep"));
        await File.WriteAllTextAsync(path, tooLarge);
        Assert.ThrowsAsync<InvalidDataException>(() => _files.LoadAsync(path));
    }

    [Test]
    public async Task FailedReplacementLeavesOriginalAndRemovesTemporaryFile()
    {
        var path = Path.Combine(_directory, "locked.txt");
        await File.WriteAllTextAsync(path, "original");
        if (!OperatingSystem.IsWindows()) Assert.Ignore("FileShare denies replacement on Windows; Unix allows unlink of an open file.");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAsync(Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>(), () => _files.SaveAsync(path, "changed"));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("original"));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task TabRestoresEncodingDirtyBaselineAndRevisionWithoutReplacingBufferFromDisk()
    {
        using var context = new WorkspaceTestContext();
        var path = Path.Combine(_directory, "unicode.txt");
        var codec = new UnicodeEncoding(true, true, true);
        await File.WriteAllBytesAsync(path, codec.GetPreamble().Concat(codec.GetBytes("original")).ToArray());
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = "Texto" };
        await tab.OpenAsync(path);
        tab.Text = "draft";
        var snapshot = tab.Snapshot();
        await context.Repository.SaveSessionAsync(new WorkspaceSession { WorkspaceRootPath = _directory, ActiveSidebar = "Files", Tabs = [snapshot] });
        await File.WriteAllTextAsync(path, "external");
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
        using var context = new WorkspaceTestContext();
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

    [Test]
    public async Task VersionOneSessionMigratesWithoutLosingDraftsOrProfiles()
    {
        var path = Path.Combine(_directory, "session.db");
        var draft = new WorkspaceDraft { Text = "rascunho", IsDirty = true };
        using (var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct"))
            database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = JsonSerializer.Serialize(new WorkspaceSession { Version = 1, Tabs = [draft] }) });
        using var repository = new LiteDbConnectionProfileRepository(path);
        var migrated = await repository.LoadSessionAsync();
        Assert.That(migrated.Version, Is.EqualTo(2));
        Assert.That(migrated.Tabs.Single(), Is.EqualTo(draft));
        await repository.SaveSessionAsync(migrated);
        Assert.That((await repository.LoadSessionAsync()).Tabs.Single().Text, Is.EqualTo("rascunho"));
    }

    [TestCase(3, false)]
    [TestCase(2, true)]
    public void UnknownVersionOrInvalidEncodingCannotBeOverwritten(int version, bool invalidEncoding)
    {
        var path = Path.Combine(_directory, "protected.db");
        var raw = JsonSerializer.Serialize(new WorkspaceSession { Version = version, Tabs = [new() { Text = "keep", FileEncoding = invalidEncoding ? "future-encoding" : "Utf8" }] });
        using (var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct"))
            database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = raw });
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.CatchAsync<InvalidDataException>(() => repository.LoadSessionAsync());
            Assert.CatchAsync<InvalidDataException>(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }
        using var read = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        Assert.That(read.GetCollection("workspaceSession").FindById("current")["json"].AsString, Is.EqualTo(raw));
    }
}
