using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelTextExportTests
{
    private string _previousLanguage = "pt-BR";

    [SetUp]
    public void SetUp()
    {
        _previousLanguage = LocalizationViewModel.Current.Language;
        LocalizationViewModel.Current.Language = "pt-BR";
    }

    [TearDown]
    public void TearDown() => LocalizationViewModel.Current.Language = _previousLanguage;

    [Test]
    public async Task AuditExportWritesSerializedJsonThroughTheTextExportPort()
    {
        var exporter = new FakeTextExportFileService();
        using var context = new WorkspaceTestContext(textExports: exporter);
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            AuditExportPath = "exports/audit.json"
        };

        await viewModel.ExportAuditCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(exporter.Calls, Is.EqualTo(1));
            Assert.That(exporter.Path, Is.EqualTo("exports/audit.json"));
            Assert.That(exporter.Content, Is.EqualTo(AuditJsonSerializer.Serialize([])));
            Assert.That(viewModel.StatusMessage, Does.Contain("Auditoria exportada para"));
        });
    }

    [Test]
    public async Task AuditExportKeepsFailureAndConflictMessagesDistinct()
    {
        using var context = new WorkspaceTestContext(textExports: new FakeTextExportFileService
        {
            Failure = new TextExportFileAlreadyExistsException("audit.json")
        });
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            AuditExportPath = "audit.json"
        };

        await viewModel.ExportAuditCommand.ExecuteAsync(null);

            Assert.That(viewModel.StatusMessage, Does.Contain("O arquivo de auditoria já existe"));
    }

    [Test]
    public async Task AuditExportReportsOtherPortFailures()
    {
        using var context = new WorkspaceTestContext(textExports: new FakeTextExportFileService
        {
            Failure = new IOException("synthetic storage failure")
        });
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            AuditExportPath = "audit.json"
        };

        await viewModel.ExportAuditCommand.ExecuteAsync(null);

        Assert.That(viewModel.StatusMessage, Does.Contain("synthetic storage failure"));
        Assert.That(viewModel.StatusMessage, Does.Not.Contain("O arquivo de auditoria já existe"));
    }

    [Test]
    public async Task QueryExportNormalizesPathAndKeepsJsonSerializerOutsideThePort()
    {
        var exporter = new FakeTextExportFileService();
        using var context = new WorkspaceTestContext(textExports: exporter);
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false);
        var documents = new[] { "{ \"_id\": 1 }" };
        typeof(MainWindowViewModel).GetField("_lastQueryDocuments", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, documents);
        viewModel.QueryExportPath = SyntheticPaths.Combine("exports", "query.json");

        await viewModel.ExportQueryResultsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(exporter.Calls, Is.EqualTo(1));
            Assert.That(exporter.Path, Is.EqualTo(viewModel.QueryExportPath));
            Assert.That(exporter.Content, Is.EqualTo(QueryResultExportSerializer.Serialize(documents)));
            Assert.That(viewModel.StatusMessage, Does.Contain("1 documento(s) exportado(s) em Extended JSON"));
        });
    }

    private sealed class FakeTextExportFileService : ITextExportFileService
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? Content { get; private set; }
        public Exception? Failure { get; init; }

        public Task WriteNewAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Calls++;
            Path = path;
            Content = content;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
}
