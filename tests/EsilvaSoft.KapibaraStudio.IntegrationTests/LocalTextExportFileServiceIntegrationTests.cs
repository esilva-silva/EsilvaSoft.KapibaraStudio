using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalTextExportFileServiceIntegrationTests
{
    [Test]
    public async Task CreatesParentDirectoryAndWritesUtf8WithoutBom()
    {
        using var workspace = new SyntheticDirectory();
        var destination = Path.Combine(workspace.Path, "nested", "audit.json");
        const string content = "{\"cidade\":\"São Paulo\"}";
        var service = new LocalTextExportFileService();

        await service.WriteNewAsync(destination, content);

        var bytes = await File.ReadAllBytesAsync(destination);
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.GetDirectoryName(destination)), Is.True);
            Assert.That(bytes, Is.EqualTo(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content)));
        });
    }

    [Test]
    public async Task ExistingDestinationIsReportedAndPreserved()
    {
        using var workspace = new SyntheticDirectory();
        var destination = Path.Combine(workspace.Path, "nested", "query.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        const string original = "original evidence";
        await File.WriteAllTextAsync(destination, original);
        var service = new LocalTextExportFileService();

        var exception = Assert.ThrowsAsync<TextExportFileAlreadyExistsException>(
            async () => await service.WriteNewAsync(destination, "replacement"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.DestinationPath, Is.EqualTo(Path.GetFullPath(destination)));
            Assert.That(File.ReadAllText(destination), Is.EqualTo(original));
        });
    }
}
