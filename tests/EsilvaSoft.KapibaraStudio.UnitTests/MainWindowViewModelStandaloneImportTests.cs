using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MainWindowViewModelStandaloneImportTests
{
    [Test]
    public async Task PreviewCapturesMappingAndInvalidatesWhenTargetChanges()
    {
        using var context = new WorkspaceTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TransferDocumentPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, _) =>
        {
            if (method != "PreviewStandaloneImportAsync") throw new InvalidOperationException(method);
            started.SetResult();
            return release.Task;
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target",
            StandaloneSourceFile = @"C:\exports\items.csv",
            StandaloneTargetCollection = "items",
            StandaloneUseCsv = true,
            StandaloneMappingJson = """
                [{"SourceColumn":"id","TargetField":"_id","Type":"Integer64"}]
                """
        };

        var preview = viewModel.PreviewStandaloneImportCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.StandaloneTargetCollection = "other";
        release.SetResult(new TransferDocumentPreview([new TransferPreviewField("_id", "Integer64", "id")], 1, false));
        await preview;

        Assert.That(viewModel.StandalonePreviewText, Is.Empty);
        Assert.That(viewModel.CanImportStandalone, Is.False);
    }

    [Test]
    public async Task JsonArrayPreviewCapturesFormatAndCsvSelectionRemainsExclusive()
    {
        using var context = new WorkspaceTestContext();
        TransferImportSchema? capturedSchema = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method != "PreviewStandaloneImportAsync") throw new InvalidOperationException(method);
            capturedSchema = (TransferImportSchema)arguments[1]!;
            return Task.FromResult(new TransferDocumentPreview([new TransferPreviewField("_id", "Int32")], 1, false));
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target",
            StandaloneSourceFile = @"C:\exports\items.json",
            StandaloneTargetCollection = "items",
            StandaloneUseJsonArray = true
        };

        await viewModel.PreviewStandaloneImportCommand.ExecuteAsync(null);
        viewModel.StandaloneUseCsv = true;

        Assert.Multiple(() =>
        {
            Assert.That(capturedSchema!.Format, Is.EqualTo(TransferImportFormat.JsonArray));
            Assert.That(viewModel.StandalonePreviewText, Is.Empty, "changing format invalidates the captured preview");
            Assert.That(viewModel.StandaloneUseJsonArray, Is.False);
        });
    }

    [Test]
    public async Task ConfirmedStandaloneImportUsesCapturedRequestAndIgnoresStaleResult()
    {
        using var context = new WorkspaceTestContext();
        var release = new TaskCompletionSource<StandaloneImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StandaloneImportRequest? captured = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method == "PreviewStandaloneImportAsync")
                return Task.FromResult(new TransferDocumentPreview([new TransferPreviewField("_id", "ObjectId")], 1, false));
            if (method != "ImportStandaloneAsync") throw new InvalidOperationException(method);
            captured = (StandaloneImportRequest)arguments[1]!;
            started.SetResult();
            return release.Task;
        };
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "target",
            StandaloneSourceFile = @"C:\exports\items.ndjson",
            StandaloneTargetCollection = "items"
        };
        await viewModel.PreviewStandaloneImportCommand.ExecuteAsync(null);
        viewModel.StandaloneConfirmation = "target";
        Assert.That(viewModel.CanImportStandalone, Is.True);

        var import = viewModel.ImportStandaloneCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.SelectedDatabase = "other";
        release.SetResult(new StandaloneImportResult(@"C:\exports\items.ndjson", "target", "items", 1));
        await import;

        Assert.Multiple(() =>
        {
            Assert.That(captured!.TargetDatabase, Is.EqualTo("target"));
            Assert.That(captured.TargetCollection, Is.EqualTo("items"));
            Assert.That(captured.DuplicatePolicy, Is.EqualTo(DatabaseImportDuplicatePolicy.Reject));
            Assert.That(viewModel.StandaloneImportResults, Is.Empty);
            Assert.That(viewModel.CanImportStandalone, Is.False);
        });
    }
}
