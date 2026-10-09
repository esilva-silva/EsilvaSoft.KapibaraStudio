using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MainWindowViewModelDatabaseImportTests
{
    [Test]
    public async Task PreviewRequiresExactConfirmationAndInvalidatesOnSourcePolicyOrTargetChange()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "PreviewDatabaseImportDefinitionsAsync" => Task.FromResult(new DatabaseDefinitionImportPreview([])),
            _ => throw new NotSupportedException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target",
            ImportSourceDirectory = @"C:\exports\package"
        };

        Assert.That(viewModel.CanImportDatabase, Is.False);
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        Assert.That(viewModel.ImportPreview, Does.Contain("target"));
        viewModel.ImportConfirmation = "other";
        Assert.That(viewModel.CanImportDatabase, Is.False);
        viewModel.ImportConfirmation = "target";
        Assert.That(viewModel.CanImportDatabase, Is.True);

        viewModel.ImportUseUpsert = true;
        Assert.That(viewModel.CanImportDatabase, Is.False);
        Assert.That(viewModel.ImportPreview, Is.Empty);
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        Assert.That(viewModel.CanImportDatabase, Is.True);
        viewModel.ImportRestoreDefinitions = true;
        Assert.That(viewModel.CanImportDatabase, Is.False);
        Assert.That(viewModel.ImportPreview, Is.Empty);
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        Assert.That(viewModel.CanImportDatabase, Is.False);
        await viewModel.PreviewImportDefinitionsCommand.ExecuteAsync(null);
        Assert.That(viewModel.CanImportDatabase, Is.True);
        viewModel.ImportSourceDirectory = @"C:\exports\other";
        Assert.That(viewModel.CanImportDatabase, Is.False);

        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        viewModel.SelectedDatabase = "changed";
        Assert.That(viewModel.CanImportDatabase, Is.False);
        Assert.That(viewModel.ImportPreview, Is.Empty);
    }

    [Test]
    public async Task ImportUsesCapturedTargetAndDoesNotPublishResultAfterSelectionChanges()
    {
        using var context = new WorkspaceTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DatabaseImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        DatabaseImportRequest? capturedRequest = null;
        ConnectionProfile? capturedProfile = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method == "PreviewDatabaseImportAsync")
                return Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!));
            if (method == "PreviewDatabaseImportDefinitionsAsync")
                return Task.FromResult(new DatabaseDefinitionImportPreview([]));
            if (method != "ImportDatabaseAsync") throw new InvalidOperationException(method);
            capturedProfile = (ConnectionProfile)arguments[0]!;
            capturedRequest = (DatabaseImportRequest)arguments[1]!;
            started.SetResult();
            return release.Task;
        };
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "target",
            ImportSourceDirectory = @"C:\exports\package",
            ImportRestoreDefinitions = true
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        await viewModel.PreviewImportDefinitionsCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        var originalResults = viewModel.ImportResults;

        var import = viewModel.ImportDatabaseCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.SelectedDatabase = "other";
        release.SetResult(new DatabaseImportResult(@"C:\exports\package", "target", 1, 1));
        await import;

        Assert.Multiple(() =>
        {
            Assert.That(capturedProfile, Is.SameAs(profile));
            Assert.That(capturedRequest!.TargetDatabase, Is.EqualTo("target"));
            Assert.That(capturedRequest.DuplicatePolicy, Is.EqualTo(DatabaseImportDuplicatePolicy.Reject));
            Assert.That(capturedRequest.RestoreDefinitions, Is.True);
            Assert.That(capturedRequest.PreviewFingerprint, Is.Not.Null.And.Not.Empty);
            Assert.That(viewModel.ImportResults, Is.EqualTo(originalResults));
            Assert.That(viewModel.CanImportDatabase, Is.False);
        });
    }

    [Test]
    public async Task ExportDisablesOverlappingRunsAndDoesNotPublishToAChangedTarget()
    {
        using var context = new WorkspaceTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DatabaseExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        DatabaseExportRequest? capturedRequest = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method != "ExportDatabaseAsync") throw new InvalidOperationException(method);
            capturedRequest = (DatabaseExportRequest)arguments[1]!;
            started.SetResult();
            return release.Task;
        };
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "source"
        };
        var originalResults = viewModel.ExportResults;

        var export = viewModel.ExportDatabaseCommand.ExecuteAsync(null);
        await started.Task;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsExportInProgress, Is.True);
            Assert.That(viewModel.CanExportDatabase, Is.False);
            Assert.That(viewModel.ExportDatabaseCommand.CanExecute(null), Is.False);
            Assert.That(capturedRequest!.Database, Is.EqualTo("source"));
            Assert.That(capturedRequest.Progress, Is.Not.Null);
        });

        viewModel.SelectedDatabase = "other";
        release.SetResult(new DatabaseExportResult("export", 1, 2, false));
        await export;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ExportResults, Is.EqualTo(originalResults));
            Assert.That(viewModel.ExportProgress, Is.Empty);
            Assert.That(viewModel.IsExportInProgress, Is.False);
        });
    }

    [Test]
    public async Task DefinitionPreviewBlocksCollisionAndIgnoresLateResultAfterSourceChange()
    {
        using var context = new WorkspaceTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DatabaseDefinitionImportPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "PreviewDatabaseImportDefinitionsAsync" => BeginPreview(),
            _ => throw new InvalidOperationException(method)
        };
        Task<DatabaseDefinitionImportPreview> BeginPreview() { started.SetResult(); return release.Task; }
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target",
            ImportSourceDirectory = @"C:\exports\package",
            ImportRestoreDefinitions = true
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        var preview = viewModel.PreviewImportDefinitionsCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.ImportSourceDirectory = @"C:\exports\changed";
        release.SetResult(new DatabaseDefinitionImportPreview([
            new(DatabaseDefinitionKind.View, "sensitive_view", null, ["orders"],
                DatabaseDefinitionCollision.Conflicting, DatabaseDefinitionPlannedStatus.Blocked)]));
        await preview;
        Assert.That(viewModel.ImportDefinitionPreview, Is.Empty);
        Assert.That(viewModel.CanImportDatabase, Is.False);
    }

    [Test]
    public async Task DefinitionCollisionPreventsImportAfterExplicitReview()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "PreviewDatabaseImportDefinitionsAsync" => Task.FromResult(new DatabaseDefinitionImportPreview([
                new(DatabaseDefinitionKind.CollectionOptions, "orders", null, [],
                    DatabaseDefinitionCollision.Conflicting, DatabaseDefinitionPlannedStatus.Blocked)])),
            _ => throw new AssertionException("A importação não deve iniciar com colisão na prévia.")
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target", ImportSourceDirectory = "source", ImportRestoreDefinitions = true
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        await viewModel.PreviewImportDefinitionsCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanImportDatabase, Is.False);
            Assert.That(viewModel.ImportDefinitionPreview, Does.Contain("orders"));
        });
    }

    [Test]
    public async Task DefinitionReportShowsDependenciesAndCollisionWithoutUnsafeMessage()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "PreviewDatabaseImportDefinitionsAsync" => Task.FromResult(new DatabaseDefinitionImportPreview([
                new(DatabaseDefinitionKind.CollectionOptions, "orders", null, [],
                    DatabaseDefinitionCollision.None, DatabaseDefinitionPlannedStatus.Attempt)])),
            "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>([]),
            "ImportDatabaseAsync" => Task.FromResult(new DatabaseImportResult("source", "target", 1, 0)
            {
                DefinitionRestoreReport = new DatabaseDefinitionRestoreReport([
                    new("options:1", DatabaseDefinitionKind.CollectionOptions, "orders", [],
                        DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Restored),
                    new("index:1", DatabaseDefinitionKind.Index, "orders", ["options:1"],
                        DatabaseDefinitionCollision.Conflicting, DatabaseDefinitionRestoreStatus.Failed,
                        "DefinitionConflict", "secret://do-not-display\npassword")])
            }),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target", ImportSourceDirectory = "source", ImportRestoreDefinitions = true
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        await viewModel.PreviewImportDefinitionsCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        await viewModel.ImportDatabaseCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ImportResults, Does.Contain("orders"));
            Assert.That(viewModel.ImportResults, Does.Not.Contain("secret://"));
            Assert.That(viewModel.ImportResults, Does.Not.Contain("password"));
            Assert.That(viewModel.ImportResults, Does.Contain(" — "));
        });
    }

    [Test]
    public async Task DestinationPreviewShowsNamespaceCollisionAndBlocksImportForNonEmptyDatabase()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(new DatabaseImportPreview("target",
                DatabaseImportDuplicatePolicy.Reject, false,
                [new(DatabaseImportObjectKind.Collection, "movies", 12, true)], ["movies", "unrelated"],
                false, 12, new string('B', 64))),
            _ => throw new AssertionException("A importação não deve iniciar para um destino ocupado.")
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target", ImportSourceDirectory = "source"
        };

        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ImportPreview, Does.Contain("12"));
            Assert.That(viewModel.ImportPreview, Does.Contain("movies"));
            Assert.That(viewModel.ImportPreview, Does.Contain("unrelated"));
            Assert.That(viewModel.ImportPreview,
                Does.Contain(LocalizationViewModel.Current.Resolve("databaseImportTargetNotEmpty")));
            Assert.That(viewModel.CanImportDatabase, Is.False);
        });
    }

    [Test]
    public async Task UpsertPreviewExplainsFullReplacementAndEnablesOnlyCompatibleExistingCollections()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(new DatabaseImportPreview("target",
                DatabaseImportDuplicatePolicy.Upsert, false,
                [new(DatabaseImportObjectKind.Collection, "movies", 12, true)], ["movies"],
                false, 12, new string('C', 64), DestinationCanBeUpsertedInto: true)),
            _ => throw new AssertionException("A prévia não deve iniciar a importação.")
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target", ImportSourceDirectory = "source", ImportUseUpsert = true
        };

        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanImportDatabase, Is.True);
            Assert.That(viewModel.ImportPreview,
                Does.Contain(LocalizationViewModel.Current.Resolve("databaseImportUpsertItem")));
            Assert.That(viewModel.ImportPreview,
                Does.Contain(LocalizationViewModel.Current.Resolve("databaseImportUpsertNote")));
            Assert.That(viewModel.ImportPreview, Does.Contain("_id"));
            Assert.That(viewModel.ImportPreview, Does.Contain("não há rollback"));
        });
    }

    [Test]
    public async Task StaleDestinationResponseInvalidatesReviewedPlanAndRequiresNewPreview()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "ImportDatabaseAsync" => throw new DatabaseImportPreviewStaleException(),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            SelectedDatabase = "target", ImportSourceDirectory = "source"
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        Assert.That(viewModel.CanImportDatabase, Is.True);

        await viewModel.ImportDatabaseCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanImportDatabase, Is.False);
            Assert.That(viewModel.ImportPreview, Does.Contain("mudou"));
        });
    }

    [Test]
    public async Task PendingCheckpointRequiresExplicitVerificationBeforeRestartIsEnabled()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var checkpoint = Checkpoint(profile);
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "GetPendingImportCheckpointsAsync" => Task.FromResult<IReadOnlyList<ImportCheckpoint>>([checkpoint]),
            "InspectDatabaseImportRestartAsync" => Task.FromResult(new ImportRestartDecision(true, "restart-from-beginning")),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "target",
            ImportSourceDirectory = @"C:\private\package"
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        await viewModel.LoadImportCheckpointsCommand.ExecuteAsync(null);
        viewModel.SelectedImportCheckpoint = viewModel.PendingImportCheckpoints.Single();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanImportDatabase, Is.False);
            Assert.That(viewModel.PendingImportCheckpoints.Single().Display, Does.Not.Contain(@"C:\private\package"));
        });

        await viewModel.VerifyImportRecoveryCommand.ExecuteAsync(null);
        Assert.That(viewModel.CanImportDatabase, Is.True);
        Assert.That(viewModel.ShowPackageRestartAction, Is.True);
        viewModel.ImportSourceDirectory = @"C:\private\other";
        Assert.That(viewModel.CanImportDatabase, Is.False);
    }

    [Test]
    public async Task LateRecoveryVerificationCannotEnableChangedSource()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var checkpoint = Checkpoint(profile);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ImportRestartDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "PreviewDatabaseImportAsync" => Task.FromResult(EmptyPreview((DatabaseImportRequest)arguments[1]!)),
            "GetPendingImportCheckpointsAsync" => Task.FromResult<IReadOnlyList<ImportCheckpoint>>([checkpoint]),
            "InspectDatabaseImportRestartAsync" => BeginInspection(),
            _ => throw new InvalidOperationException(method)
        };
        Task<ImportRestartDecision> BeginInspection() { started.SetResult(); return release.Task; }
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "target",
            ImportSourceDirectory = @"C:\private\package"
        };
        await viewModel.PreviewImportDatabaseCommand.ExecuteAsync(null);
        viewModel.ImportConfirmation = "target";
        await viewModel.LoadImportCheckpointsCommand.ExecuteAsync(null);
        viewModel.SelectedImportCheckpoint = viewModel.PendingImportCheckpoints.Single();
        var verify = viewModel.VerifyImportRecoveryCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.ImportSourceDirectory = @"C:\private\changed";
        release.SetResult(new ImportRestartDecision(true, "restart-from-beginning"));
        await verify;

        Assert.That(viewModel.CanImportDatabase, Is.False);
    }

    private static ImportCheckpoint Checkpoint(ConnectionProfile profile) => new(
        ImportCheckpoint.CurrentVersion, Guid.NewGuid(), ImportCheckpointKind.LogicalPackage,
        profile.Id, profile.SourceGenerationId,
        ImportCheckpointRecovery.Sha256OfText("path"), ImportCheckpointRecovery.Sha256OfText("source"),
        ImportCheckpointRecovery.Sha256OfText("plan"), "target", null,
        1, 2, 1, 0, 0, ImportCheckpointState.NeedsReview, DateTimeOffset.UtcNow);

    private static DatabaseImportPreview EmptyPreview(DatabaseImportRequest request) => new(
        request.TargetDatabase, request.DuplicatePolicy, request.RestoreDefinitions, [], [], true, 0,
        new string('A', 64));
}
