using System.Text;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoDatabaseExportFileAccessIntegrationTests
{
    [Test]
    public async Task CreatesIsolatedExportDirectoryAndNeverOverwritesCollectionFiles()
    {
        using var synthetic = new SyntheticDirectory();
        var exportsRoot = Path.Combine(synthetic.Path, "exports");
        var files = new LocalMongoDatabaseExportFileAccess(exportsRoot);
        var exportDirectory = files.CreateExportDirectory("catalogo-run");
        var collectionPath = Path.Combine(exportDirectory, "collection-001.extended.json");

        await using (var stream = files.CreateNewFile(collectionPath))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            await writer.WriteAsync("[]");
        }

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(exportDirectory), Is.True);
            Assert.That(files.FileExists(collectionPath), Is.True);
            Assert.That(File.ReadAllText(collectionPath), Is.EqualTo("[]"));
        });
        Assert.Throws<IOException>(() => files.CreateNewFile(collectionPath));
        Assert.That(File.ReadAllText(collectionPath), Is.EqualTo("[]"));
    }

    [Test]
    public async Task CanceledManifestPublicationLeavesNoFinalOrPartialFile()
    {
        using var synthetic = new SyntheticDirectory();
        var files = new LocalMongoDatabaseExportFileAccess(Path.Combine(synthetic.Path, "exports"));
        var exportDirectory = files.CreateExportDirectory("canceled-run");
        var manifestPath = Path.Combine(exportDirectory, "manifest.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(async () => await files.WriteAllTextAsync(manifestPath, "{\"FormatVersion\":3}", cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(manifestPath), Is.False);
            Assert.That(Directory.EnumerateFiles(exportDirectory, "manifest.json.partial-*"), Is.Empty);
        });
    }

    [Test]
    public async Task ManifestPublicationDoesNotReplaceExistingPackageMarker()
    {
        using var synthetic = new SyntheticDirectory();
        var files = new LocalMongoDatabaseExportFileAccess(Path.Combine(synthetic.Path, "exports"));
        var exportDirectory = files.CreateExportDirectory("existing-run");
        var manifestPath = Path.Combine(exportDirectory, "manifest.json");
        const string original = "{\"FormatVersion\":3}";
        await files.WriteAllTextAsync(manifestPath, original);

        Assert.ThrowsAsync<IOException>(() => files.WriteAllTextAsync(manifestPath, "truncated"));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(manifestPath), Is.EqualTo(original));
            Assert.That(Directory.EnumerateFiles(exportDirectory, "manifest.json.partial-*"), Is.Empty);
        });
    }

    [Test]
    public async Task DeletesOnlyAnAllocatedExportDirectoryBeneathItsRoot()
    {
        using var synthetic = new SyntheticDirectory();
        var exportsRoot = Path.Combine(synthetic.Path, "exports");
        var files = new LocalMongoDatabaseExportFileAccess(exportsRoot);
        var exportDirectory = files.CreateExportDirectory("failed-run");
        var collectionPath = Path.Combine(exportDirectory, "collection-001.extended.json");
        var outsidePath = Path.Combine(synthetic.Path, "keep.txt");
        await File.WriteAllTextAsync(collectionPath, "partial");
        await File.WriteAllTextAsync(outsidePath, "keep");

        files.DeleteExportDirectory(exportDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(exportDirectory), Is.False);
            Assert.That(File.Exists(outsidePath), Is.True);
            Assert.Throws<ArgumentException>(() => files.DeleteExportDirectory(exportsRoot));
            Assert.Throws<ArgumentException>(() => files.DeleteExportDirectory(synthetic.Path));
        });
    }

    [Test]
    public async Task NextExportRecoversOnlyOwnedIncompleteDirectoryAfterProcessRestart()
    {
        using var synthetic = new SyntheticDirectory();
        var root = Path.Combine(synthetic.Path, "exports");
        var firstProcess = new LocalMongoDatabaseExportFileAccess(root);
        var interrupted = firstProcess.CreateExportDirectory(GeneratedExportDirectoryName());
        await File.WriteAllTextAsync(Path.Combine(interrupted, "collection-001.extended.json"), "partial");

        // A new adapter instance models process restart: its in-memory allocation registry is empty.
        var restartedProcess = new LocalMongoDatabaseExportFileAccess(root);
        var next = restartedProcess.CreateExportDirectory("next-run");

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(interrupted), Is.False);
            Assert.That(Directory.Exists(next), Is.True);
            Assert.That(Directory.EnumerateDirectories(root).Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RecoveryPreservesCommittedPackageAndRemovesOnlyStaleMarker()
    {
        using var synthetic = new SyntheticDirectory();
        var root = Path.Combine(synthetic.Path, "exports");
        var firstProcess = new LocalMongoDatabaseExportFileAccess(root);
        var completed = firstProcess.CreateExportDirectory(GeneratedExportDirectoryName());
        await firstProcess.WriteAllTextAsync(Path.Combine(completed, "manifest.json"), "{\"FormatVersion\":3}");
        // Simulate a crash after atomic manifest publication and before marking the directory committed.

        var restartedProcess = new LocalMongoDatabaseExportFileAccess(root);
        _ = restartedProcess.CreateExportDirectory("next-run");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(completed, "manifest.json")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(completed, "manifest.json")), Is.EqualTo("{\"FormatVersion\":3}"));
            Assert.That(File.Exists(Path.Combine(completed, ".kapibara-export-in-progress")), Is.False);
        });
    }

    [Test]
    public async Task RecoveryLeavesUnmarkedUserDirectoryUntouched()
    {
        using var synthetic = new SyntheticDirectory();
        var root = Path.Combine(synthetic.Path, "exports");
        Directory.CreateDirectory(root);
        var userDirectory = Path.Combine(root, GeneratedExportDirectoryName());
        Directory.CreateDirectory(userDirectory);
        var userFile = Path.Combine(userDirectory, "notes.txt");
        await File.WriteAllTextAsync(userFile, "keep");

        var restartedProcess = new LocalMongoDatabaseExportFileAccess(root);
        _ = restartedProcess.CreateExportDirectory("next-run");

        Assert.That(File.ReadAllText(userFile), Is.EqualTo("keep"));
    }

    [Test]
    public void ExportDirectoryNamesCannotEscapeOrAddNestedPaths()
    {
        using var synthetic = new SyntheticDirectory();
        var files = new LocalMongoDatabaseExportFileAccess(Path.Combine(synthetic.Path, "exports"));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => files.CreateExportDirectory(".."));
            Assert.Throws<ArgumentException>(() => files.CreateExportDirectory("nested\\outside"));
            Assert.That(Directory.Exists(synthetic.Path), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(synthetic.Path), Is.Empty);
        });
    }

    [Test]
    public async Task CleanupRemovesLinkWithoutTraversingItsExternalTarget()
    {
        using var synthetic = new SyntheticDirectory();
        var exportsRoot = Path.Combine(synthetic.Path, "exports");
        var outsideDirectory = Path.Combine(synthetic.Path, "outside");
        Directory.CreateDirectory(outsideDirectory);
        var sentinel = Path.Combine(outsideDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        var files = new LocalMongoDatabaseExportFileAccess(exportsRoot);
        var exportDirectory = files.CreateExportDirectory("linked-run");
        var link = Path.Combine(exportDirectory, "outside-link");
        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or PlatformNotSupportedException or IOException)
        {
            Assert.Ignore("O sistema não permite criar links simbólicos nesta sessão de teste.");
        }

        files.DeleteExportDirectory(exportDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(exportDirectory), Is.False);
            Assert.That(File.Exists(sentinel), Is.True);
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        });
    }

    private static string GeneratedExportDirectoryName() =>
        $"sample_mflix-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
}
