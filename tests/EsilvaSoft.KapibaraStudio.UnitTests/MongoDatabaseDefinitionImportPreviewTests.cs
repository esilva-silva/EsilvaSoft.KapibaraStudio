using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoDatabaseDefinitionImportPreviewTests
{
    [Test]
    public async Task BuildsPerItemPlanAndBlocksTransitiveViewDependencies()
    {
        var definitions = new DatabaseDefinitionManifest(1,
            [new CollectionDefinitionSnapshot("orders", "{}", null,
                [new IndexDefinitionSnapshot("_id_", "{\"_id\":1}", "{}"),
                 new IndexDefinitionSnapshot("customer_1", "{\"customer\":1}", "{}")])],
            [new ViewDefinitionSnapshot("recent", "orders", "[]", null),
             new ViewDefinitionSnapshot("recent2", "recent", "[]", null)]);
        var files = new MemoryManifestFiles(JsonSerializer.Serialize(new
        {
            FormatVersion = 3,
            Collections = new[] { new { Name = "orders" } },
            Views = new[] { new { Name = "recent", ViewOn = "orders", Pipeline = Array.Empty<string>(), Collation = (string?)null },
                            new { Name = "recent2", ViewOn = "recent", Pipeline = Array.Empty<string>(), Collation = (string?)null } },
            Definitions = definitions
        }));
        var reads = 0;
        var plan = await MongoDatabaseDefinitionImportPreview.CreateAsync(files, "package",
            _ => { reads++; return Task.FromResult<IReadOnlyList<string>>(["orders"]); }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(plan.Items.Count, Is.EqualTo(5));
            Assert.That(plan.Items.Single(item => item.Name == "_id_").Status, Is.EqualTo(DatabaseDefinitionPlannedStatus.Blocked));
            Assert.That(plan.Items.Single(item => item.Target == "recent2").DependsOn.Single(), Is.EqualTo("recent"));
            Assert.That(plan.Items.Single(item => item.Target == "recent2").Status, Is.EqualTo(DatabaseDefinitionPlannedStatus.Blocked));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RejectsDefinitionsWithMissingViewSourcesOrCyclesBeforeReadingTarget(bool cycle)
    {
        var views = cycle
            ? new[]
            {
                new { Name = "cycleA", ViewOn = "cycleB", PipelineJson = "[]", CollationJson = (string?)null },
                new { Name = "cycleB", ViewOn = "cycleA", PipelineJson = "[]", CollationJson = (string?)null }
            }
            : [new { Name = "missingSource", ViewOn = "notInPackageOrTarget", PipelineJson = "[]", CollationJson = (string?)null }];
        var legacyViews = views.Select(view => new
        {
            view.Name,
            view.ViewOn,
            Pipeline = Array.Empty<string>(),
            Collation = (string?)null
        }).ToArray();
        var files = new MemoryManifestFiles(JsonSerializer.Serialize(new
        {
            FormatVersion = 3,
            Collections = Array.Empty<object>(),
            Views = legacyViews,
            Definitions = new { SchemaVersion = 1, Collections = Array.Empty<object>(), Views = views }
        }));
        var reads = 0;

        Assert.ThrowsAsync<ArgumentException>(async () => await MongoDatabaseDefinitionImportPreview.CreateAsync(
            files, "package", _ => { reads++; return Task.FromResult<IReadOnlyList<string>>([]); }, CancellationToken.None));
        Assert.That(reads, Is.Zero);
    }

    [Test]
    public void LegacyManifestIsRejectedBeforeReadingTarget()
    {
        var files = new MemoryManifestFiles("{\"FormatVersion\":2,\"Collections\":[],\"Views\":[]}");
        var reads = 0;
        Assert.ThrowsAsync<ArgumentException>(async () => { await MongoDatabaseDefinitionImportPreview.CreateAsync(
            files, "package", _ => { reads++; return Task.FromResult<IReadOnlyList<string>>([]); }, CancellationToken.None); });
        Assert.That(reads, Is.Zero);
    }

    private sealed class MemoryManifestFiles(string manifest) : IMongoDatabaseExportFileAccess, IStreamingMongoDatabaseExportFileAccess
    {
        public string NormalizePath(string path) => path;
        public string CreateExportDirectory(string directoryName) => throw new NotSupportedException();
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => path.EndsWith("manifest.json", StringComparison.Ordinal);
        public Stream CreateNewFile(string path) => throw new NotSupportedException();
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(manifest);
        public Stream OpenRead(string path) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifest));
    }
}
