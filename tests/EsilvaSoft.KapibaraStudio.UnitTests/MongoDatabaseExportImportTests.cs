using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoDatabaseExportImportTests
{
    private static readonly string[] ExpectedSystemNamespacePatterns = ["system.*"];
    private static readonly int[] ExpectedTwoSingleDocumentBatches = [1, 1];
    private static readonly string[] ExpectedCollisionCleanup = ["create:items", "drop:items"];
    private static readonly string[] ExpectedStandaloneImport = ["create:items", "write:items"];
    private static readonly string[] ExpectedStandaloneMutationCleanup = ["create:items", "write:items", "drop:items"];
    private static readonly string[] ExpectedUncertainCreate = ["create:items"];
    private static readonly string[] CompletedCheckpointEvents = ["create", "update", "complete"];
    private static readonly TransferImportSchema StandaloneNdjsonSchema = new(TransferImportFormat.Ndjson);
    private static readonly TransferImportSchema StandaloneCsvSchema = new(TransferImportFormat.Csv,
        [new("id", "_id", TransferFieldType.Integer64), new("name", "name", TransferFieldType.Text)]);

    [Test]
    public async Task DatabaseImportPreviewShowsManifestCountsAndNamespaceCollisionWithoutReadingTargetDocuments()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        target.FindCalls.Clear();

        var emptyPreview = await service.PreviewDatabaseImportAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target"));
        target.Collections.Add("movies");
        var collisionPreview = await service.PreviewDatabaseImportAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(emptyPreview.CanImport, Is.True);
            Assert.That(emptyPreview.DestinationIsEmpty, Is.True);
            Assert.That(emptyPreview.TotalDocuments, Is.EqualTo(1));
            Assert.That(emptyPreview.Items.Single(item => item.Kind == DatabaseImportObjectKind.Collection).DocumentCount,
                Is.EqualTo(1));
            Assert.That(emptyPreview.Items.Single(item => item.Kind == DatabaseImportObjectKind.View).DocumentCount,
                Is.Zero);
            Assert.That(collisionPreview.CanImport, Is.False);
            Assert.That(collisionPreview.DestinationIsEmpty, Is.False);
            Assert.That(collisionPreview.Items.Single(item => item.Namespace == "movies").DestinationNamespaceExists,
                Is.True);
            Assert.That(target.FindCalls, Is.Empty, "The preview may inspect namespace names but never target documents.");
            Assert.That(target.Operations, Is.Empty, "A destination preview is read-only.");
        });
    }

    [Test]
    public async Task UpsertPreviewAllowsOnlyManifestCollectionsAndNeverReadsTargetDocuments()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        target.Collections.Add("movies");
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));

        var upsertPreview = await service.PreviewDatabaseImportAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target"));
        var rejectPreview = await service.PreviewDatabaseImportAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(upsertPreview.DestinationIsEmpty, Is.False);
            Assert.That(upsertPreview.DestinationCanBeUpsertedInto, Is.True);
            Assert.That(upsertPreview.CanImport, Is.True);
            Assert.That(rejectPreview.CanImport, Is.False);
            Assert.That(target.FindCalls, Is.Empty, "Preview must not read destination documents.");
            Assert.That(target.Operations, Is.Empty, "Preview is read-only.");
        });
    }

    [TestCase(false, "extra")]
    [TestCase(true, "movies")]
    public async Task UpsertPreviewBlocksViewsAndNamespacesOutsidePackage(bool destinationIsView, string name)
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        target.Collections.Add(name);
        if (destinationIsView)
            target.Definitions.Add(new BsonDocument("name", name).Add("type", "view"));
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));

        var preview = await service.PreviewDatabaseImportAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(preview.DestinationCanBeUpsertedInto, Is.False);
            Assert.That(preview.CanImport, Is.False);
            Assert.That(target.FindCalls, Is.Empty);
            Assert.That(target.Operations, Is.Empty);
        });
    }

    [Test]
    public async Task UpsertReusesExistingManifestCollectionAndOnlyCreatesNewNamespaces()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        var source = DatabaseState.WithSourceData();
        source.Collections.Add("shows");
        source.Documents["movies"] = [new BsonDocument("_id", 1).Add("title", "Imported Movie")];
        source.Documents["shows"] = [new BsonDocument("_id", "s1").Add("title", "Show")];
        source.Definitions.Add(new BsonDocument("name", "shows").Add("type", "collection"));
        driver.Databases["source"] = source;
        var target = new DatabaseState();
        target.Collections.Add("movies");
        target.Documents["movies"] = [new BsonDocument("_id", 1).Add("old", true)];
        target.Definitions.Add(new BsonDocument("name", "movies").Add("type", "collection"));
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var request = new DatabaseImportRequest(export.OutputDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target");
        var preview = await service.PreviewDatabaseImportAsync(profile, request);

        var result = await service.ImportDatabaseAsync(profile, request with { PreviewFingerprint = preview.Fingerprint });

        Assert.Multiple(() =>
        {
            Assert.That(result.DocumentCount, Is.EqualTo(2));
            Assert.That(target.Collections, Does.Contain("movies"));
            Assert.That(target.Collections, Does.Contain("shows"));
            Assert.That(target.Operations, Does.Not.Contain("create:movies"));
            Assert.That(target.Operations, Does.Contain("create:shows"));
            Assert.That(target.Operations, Does.Contain("write:movies"));
            Assert.That(target.Operations, Does.Contain("write:shows"));
            Assert.That(target.WriteModelTypes, Has.All.StartsWith("ReplaceOneModel"));
            Assert.That(target.UpsertFlags, Has.All.True);
            Assert.That(target.Documents["movies"], Has.Count.EqualTo(1));
            Assert.That(target.Documents["movies"][0].Names, Does.Not.Contain("old"));
            Assert.That(target.Documents["movies"][0]["title"].AsString, Is.EqualTo("Imported Movie"));
        });
    }

    [Test]
    public async Task UpsertFailureNeverDropsExistingCollectionAndReportsPartialOutcome()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        var source = DatabaseState.WithSourceData();
        source.Collections.Add("shows");
        source.Documents["shows"] = [new BsonDocument("_id", "s1").Add("title", "Show")];
        source.Definitions.Add(new BsonDocument("name", "shows").Add("type", "collection"));
        driver.Databases["source"] = source;
        var target = new DatabaseState { FailBulkWriteCollection = "movies" };
        target.Collections.Add("movies");
        target.Definitions.Add(new BsonDocument("name", "movies").Add("type", "collection"));
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var request = new DatabaseImportRequest(export.OutputDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target");
        var preview = await service.PreviewDatabaseImportAsync(profile, request);
        var progress = new List<DatabaseImportProgress>();

        var failure = Assert.ThrowsAsync<AggregateException>(() => service.ImportDatabaseAsync(profile,
            request with { PreviewFingerprint = preview.Fingerprint, Progress = new ImmediateProgress(progress.Add) }));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("coleção preexistente"));
            Assert.That(target.Collections, Does.Contain("movies"));
            Assert.That(target.Collections, Does.Not.Contain("shows"));
            Assert.That(target.Operations, Does.Not.Contain("drop:movies"));
            Assert.That(target.Operations, Does.Contain("drop:shows"));
            Assert.That(progress.Last(item => item.Stage == DatabaseImportStage.Failed).OutcomeMayBePartial, Is.True);
        });
    }

    [Test]
    public async Task UpsertIntoExistingCollectionBlocksDefinitionsAndCheckpointRestart()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        target.Collections.Add("movies");
        target.Definitions.Add(new BsonDocument("name", "movies").Add("type", "collection"));
        driver.Databases["target"] = target;
        var checkpoints = new FakeImportCheckpointRepository();
        var service = CreateService(files, driver, checkpoints);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var request = new DatabaseImportRequest(export.OutputDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target");
        var preview = await service.PreviewDatabaseImportAsync(profile, request);
        var definitionsPreview = await service.PreviewDatabaseImportAsync(profile, request with { RestoreDefinitions = true });

        var definitions = Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportDatabaseAsync(profile,
            request with { RestoreDefinitions = true, PreviewFingerprint = null }));
        var checkpointRestart = Assert.ThrowsAsync<NotSupportedException>(() => service.ImportDatabaseAsync(profile,
            request with { RestartCheckpointId = Guid.NewGuid(), PreviewFingerprint = null }));

        Assert.Multiple(() =>
        {
            Assert.That(preview.CanImport, Is.True);
            Assert.That(definitionsPreview.CanImport, Is.False);
            Assert.That(definitionsPreview.DestinationCanBeUpsertedInto, Is.False);
            Assert.That(definitions!.Message, Does.Contain("definições"));
            Assert.That(checkpointRestart!.Message, Does.Contain("checkpoint"));
            Assert.That(checkpoints.Events, Is.Empty);
            Assert.That(target.Operations, Is.Empty);
        });
    }

    [Test]
    public async Task ImportRejectsPreviewWhenDestinationNamespaceSetChangesBeforeDispatch()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var request = new DatabaseImportRequest(export.OutputDirectory, "target");
        var preview = await service.PreviewDatabaseImportAsync(profile, request);
        target.Collections.Add("arrived_after_preview");

        var exception = Assert.ThrowsAsync<DatabaseImportPreviewStaleException>(() => service.ImportDatabaseAsync(profile,
            request with { PreviewFingerprint = preview.Fingerprint }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("desatualizada"));
            Assert.That(target.Operations, Is.Empty);
            Assert.That(target.Collections, Is.EquivalentTo(["arrived_after_preview"]));
            Assert.That(target.FindCalls, Is.Empty);
        });
    }

    [Test]
    public async Task ImportRejectsPreviewWhenProfileGenerationOrPolicyChanges()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var target = new DatabaseState();
        driver.Databases["target"] = target;
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var rejectRequest = new DatabaseImportRequest(export.OutputDirectory, "target");
        var preview = await service.PreviewDatabaseImportAsync(profile, rejectRequest);
        var changedProfile = profile with { SourceGenerationId = Guid.NewGuid() };

        var generationFailure = Assert.ThrowsAsync<DatabaseImportPreviewStaleException>(() => service.ImportDatabaseAsync(changedProfile,
            rejectRequest with { PreviewFingerprint = preview.Fingerprint }));
        var policyFailure = Assert.ThrowsAsync<DatabaseImportPreviewStaleException>(() => service.ImportDatabaseAsync(profile,
            rejectRequest with
            {
                DuplicatePolicy = DatabaseImportDuplicatePolicy.Upsert,
                ConfirmationDatabase = "target",
                PreviewFingerprint = preview.Fingerprint
            }));

        Assert.Multiple(() =>
        {
            Assert.That(generationFailure!.Message, Does.Contain("desatualizada"));
            Assert.That(policyFailure!.Message, Does.Contain("desatualizada"));
            Assert.That(target.Operations, Is.Empty);
            Assert.That(target.Collections, Is.Empty);
        });
    }

    [Test]
    public async Task ExportAndImportRoundTripSeparatesViewsAndCreatesThemAfterCollections()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        using var manifest = JsonDocument.Parse(files.Text[Path.Combine(export.OutputDirectory, "manifest.json")]);
        Assert.That(manifest.RootElement.GetProperty("FormatVersion").GetInt32(), Is.EqualTo(3));
        Assert.That(manifest.RootElement.GetProperty("CreatedAtUtc").GetDateTimeOffset().Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Version").GetInt32(), Is.EqualTo(2));
        Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Producer").GetString(), Is.EqualTo("EsilvaSoft.KapibaraStudio"));
        Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("SourceKind").GetString(), Is.EqualTo("MongoDB"));
        Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Omissions").GetProperty("NamespacePatterns").EnumerateArray().Select(item => item.GetString()),
            Is.EqualTo(ExpectedSystemNamespacePatterns));
        Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("Omissions").GetProperty("NamespaceCount").GetInt32(), Is.EqualTo(1));
        Assert.That(manifest.RootElement.GetProperty("Collections")[0].GetProperty("Sha256").GetString(), Has.Length.EqualTo(64));
        Assert.That(manifest.RootElement.GetProperty("Collections").EnumerateArray().Select(item => item.GetProperty("Name").GetString()),
            Is.EqualTo(new List<string> { "movies" }));
        Assert.That(manifest.RootElement.GetProperty("Views").EnumerateArray().Select(item => item.GetProperty("Name").GetString()),
            Is.EqualTo(new List<string> { "active_movies" }));
        Assert.That(driver.Databases["source"].FindCalls, Is.EqualTo(new List<string> { "movies" }), "system.* and view namespaces must not be exported as collections.");

        var imported = await service.ImportDatabaseAsync(profile, new DatabaseImportRequest(export.OutputDirectory, "target"));

        Assert.That(imported.CollectionCount, Is.EqualTo(2));
        Assert.That(imported.DocumentCount, Is.EqualTo(1));
        Assert.That(driver.Databases["target"].Collections, Is.EquivalentTo(new List<string> { "movies", "active_movies" }));
        Assert.That(driver.Databases["target"].Collections, Does.Not.Contain("system.views"));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:movies", "write:movies", "view:active_movies" }));
        Assert.That(driver.Databases["target"].WriteModelTypes, Has.All.StartsWith("InsertOneModel"));
        Assert.That(driver.Databases["target"].Views.Single().ViewOn, Is.EqualTo("movies"));
        var restoredView = driver.Databases["target"].Views.Single();
        var restoredPipeline = BsonSerializer.Deserialize<BsonArray>(restoredView.PipelineJson);
        Assert.That(restoredPipeline.Single().AsBsonDocument["$match"].AsBsonDocument["active"].AsBoolean, Is.True);
        Assert.That(restoredView.Collation?.Locale, Is.EqualTo("en"));
    }

    [Test]
    public async Task ExportReportsCollectionAndDocumentProgressAndCompletesAfterManifestPublication()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var progress = new RecordingProgress<DatabaseExportProgress>();

        var result = await service.ExportDatabaseAsync(ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseExportRequest("source") { Progress = progress });

        Assert.Multiple(() =>
        {
            Assert.That(progress.Values, Has.Count.EqualTo(3));
            Assert.That(progress.Values[0].Stage, Is.EqualTo(DatabaseExportStage.Exporting));
            Assert.That(progress.Values[0].TotalCollections, Is.EqualTo(1));
            Assert.That(progress.Values[1].CompletedCollections, Is.EqualTo(1));
            Assert.That(progress.Values[1].TotalDocuments, Is.EqualTo(1));
            Assert.That(progress.Values[^1].Stage, Is.EqualTo(DatabaseExportStage.Completed));
            Assert.That(progress.Values[^1].TotalDocuments, Is.EqualTo(result.DocumentCount));
            Assert.That(files.Text.ContainsKey(Path.Combine(result.OutputDirectory, "manifest.json")), Is.True,
                "Completion is reported only after the final manifest has been published.");
        });
    }

    [Test]
    public void FailedExportRemovesPartialFilesAndDoesNotPublishCompletion()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        var source = DatabaseState.WithSourceData();
        source.FailExportRead = true;
        driver.Databases["source"] = source;
        var service = CreateService(files, driver);
        var progress = new RecordingProgress<DatabaseExportProgress>();

        Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseExportRequest("source") { Progress = progress }));

        Assert.Multiple(() =>
        {
            Assert.That(files.ExportDirectories, Is.Empty);
            Assert.That(files.Text, Is.Empty);
            Assert.That(progress.Values.Any(value => value.Stage == DatabaseExportStage.Completed), Is.False);
        });
    }

    [Test]
    public void FailedExportReportsWhenPartialFileCleanupAlsoFails()
    {
        var files = new MemoryExportFiles { FailExportCleanup = true };
        var driver = new FakeMongoDriver();
        var source = DatabaseState.WithSourceData();
        source.FailExportRead = true;
        driver.Databases["source"] = source;
        var service = CreateService(files, driver);

        var failure = Assert.ThrowsAsync<IOException>(() => service.ExportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseExportRequest("source")));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("arquivos parciais"));
            Assert.That(failure.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(files.ExportDirectories, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ThrowingCompletionObserverDoesNotDeletePublishedExport()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var observer = new ThrowingProgress<DatabaseExportProgress>(value =>
        {
            if (value.Stage == DatabaseExportStage.Completed)
                throw new InvalidOperationException("Synthetic observer failure.");
        });

        Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseExportRequest("source") { Progress = observer }));

        Assert.Multiple(() =>
        {
            Assert.That(files.ExportDirectories, Has.Count.EqualTo(1));
            Assert.That(files.Text.Keys.Any(path => path.EndsWith(Path.Combine("manifest.json"), StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task OptInRestoreReportsIdenticalIdIndexAndRestoredView()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        driver.ClientRequests = 0;

        var result = await service.ImportDatabaseAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target", ConfirmationDatabase: "target", RestoreDefinitions: true));

        Assert.Multiple(() =>
        {
            Assert.That(driver.ClientRequests, Is.GreaterThan(0));
            Assert.That(result.DefinitionRestoreReport, Is.Not.Null);
            Assert.That(result.DefinitionRestoreReport!.Items.Single(item => item.Kind == DatabaseDefinitionKind.Index).Status,
                Is.EqualTo(DatabaseDefinitionRestoreStatus.Omitted));
            Assert.That(result.DefinitionRestoreReport.Items.Single(item => item.Kind == DatabaseDefinitionKind.Index).Collision,
                Is.EqualTo(DatabaseDefinitionCollision.Identical));
            Assert.That(result.DefinitionRestoreReport.Items.Single(item => item.Kind == DatabaseDefinitionKind.View).Status,
                Is.EqualTo(DatabaseDefinitionRestoreStatus.Restored));
        });
    }

    [Test]
    public async Task OptInRestoreRejectsUnsupportedDefinitionsBeforeMongoAccess()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifestPath = Path.Combine(export.OutputDirectory, "manifest.json");
        var manifest = JsonNode.Parse(files.Text[manifestPath])!;
        manifest["Definitions"]!["Collections"]![0]!["OptionsJson"] = "{\"encryptedFields\":{}}";
        files.Text[manifestPath] = manifest.ToJsonString();
        driver.ClientRequests = 0;

        var failure = Assert.ThrowsAsync<DatabaseDefinitionRestoreException>(() => service.ImportDatabaseAsync(profile,
            new DatabaseImportRequest(export.OutputDirectory, "target", ConfirmationDatabase: "target", RestoreDefinitions: true)));

        Assert.Multiple(() =>
        {
            Assert.That(driver.ClientRequests, Is.Zero);
            Assert.That(failure!.OutcomeMayBePartial, Is.False);
            Assert.That(failure.Message, Does.Not.Contain("encryptedFields"));
            Assert.That(failure.Report.Items.Any(item => item.Status == DatabaseDefinitionRestoreStatus.Failed), Is.True);
        });
    }

    [Test]
    public async Task ExportMetadataOmitsConnectionAndProfileSecrets()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Profile private-marker", "mongodb://user:manifest-secret-marker@private-host.example:27017/source");

        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifestText = files.Text[Path.Combine(export.OutputDirectory, "manifest.json")];
        using var manifest = JsonDocument.Parse(manifestText);

        Assert.Multiple(() =>
        {
            Assert.That(manifestText, Does.Not.Contain("manifest-secret-marker"));
            Assert.That(manifestText, Does.Not.Contain("private-host.example"));
            Assert.That(manifestText, Does.Not.Contain("Profile private-marker"));
            Assert.That(manifest.RootElement.GetProperty("Database").GetString(), Is.EqualTo("source"));
            Assert.That(manifest.RootElement.GetProperty("Metadata").GetProperty("SourceKind").GetString(), Is.EqualTo("MongoDB"));
        });
    }

    [Test]
    public async Task ImportReadsVersionThreeManifestWithoutOptionalMetadata()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifestPath = Path.Combine(export.OutputDirectory, "manifest.json");
        var manifest = JsonNode.Parse(files.Text[manifestPath])!.AsObject();
        manifest.Remove("Metadata");
        files.Text[manifestPath] = manifest.ToJsonString();
        driver.Databases["target"] = new DatabaseState();

        var imported = await service.ImportDatabaseAsync(profile, new DatabaseImportRequest(export.OutputDirectory, "target"));

        Assert.That(imported.CollectionCount, Is.EqualTo(2));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:movies", "write:movies", "view:active_movies" }));
    }

    [Test]
    public async Task ImportRejectsModifiedVersionThreeCollectionFileBeforeMongoAccess()
    {
        var files = new MemoryExportFiles();
        var driver = new FakeMongoDriver();
        driver.Databases["source"] = DatabaseState.WithSourceData();
        var service = CreateService(files, driver);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var export = await service.ExportDatabaseAsync(profile, new DatabaseExportRequest("source"));
        var manifest = JsonDocument.Parse(files.Text[Path.Combine(export.OutputDirectory, "manifest.json")]);
        var collectionFile = Path.Combine(export.OutputDirectory, manifest.RootElement.GetProperty("Collections")[0].GetProperty("File").GetString()!);
        files.Text[collectionFile] = files.Text[collectionFile].Replace("Movie", "Other", StringComparison.Ordinal);
        driver.ClientRequests = 0;

        var failure = Assert.ThrowsAsync<ArgumentException>(() => service.ImportDatabaseAsync(
            profile,
            new DatabaseImportRequest(export.OutputDirectory, "target")));

        Assert.That(failure!.Message, Does.Contain("SHA-256"));
        Assert.That(driver.ClientRequests, Is.Zero);
        Assert.That(driver.Databases.Values.SelectMany(database => database.Operations), Is.Empty);
        Assert.That(files.CollectionStreamOpenCount, Is.EqualTo(1), "Package preflight uses the streaming port.");
    }

    [Test]
    public async Task ImportStreamsLegacyFilesAndPreservesExtendedBsonTypes()
    {
        var files = new MemoryExportFiles();
        var sourceDirectory = files.CreateLegacyDirectory("typed-v1");
        var objectId = "66e3bd5b3bc3f54c840d73ac";
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] =
            "[{\"_id\":{\"$oid\":\"" + objectId + "\"},\"when\":{\"$date\":\"2026-10-08T12:00:00Z\"}}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 1);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);

        var result = await service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target"));
        var restored = driver.Databases["target"].WrittenBsonDocuments["items"].Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.DocumentCount, Is.EqualTo(1));
            Assert.That(restored["_id"].AsObjectId.ToString(), Is.EqualTo(objectId));
            Assert.That(restored["when"].BsonType, Is.EqualTo(BsonType.DateTime));
            Assert.That(files.CollectionStreamOpenCount, Is.EqualTo(2), "The legacy file is parsed in bounded preflight and write passes.");
            Assert.That(files.CollectionReadAllCount, Is.Zero);
            Assert.That(driver.Databases["target"].WriteModelTypes, Has.All.StartsWith("InsertOneModel"));
        });
    }

    [Test]
    public void ImportRejectsDuplicateIdsInSourceBeforeMongoAccess()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] = "[{\"_id\":1},{\"_id\":1}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 2);
        var driver = new FakeMongoDriver();
        var service = CreateService(files, driver);

        var error = Assert.ThrowsAsync<ArgumentException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(error!.Message, Does.Contain("_id duplicado"));
        Assert.That(driver.ClientRequests, Is.Zero);
    }

    [Test]
    public async Task StandaloneCsvPreviewShowsMappingWithoutMongoAccess()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-preview");
        var source = Path.Combine(directory, "items.csv");
        files.Text[source] = "id,name\n1,one\n";
        var driver = new FakeMongoDriver();

        var preview = await CreateService(files, driver).PreviewStandaloneImportAsync(source, StandaloneCsvSchema);

        Assert.Multiple(() =>
        {
            Assert.That(preview.SampledRows, Is.EqualTo(1));
            Assert.That(preview.Fields.Single(field => field.Name == "_id").Type, Is.EqualTo("Integer64"));
            Assert.That(driver.ClientRequests, Is.Zero);
        });
    }

    [Test]
    public void StandaloneRejectDetectsSourceDuplicateBeforeMongoAccess()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-duplicate");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n{\"_id\":{\"$numberLong\":\"1\"}}\n";
        var driver = new FakeMongoDriver();

        var error = Assert.ThrowsAsync<TransferRowException>(() => CreateService(files, driver).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target")));

        Assert.That(error!.Code, Is.EqualTo("duplicate-id"));
        Assert.That(driver.ClientRequests, Is.Zero);
    }

    [Test]
    public async Task StandaloneNdjsonImportsTypedDocumentsIntoNewCollection()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-typed");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] =
            "{\"_id\":{\"$oid\":\"66e3bd5b3bc3f54c840d73ac\"},\"uuid\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}}}\n";
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();

        var result = await CreateService(files, driver).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(result.DocumentCount, Is.EqualTo(1));
            Assert.That(driver.Databases["target"].Operations, Is.EqualTo(ExpectedStandaloneImport));
            Assert.That(driver.Databases["target"].WrittenBsonDocuments["items"].Single()["uuid"].AsBsonBinaryData.SubType,
                Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(driver.Databases["target"].WriteModelTypes, Has.All.StartsWith("InsertOneModel"));
        });
    }

    [Test]
    public void StandaloneInvalidCsvFieldFailsBeforeMongoAccess()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-invalid");
        var source = Path.Combine(directory, "items.csv");
        files.Text[source] = "id,name\nbad,private-marker\n";
        var driver = new FakeMongoDriver();

        var error = Assert.ThrowsAsync<TransferRowException>(() => CreateService(files, driver).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneCsvSchema,
                DatabaseImportDuplicatePolicy.Reject, "target")));

        Assert.That(error!.Line, Is.EqualTo(2));
        Assert.That(error.Field, Is.EqualTo("id"));
        Assert.That(error.Message, Does.Not.Contain("private-marker"));
        Assert.That(driver.ClientRequests, Is.Zero);
    }

    [Test]
    public void StandaloneChangeBetweenPassesCleansCreatedCollection()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-mutated");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        files.OnOpenRead = (path, number) =>
        {
            if (path == source && number == 2) files.Text[source] = "{\"_id\":2}\n";
        };
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();

        Assert.ThrowsAsync<InvalidDataException>(() => CreateService(files, driver).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target")));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(ExpectedStandaloneMutationCleanup));
    }

    [Test]
    public void StandaloneUncertainCreateDoesNotDropUnknownCollectionAndReportsPartialOutcome()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-create-uncertain");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { FailCreateAfterAdding = true };
        var checkpoints = new FakeImportCheckpointRepository();
        var progress = new List<DatabaseImportProgress>();
        var request = new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
            DatabaseImportDuplicatePolicy.Reject, "target") { Progress = new ImmediateProgress(progress.Add) };

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(files, driver, checkpoints).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"), request));

        Assert.That(error!.Message, Does.Contain("incerto"));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(ExpectedUncertainCreate));
        Assert.That(progress.Last().OutcomeMayBePartial, Is.True);
        Assert.That(checkpoints.Pending.Single().State, Is.EqualTo(ImportCheckpointState.NeedsReview));
    }

    [Test]
    public async Task StandaloneImportPersistsBoundCheckpointBeforeWriteAndCompletesItAfterWrites()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-checkpoint");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var checkpoints = new FakeImportCheckpointRepository();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        await CreateService(files, driver, checkpoints).ImportStandaloneAsync(profile,
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(checkpoints.Events, Is.EqualTo(CompletedCheckpointEvents));
            Assert.That(checkpoints.Created!.ProfileId, Is.EqualTo(profile.Id));
            Assert.That(checkpoints.Created.SourceSha256, Has.Length.EqualTo(64));
            Assert.That(checkpoints.Created.PlanSha256, Has.Length.EqualTo(64));
            Assert.That(checkpoints.Created!.ToString(), Does.Not.Contain(source));
            Assert.That(checkpoints.Updates.Single().ProcessedDocuments, Is.EqualTo(1));
            Assert.That(checkpoints.Pending, Is.Empty);
        });
    }

    [Test]
    public void StandaloneCheckpointFailurePreventsAnyMongoWrite()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-checkpoint-fail");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var checkpoints = new FakeImportCheckpointRepository { FailCreate = true };

        Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(files, driver, checkpoints).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target")));
        Assert.That(driver.Databases["target"].Operations, Is.Empty);
        Assert.That(checkpoints.Pending, Is.Empty);
    }

    [Test]
    public async Task StandaloneJsonArrayRequiresJsonExtension()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-json-extension");
        var jsonPath = Path.Combine(directory, "items.json");
        var ndjsonPath = Path.Combine(directory, "items.ndjson");
        files.Text[jsonPath] = "[{\"_id\":1}]";
        files.Text[ndjsonPath] = "[{\"_id\":1}]";
        var service = CreateService(files, new FakeMongoDriver());

        var preview = await service.PreviewStandaloneImportAsync(jsonPath,
            new TransferImportSchema(TransferImportFormat.JsonArray));

        Assert.That(preview.SampledRows, Is.EqualTo(1));
        Assert.ThrowsAsync<ArgumentException>(() => service.PreviewStandaloneImportAsync(ndjsonPath,
            new TransferImportSchema(TransferImportFormat.JsonArray)));
    }

    [Test]
    public void StandaloneOccupiedDestinationDoesNotLeavePreparedCheckpoint()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-occupied-checkpoint");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { Collections = { "already_exists" } };
        var checkpoints = new FakeImportCheckpointRepository();

        Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(files, driver, checkpoints).ImportStandaloneAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
                DatabaseImportDuplicatePolicy.Reject, "target")));

        Assert.That(checkpoints.Events, Is.Empty);
        Assert.That(driver.Databases["target"].Operations, Is.Empty);
    }

    [Test]
    public async Task StandaloneRestartRequiresEmptyTargetAndResetsOriginalReceipt()
    {
        var files = new MemoryExportFiles();
        var directory = files.CreateLegacyDirectory("standalone-restart");
        var source = Path.Combine(directory, "items.ndjson");
        files.Text[source] = "{\"_id\":1}\n";
        var driver = new FakeMongoDriver();
        var target = new DatabaseState { FailCreateAfterAdding = true };
        driver.Databases["target"] = target;
        var checkpoints = new FakeImportCheckpointRepository();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var request = new StandaloneImportRequest(source, "target", "items", StandaloneNdjsonSchema,
            DatabaseImportDuplicatePolicy.Reject, "target");
        var service = CreateService(files, driver, checkpoints);
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportStandaloneAsync(profile, request));
        var checkpointId = checkpoints.Pending.Single().Id;

        var occupied = await service.InspectStandaloneImportRestartAsync(profile, request, checkpointId);
        target.Collections.Remove("items");
        var eligible = await service.InspectStandaloneImportRestartAsync(profile, request, checkpointId);
        target.FailCreateAfterAdding = false;
        await service.ImportStandaloneAsync(profile, request with { RestartCheckpointId = checkpointId });

        Assert.Multiple(() =>
        {
            Assert.That(occupied.ReasonCode, Is.EqualTo("target-not-empty"));
            Assert.That(eligible.CanRestartFromBeginning, Is.True);
            Assert.That(checkpoints.Events.Count(item => item == "reset"), Is.EqualTo(1));
            Assert.That(checkpoints.Pending, Is.Empty);
        });
    }

    [Test]
    public void ImportRejectsEquivalentNumericIdsAcrossBsonTypesBeforeMongoAccess()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] =
            "[{\"_id\":{\"key\":1}},{\"_id\":{\"key\":{\"$numberLong\":\"1\"}}}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 2);
        var driver = new FakeMongoDriver();

        Assert.ThrowsAsync<ArgumentException>(() => CreateService(files, driver).ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"), new DatabaseImportRequest(sourceDirectory, "target")));
        Assert.That(driver.ClientRequests, Is.Zero);
    }

    [Test]
    public async Task LegacyPackageUsesUpsertOnlyWhenExplicitlySelectedAndConfirmed()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] = "[{\"_id\":1,\"value\":\"first\"},{\"_id\":1,\"value\":\"last\"}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 2);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);

        await service.ImportDatabaseAsync(ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target"));

        Assert.That(driver.Databases["target"].WriteModelTypes, Has.All.StartsWith("ReplaceOneModel"));
        Assert.That(driver.Databases["target"].WrittenBsonDocuments["items"], Has.Count.EqualTo(2));
    }

    [Test]
    public void InsertCollisionKeepsFailureVisibleAndCleansCreatedCollection()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { FailDuplicateInsert = true };
        var service = CreateService(files, driver);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"), new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(error!.Message, Does.Contain("duplicate key collision"));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(ExpectedCollisionCleanup));
    }

    [Test]
    public void RejectRefusesDeclaredCountAboveBoundBeforeMongoAccess()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 1_000_001)
            .Replace("\"DocumentsPerCollectionLimit\": 100", "\"DocumentsPerCollectionLimit\": 1000000", StringComparison.Ordinal);
        var driver = new FakeMongoDriver();

        Assert.ThrowsAsync<ArgumentException>(() => CreateService(files, driver).ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"), new DatabaseImportRequest(sourceDirectory, "target")));
        Assert.That(driver.ClientRequests, Is.Zero);
    }

    [Test]
    public async Task ImportDetectsMutationBetweenStreamingPassesAndCleansCreatedCollection()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var dataFile = Path.Combine(sourceDirectory, "items.extended.json");
        files.OnOpenRead = (path, openNumber) =>
        {
            if (path == dataFile && openNumber == 2)
                files.Text[dataFile] = "[{\"_id\":2}]";
        };
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);

        var failure = Assert.ThrowsAsync<ArgumentException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(failure!.Message, Does.Contain("mudou entre a pré-validação e a importação"));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:items", "write:items", "drop:items" }));
    }

    [Test]
    public async Task ImportBoundsBatchBySerializedBsonBytes()
    {
        var files = new MemoryExportFiles();
        var sourceDirectory = files.CreateLegacyDirectory("bounded-batches");
        var largeValue = new string('x', 2_300_000);
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] =
            "[{\"_id\":1,\"payload\":\"" + largeValue + "\"},{\"_id\":2,\"payload\":\"" + largeValue + "\"}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 2);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);

        await service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target"));

        var state = driver.Databases["target"];
        Assert.That(state.BulkWriteBatchDocumentCounts, Is.EqualTo(ExpectedTwoSingleDocumentBatches));
        Assert.That(state.BulkWriteBatchBytes.All(bytes => bytes <= 4 * 1024 * 1024), Is.True);
    }

    [Test]
    public async Task ImportCancellationAfterCollectionCreationCleansCreatedCollection()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        using var cancellation = new CancellationTokenSource();
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { CancelAfterCreate = cancellation };
        var service = CreateService(files, driver);

        Assert.ThrowsAsync<OperationCanceledException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target"), cancellation.Token));

        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:items", "drop:items" }));
    }

    [Test]
    public async Task ImportStillReadsVersionOneManifestForRegularCollections()
    {
        var files = new MemoryExportFiles();
        var sourceDirectory = files.CreateLegacyDirectory("legacy-v1");
        files.Text[Path.Combine(sourceDirectory, "legacy.extended.json")] = "[{\"_id\":1,\"name\":\"legacy\"}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = """
            {
              "FormatVersion": 1,
              "Database": "old",
              "CreatedAtUtc": "2026-10-08T00:00:00+00:00",
              "DocumentsPerCollectionLimit": 100,
              "Collections": [
                { "Name": "items", "File": "legacy.extended.json", "Documents": 1, "IsTruncated": false }
              ]
            }
            """;
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var service = CreateService(files, driver);

        var imported = await service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target"));

        Assert.That(imported.CollectionCount, Is.EqualTo(1));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:items", "write:items" }));
    }

    [Test]
    public async Task ImportReportsSecretFreeStageAndDocumentProgress()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var progress = new RecordingProgress<DatabaseImportProgress>();
        var request = new DatabaseImportRequest(sourceDirectory, "target") { Progress = progress };

        await CreateService(files, driver).ImportDatabaseAsync(ConnectionProfile.Create("Local", "mongodb://localhost:27017"), request);

        Assert.That(progress.Values.Select(item => item.Stage), Does.Contain(DatabaseImportStage.Validating));
        Assert.That(progress.Values.Select(item => item.Stage), Does.Contain(DatabaseImportStage.ImportingDocuments));
        Assert.That(progress.Values[^1].Stage, Is.EqualTo(DatabaseImportStage.Completed));
        Assert.That(progress.Values[^1].ProcessedDocuments, Is.EqualTo(1));
        Assert.That(progress.Values[^1].InsertedDocuments, Is.EqualTo(1));
        Assert.That(progress.Values.Any(item => item.CurrentCollection == "items"), Is.True);
    }

    [Test]
    public void ImportRejectsLegacyManifestWithSystemNamespaceBeforeMongoAccess()
    {
        var files = new MemoryExportFiles();
        var sourceDirectory = files.CreateLegacyDirectory("unsafe-v1");
        files.Text[Path.Combine(sourceDirectory, "system.extended.json")] = "[]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = """
            {
              "FormatVersion": 1,
              "Database": "old",
              "CreatedAtUtc": "2026-10-08T00:00:00+00:00",
              "DocumentsPerCollectionLimit": 100,
              "Collections": [
                { "Name": "system.views", "File": "system.extended.json", "Documents": 0, "IsTruncated": false }
              ]
            }
            """;
        var driver = new FakeMongoDriver();
        var service = CreateService(files, driver);

        var failure = Assert.ThrowsAsync<ArgumentException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(failure!.Message, Does.Contain("coleção inválida"));
        Assert.That(driver.ClientRequests, Is.Zero, "Unsafe legacy namespaces are refused before any Mongo access.");
    }

    [Test]
    public void ImportRefusesNonEmptyDestinationWithoutWriting()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { Collections = { "already_exists" } };
        var service = CreateService(files, driver);

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(failure!.Message, Does.Contain("banco de destino vazio"));
        Assert.That(driver.Databases["target"].Operations, Is.Empty);
    }

    [Test]
    public void ImportPrevalidatesEveryDataFileBeforeFirstWrite()
    {
        var files = new MemoryExportFiles();
        var sourceDirectory = files.CreateLegacyDirectory("invalid-package");
        files.Text[Path.Combine(sourceDirectory, "first.extended.json")] = "[{\"_id\":1}]";
        files.Text[Path.Combine(sourceDirectory, "second.extended.json")] = "[{\"name\":\"missing-id\"}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = """
            {
              "FormatVersion": 1,
              "Database": "old",
              "CreatedAtUtc": "2026-10-08T00:00:00+00:00",
              "DocumentsPerCollectionLimit": 100,
              "Collections": [
                { "Name": "first", "File": "first.extended.json", "Documents": 1, "IsTruncated": false },
                { "Name": "second", "File": "second.extended.json", "Documents": 1, "IsTruncated": false }
              ]
            }
            """;
        var driver = new FakeMongoDriver();
        var service = CreateService(files, driver);

        Assert.ThrowsAsync<ArgumentException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(driver.ClientRequests, Is.Zero);
        Assert.That(driver.Databases.Values.SelectMany(database => database.Operations), Is.Empty);
    }

    [Test]
    public void ImportReportsPossiblePartialDestinationWhenCleanupFails()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { FailCleanup = true };
        var service = CreateService(files, driver);
        driver.Databases["target"].FailViewCreation = true;
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = """
            {
              "FormatVersion": 2,
              "Database": "old",
              "CreatedAtUtc": "2026-10-08T00:00:00+00:00",
              "DocumentsPerCollectionLimit": 100,
              "Collections": [
                { "Name": "items", "File": "items.extended.json", "Documents": 1, "IsTruncated": false }
              ],
              "Views": [
                { "Name": "active", "ViewOn": "items", "Pipeline": ["{\"$match\":{\"_id\":1}}"], "Collation": null }
              ]
            }
            """;

        var failure = Assert.ThrowsAsync<AggregateException>(() => service.ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(failure!.Message, Does.Contain("destino pode conter estado parcial que exige revisão"));
        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(new List<string> { "create:items", "write:items", "drop:items" }));
        Assert.That(driver.Databases["target"].Operations, Does.Not.Contain("drop:active"),
            "A view não foi confirmada como criada, então o cleanup não pode removê-la como propriedade da importação.");
    }

    [Test]
    public async Task PackageImportPersistsCheckpointAfterPreflightAndCompletesAfterBatch()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState();
        var checkpoints = new FakeImportCheckpointRepository();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        await CreateService(files, driver, checkpoints).ImportDatabaseAsync(profile,
            new DatabaseImportRequest(sourceDirectory, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(checkpoints.Events, Is.EqualTo(CompletedCheckpointEvents));
            Assert.That(checkpoints.Created!.Kind, Is.EqualTo(ImportCheckpointKind.LogicalPackage));
            Assert.That(checkpoints.Created.ProfileId, Is.EqualTo(profile.Id));
            Assert.That(checkpoints.Created.SourceSha256, Has.Length.EqualTo(64));
            Assert.That(checkpoints.Created!.ToString(), Does.Not.Contain(sourceDirectory));
            Assert.That(checkpoints.Updates.Single().ProcessedDocuments, Is.EqualTo(1));
            Assert.That(checkpoints.Pending, Is.Empty);
        });
    }

    [Test]
    public void PackageUncertainCreateLeavesNeedsReviewWithoutDroppingUnknownNamespace()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var driver = new FakeMongoDriver();
        driver.Databases["target"] = new DatabaseState { FailCreateAfterAdding = true };
        var checkpoints = new FakeImportCheckpointRepository();

        Assert.ThrowsAsync<AggregateException>(() => CreateService(files, driver, checkpoints).ImportDatabaseAsync(
            ConnectionProfile.Create("Local", "mongodb://localhost:27017"),
            new DatabaseImportRequest(sourceDirectory, "target")));

        Assert.That(driver.Databases["target"].Operations, Is.EqualTo(ExpectedUncertainCreate));
        Assert.That(checkpoints.Pending.Single().State, Is.EqualTo(ImportCheckpointState.NeedsReview));
    }

    [Test]
    public async Task PackageRestartRechecksSourcePlanAndEmptyTargetBeforeAtomicReset()
    {
        var files = CreateVersionOnePackage(out var sourceDirectory);
        var documentFile = Path.Combine(sourceDirectory, "items.extended.json");
        var driver = new FakeMongoDriver();
        var target = new DatabaseState { FailCreateAfterAdding = true };
        driver.Databases["target"] = target;
        var checkpoints = new FakeImportCheckpointRepository();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var service = CreateService(files, driver, checkpoints);
        var request = new DatabaseImportRequest(sourceDirectory, "target", ConfirmationDatabase: "target");
        Assert.ThrowsAsync<AggregateException>(() => service.ImportDatabaseAsync(profile, request));
        var checkpointId = checkpoints.Pending.Single().Id;

        var occupied = await service.InspectDatabaseImportRestartAsync(profile, request, checkpointId);
        target.Collections.Remove("items");
        files.Text[documentFile] = "[{\"_id\":2}]";
        var changedSource = await service.InspectDatabaseImportRestartAsync(profile, request, checkpointId);
        files.Text[documentFile] = "[{\"_id\":1}]";
        var changedPlan = await service.InspectDatabaseImportRestartAsync(profile,
            new DatabaseImportRequest(sourceDirectory, "target", DatabaseImportDuplicatePolicy.Upsert, "target"), checkpointId);
        var eligible = await service.InspectDatabaseImportRestartAsync(profile, request, checkpointId);
        files.Text[documentFile] = "[{\"_id\":3}]";
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportDatabaseAsync(profile,
            request with { RestartCheckpointId = checkpointId }));
        Assert.That(checkpoints.Events, Does.Not.Contain("reset"));
        files.Text[documentFile] = "[{\"_id\":1}]";
        target.Collections.Add("arrived_later");
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportDatabaseAsync(profile,
            request with { RestartCheckpointId = checkpointId }));
        Assert.That(checkpoints.Events, Does.Not.Contain("reset"));
        target.Collections.Remove("arrived_later");
        target.FailCreateAfterAdding = false;
        await service.ImportDatabaseAsync(profile, request with { RestartCheckpointId = checkpointId });

        Assert.Multiple(() =>
        {
            Assert.That(occupied.ReasonCode, Is.EqualTo("target-not-empty"));
            Assert.That(changedSource.ReasonCode, Is.EqualTo("source-changed"));
            Assert.That(changedPlan.ReasonCode, Is.EqualTo("plan-changed"));
            Assert.That(eligible.CanRestartFromBeginning, Is.True);
            Assert.That(checkpoints.Events.Count(item => item == "reset"), Is.EqualTo(1));
            Assert.That(checkpoints.Pending, Is.Empty);
        });
    }

    private static MemoryExportFiles CreateVersionOnePackage(out string sourceDirectory)
    {
        var files = new MemoryExportFiles();
        sourceDirectory = files.CreateLegacyDirectory("legacy-package");
        files.Text[Path.Combine(sourceDirectory, "items.extended.json")] = "[{\"_id\":1}]";
        files.Text[Path.Combine(sourceDirectory, "manifest.json")] = VersionOneManifest("items", "items.extended.json", 1);
        return files;
    }

    private static string VersionOneManifest(string collection, string file, int documents) => $$"""
            {
              "FormatVersion": 1,
              "Database": "old",
              "CreatedAtUtc": "2026-10-08T00:00:00+00:00",
              "DocumentsPerCollectionLimit": 100,
              "Collections": [
                { "Name": "{{collection}}", "File": "{{file}}", "Documents": {{documents}}, "IsTruncated": false }
              ]
            }
            """;

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }

    private sealed class ThrowingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static MongoWorkspaceService CreateService(MemoryExportFiles files, FakeMongoDriver driver,
        IImportCheckpointRepository? checkpoints = null) =>
        new(files, clients: new FakeClientPool(driver), importCheckpoints: checkpoints);

    private sealed class FakeImportCheckpointRepository : IImportCheckpointRepository
    {
        public ImportCheckpoint? Created { get; private set; }
        public List<ImportCheckpoint> Updates { get; } = [];
        public List<string> Events { get; } = [];
        public List<ImportCheckpoint> Pending { get; } = [];
        public bool FailCreate { get; set; }
        public Task<IReadOnlyList<ImportCheckpoint>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportCheckpoint>>(Pending.ToArray());
        public Task CreateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (FailCreate) throw new InvalidOperationException("Synthetic checkpoint persistence failure.");
            Created = checkpoint;
            Pending.Add(checkpoint);
            Events.Add("create");
            return Task.CompletedTask;
        }
        public Task UpdateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            Updates.Add(checkpoint);
            Pending[0] = checkpoint;
            Events.Add("update");
            return Task.CompletedTask;
        }
        public Task ResetForRestartAsync(ImportCheckpoint expected, ImportCheckpoint restarted,
            CancellationToken cancellationToken = default)
        {
            var index = Pending.FindIndex(checkpoint => checkpoint.Id == expected.Id);
            if (index < 0 || Pending[index] != expected)
                throw new InvalidOperationException("Synthetic stale checkpoint.");
            Pending[index] = restarted;
            Events.Add("reset");
            return Task.CompletedTask;
        }
        public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Pending.RemoveAll(checkpoint => checkpoint.Id == id);
            Events.Add("complete");
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateProgress(Action<DatabaseImportProgress> report) : IProgress<DatabaseImportProgress>
    {
        public void Report(DatabaseImportProgress value) => report(value);
    }

    private sealed class MemoryExportFiles : IMongoDatabaseExportFileAccess, IStreamingMongoDatabaseExportFileAccess, IExportDirectoryCleanupAccess
    {
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public int CollectionStreamOpenCount { get; private set; }
        public int CollectionReadAllCount { get; private set; }
        public IReadOnlyCollection<string> ExportDirectories => _directories.ToArray();
        public bool FailExportCleanup { get; init; }
        public Action<string, int>? OnOpenRead { get; set; }
        private readonly Dictionary<string, int> _streamOpenCounts = new(StringComparer.Ordinal);
        public string NormalizePath(string path) => path;
        public string CreateExportDirectory(string directoryName)
        {
            var directory = $@"C:\kapibara-export\{directoryName}";
            _directories.Add(directory);
            return directory;
        }
        public string CreateLegacyDirectory(string name)
        {
            var directory = $@"C:\kapibara-export\{name}";
            _directories.Add(directory);
            return directory;
        }
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void DeleteExportDirectory(string path)
        {
            if (FailExportCleanup) throw new IOException("Synthetic export cleanup failure.");
            _directories.Remove(path);
            foreach (var file in Text.Keys.Where(file => file.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.StartsWith(path + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)).ToArray())
                Text.Remove(file);
        }
        public bool FileExists(string path) => Text.ContainsKey(path);
        public Stream CreateNewFile(string path) => new CommitStream(value => Text[path] = value);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text[path] = content;
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.EndsWith(".extended.json", StringComparison.Ordinal)) CollectionReadAllCount++;
            return Text.TryGetValue(path, out var content)
                ? Task.FromResult(content)
                : Task.FromException<string>(new FileNotFoundException(path));
        }
        public Stream OpenRead(string path)
        {
            var openNumber = _streamOpenCounts.TryGetValue(path, out var count) ? count + 1 : 1;
            _streamOpenCounts[path] = openNumber;
            if (path.EndsWith(".extended.json", StringComparison.Ordinal)) CollectionStreamOpenCount++;
            OnOpenRead?.Invoke(path, openNumber);
            return Text.TryGetValue(path, out var content)
                ? new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false)
                : throw new FileNotFoundException(path);
        }

        private sealed class CommitStream(Action<string> commit) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) commit(Encoding.UTF8.GetString(ToArray()));
                base.Dispose(disposing);
            }
        }
    }

    private sealed class FakeClientPool(FakeMongoDriver driver) : IMongoClientPool
    {
        public IMongoClient GetClient(MongoClientSettings settings)
        {
            driver.ClientRequests++;
            return driver.Client;
        }
    }

    private sealed class FakeMongoDriver
    {
        public Dictionary<string, DatabaseState> Databases { get; } = new(StringComparer.Ordinal);
        public int ClientRequests { get; set; }
        public IMongoClient Client { get; }

        public FakeMongoDriver()
        {
            Client = CreateProxy<IMongoClient>((method, args) => method.Name switch
            {
                "GetDatabase" => GetDatabase((string)args![0]!),
                _ => throw new NotSupportedException($"Unexpected IMongoClient method {method.Name}.")
            });
        }

        private IMongoDatabase GetDatabase(string name)
        {
            if (!Databases.TryGetValue(name, out var state)) Databases[name] = state = new DatabaseState();
            return CreateProxy<IMongoDatabase>((method, args) => method.Name switch
            {
                "ListCollectionNamesAsync" => Task.FromResult(CreateCursor(state.Collections.ToArray())),
                "ListCollectionsAsync" => Task.FromResult(CreateCursor(GetCollectionInfos(state))),
                "CreateCollectionAsync" => CreateCollection(state, (string)args![0]!),
                "DropCollectionAsync" => DropCollection(state, (string)args![0]!),
                "GetCollection" => GetCollection(state, (string)args![0]!),
                "CreateViewAsync" => CreateView(state, args!),
                _ => throw new NotSupportedException($"Unexpected IMongoDatabase method {method.Name}.")
            });
        }

        private static Task CreateCollection(DatabaseState state, string name)
        {
            state.Collections.Add(name);
            state.Operations.Add($"create:{name}");
            state.CancelAfterCreate?.Cancel();
            if (state.FailCreateAfterAdding) throw new InvalidOperationException("Synthetic uncertain create.");
            return Task.CompletedTask;
        }

        private static Task DropCollection(DatabaseState state, string name)
        {
            state.Operations.Add($"drop:{name}");
            if (state.FailCleanup) throw new InvalidOperationException("Synthetic cleanup failure.");
            state.Collections.Remove(name);
            return Task.CompletedTask;
        }

        private static IMongoCollection<BsonDocument> GetCollection(DatabaseState state, string name) =>
            CreateProxy<IMongoCollection<BsonDocument>>((method, args) =>
            {
                if (method.Name == "get_Indexes")
                    return CreateProxy<IMongoIndexManager<BsonDocument>>((indexMethod, _) =>
                    {
                        if (indexMethod.Name != "ListAsync") throw new NotSupportedException($"Unexpected IMongoIndexManager method {indexMethod.Name}.");
                        IReadOnlyList<BsonDocument> indexes = [new BsonDocument { { "v", 2 }, { "key", new BsonDocument("_id", 1) }, { "name", "_id_" }, { "ns", $"source.{name}" } }];
                        return Task.FromResult(CreateCursor(indexes));
                    });
                if (method.Name == "FindAsync")
                {
                    if (state.FailExportRead) throw new InvalidOperationException("Synthetic export read failure.");
                    state.FindCalls.Add(name);
                    return Task.FromResult(CreateCursor(state.Documents.TryGetValue(name, out var documents) ? documents : []));
                }
                if (method.Name == "BulkWriteAsync") return BulkWrite(state, name, args!);
                throw new NotSupportedException($"Unexpected IMongoCollection method {method.Name}.");
            });

        private static BsonDocument[] GetCollectionInfos(DatabaseState state)
        {
            var infos = state.Definitions.Select(document => document.DeepClone().AsBsonDocument).ToList();
            var known = infos.Select(document => document.GetValue("name", BsonNull.Value))
                .Where(value => value.BsonType == BsonType.String).Select(value => value.AsString)
                .ToHashSet(StringComparer.Ordinal);
            infos.AddRange(state.Collections.Where(name => !known.Contains(name))
                .Select(name => new BsonDocument("name", name).Add("type", "collection")));
            return infos.ToArray();
        }

        private static Task<BulkWriteResult<BsonDocument>> BulkWrite(DatabaseState state, string collection, object?[] args)
        {
            var writes = ((IEnumerable<WriteModel<BsonDocument>>)args[0]!).ToArray();
            if (string.Equals(state.FailBulkWriteCollection, collection, StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic bulk write failure.");
            state.WrittenDocuments[collection] = state.WrittenDocuments.GetValueOrDefault(collection) + writes.Length;
            var documents = writes.Select(model => model switch
            {
                InsertOneModel<BsonDocument> insert => insert.Document,
                ReplaceOneModel<BsonDocument> replace => replace.Replacement,
                _ => throw new NotSupportedException("Unexpected import write model.")
            }).ToArray();
            state.WriteModelTypes.AddRange(writes.Select(model => model.GetType().Name));
            state.UpsertFlags.AddRange(writes.Select(model => model is ReplaceOneModel<BsonDocument> replace && replace.IsUpsert));
            if (state.FailDuplicateInsert && writes.Any(model => model is InsertOneModel<BsonDocument>))
                throw new InvalidOperationException("Synthetic duplicate key collision.");
            if (!state.WrittenBsonDocuments.TryGetValue(collection, out var capturedDocuments))
                state.WrittenBsonDocuments[collection] = capturedDocuments = [];
            capturedDocuments.AddRange(documents);
            var storedDocuments = state.Documents.TryGetValue(collection, out var existingDocuments)
                ? existingDocuments.ToList() : [];
            foreach (var model in writes)
            {
                if (model is InsertOneModel<BsonDocument> insert)
                {
                    storedDocuments.Add(insert.Document);
                    continue;
                }

                if (model is ReplaceOneModel<BsonDocument> replace)
                {
                    var id = replace.Replacement["_id"];
                    var existingIndex = storedDocuments.FindIndex(document => document.TryGetValue("_id", out var existingId)
                        && existingId == id);
                    if (existingIndex >= 0) storedDocuments[existingIndex] = replace.Replacement;
                    else if (replace.IsUpsert) storedDocuments.Add(replace.Replacement);
                }
            }
            state.Documents[collection] = storedDocuments;
            state.BulkWriteBatchDocumentCounts.Add(writes.Length);
            state.BulkWriteBatchBytes.Add(documents.Sum(document => document.ToBson().Length));
            state.Operations.Add($"write:{collection}");
            var insertedCount = writes.Count(model => model is InsertOneModel<BsonDocument>);
            return Task.FromResult<BulkWriteResult<BsonDocument>>(new BulkWriteResult<BsonDocument>.Acknowledged(
                writes.Length, 0, 0, insertedCount, insertedCount, writes, Array.Empty<BulkWriteUpsert>()));
        }

        private static Task CreateView(DatabaseState state, object?[] args)
        {
            if (state.FailViewCreation) throw new InvalidOperationException("Synthetic view creation failure.");
            var name = (string)args[0]!;
            var viewOn = (string)args[1]!;
            var pipeline = (PipelineDefinition<BsonDocument, BsonDocument>)args[2]!;
            var options = args[3] as CreateViewOptions<BsonDocument>;
            state.Collections.Add(name);
            state.Views.Add(new CreatedView(
                name,
                viewOn,
                pipeline.ToString(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry),
                options?.Collation));
            state.Definitions.Add(new BsonDocument("name", name).Add("type", "view").Add("options", new BsonDocument
            {
                { "viewOn", viewOn },
                { "pipeline", BsonSerializer.Deserialize<BsonArray>(pipeline.ToString(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)) },
                { "collation", options?.Collation is null ? BsonNull.Value : options.Collation.ToBsonDocument() }
            }));
            state.Operations.Add($"view:{name}");
            return Task.CompletedTask;
        }

        private static IAsyncCursor<T> CreateCursor<T>(IReadOnlyList<T> items)
        {
            var moved = false;
            return CreateProxy<IAsyncCursor<T>>((method, _) => method.Name switch
            {
                "MoveNext" => Move(),
                "MoveNextAsync" => Task.FromResult(Move()),
                "get_Current" => moved ? items : Array.Empty<T>(),
                "Dispose" => null,
                _ => throw new NotSupportedException($"Unexpected IAsyncCursor method {method.Name}.")
            });

            bool Move()
            {
                if (moved || items.Count == 0) return false;
                moved = true;
                return true;
            }
        }

        private static T CreateProxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, DriverDispatchProxy<T>>();
            ((DriverDispatchProxy<T>)(object)proxy).Handler = handler;
            return proxy;
        }
    }

    private sealed class DatabaseState
    {
        public HashSet<string> Collections { get; } = new(StringComparer.Ordinal);
        public List<BsonDocument> Definitions { get; } = [];
        public Dictionary<string, IReadOnlyList<BsonDocument>> Documents { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> WrittenDocuments { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<BsonDocument>> WrittenBsonDocuments { get; } = new(StringComparer.Ordinal);
        public List<bool> UpsertFlags { get; } = [];
        public List<int> BulkWriteBatchDocumentCounts { get; } = [];
        public List<int> BulkWriteBatchBytes { get; } = [];
        public List<string> WriteModelTypes { get; } = [];
        public bool FailDuplicateInsert { get; set; }
        public string? FailBulkWriteCollection { get; set; }
        public bool FailCreateAfterAdding { get; set; }
        public List<string> FindCalls { get; } = [];
        public List<string> Operations { get; } = [];
        public List<CreatedView> Views { get; } = [];
        public bool FailViewCreation { get; set; }
        public bool FailExportRead { get; set; }
        public bool FailCleanup { get; set; }
        public CancellationTokenSource? CancelAfterCreate { get; set; }

        public static DatabaseState WithSourceData()
        {
            var state = new DatabaseState();
            state.Collections.UnionWith(["movies", "active_movies", "system.views"]);
            state.Documents["movies"] = [new BsonDocument("_id", 1).Add("active", true).Add("title", "Movie")];
            state.Documents["system.views"] = [new BsonDocument("_id", "source.system.views")];
            state.Definitions.Add(new BsonDocument { { "name", "movies" }, { "type", "collection" } });
            state.Definitions.Add(new BsonDocument { { "name", "system.views" }, { "type", "collection" } });
            state.Definitions.Add(new BsonDocument
            {
                { "name", "active_movies" },
                { "type", "view" },
                { "options", new BsonDocument
                    {
                        { "viewOn", "movies" },
                        { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument("active", true)) } },
                        { "collation", new BsonDocument { { "locale", "en" }, { "strength", 2 } } }
                    }
                }
            });
            return state;
        }
    }

    private sealed record CreatedView(string Name, string ViewOn, string PipelineJson, Collation? Collation);

    public class DriverDispatchProxy<T> : DispatchProxy where T : class
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod ?? throw new InvalidOperationException("Missing target method."), args ?? []);
    }
}
