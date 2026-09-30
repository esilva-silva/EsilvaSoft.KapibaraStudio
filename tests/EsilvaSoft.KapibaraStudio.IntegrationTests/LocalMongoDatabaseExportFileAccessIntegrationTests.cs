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
}
