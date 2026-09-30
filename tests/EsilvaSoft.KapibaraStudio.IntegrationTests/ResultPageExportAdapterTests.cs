using System.Text.Json;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class ResultPageExportAdapterTests
{
    [Test]
    public async Task FailedOrCanceledExportRemovesPartialAndPreservesExistingDestination()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mvp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "result.json");
        var export = new LocalResultPageExportService();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            Assert.ThrowsAsync<IOException>(async () => await export.ExportAsync(path, ["{}"], false));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("original"));
            Assert.CatchAsync<JsonException>(async () => await export.ExportAsync(Path.Combine(directory, "bad.json"), ["{}", "{"], false));
            using var cancellation = new CancellationTokenSource();
            Assert.CatchAsync<OperationCanceledException>(async () => await export.ExportAsync(Path.Combine(directory, "cancel.json"), ["{}", "{}"], false,
                (_, _) => cancellation.Cancel(), cancellation.Token));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(Directory.GetFiles(directory)[0]), Is.EqualTo("result.json"));
        }
        finally { Directory.Delete(directory, true); }
    }

}
