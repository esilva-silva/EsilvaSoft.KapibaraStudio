using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class TextFileAdapterTests
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

}
