using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class LocalScriptFileServiceTests
{
    [Test]
    public async Task SaveAndLoadRoundTripPreservesScript()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"slopdataadmin-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "consulta.js");
        const string script = "const resultado = await db.clientes.find({ ativo: true }).toArray();";

        try
        {
            var service = new LocalScriptFileService();
            await service.SaveAsync(path, script);

            Assert.That(await service.LoadAsync(path), Is.EqualTo(script));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task SaveWithTextExtensionAndEmptyContentWorks()
    {
        var service = new LocalScriptFileService();
        var path = Path.Combine(Path.GetTempPath(), $"consulta-{Guid.NewGuid():N}.txt");

        try
        {
            await service.SaveAsync(path, string.Empty);
            Assert.That(await service.LoadAsync(path), Is.Empty);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
