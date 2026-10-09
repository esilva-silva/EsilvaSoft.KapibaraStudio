using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelIndexDiagnosticsTests
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
    public async Task DiagnosticsUseCapturedTargetAndRenderUsageSizeAndConservativeCandidate()
    {
        using var context = new WorkspaceTestContext();
        var calls = new List<(string Method, string Database, string Collection)>();
        context.Mongo.Handler = (method, args) =>
        {
            if (method != "GetCurrentOperationsAsync")
                calls.Add((method, (string)args[1]!, (string)args[2]!));
            return method switch
            {
                "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>([
                    "{\"name\":\"email_1\",\"key\":{\"email\":1}}",
                    "{\"name\":\"email_1_date_1\",\"key\":{\"email\":1,\"date\":1}}"
                ]),
                "GetIndexUsageStatsAsync" => Task.FromResult<IReadOnlyList<string>>([
                    "{\"name\":\"email_1\",\"host\":\"node-a:27017\",\"accesses\":{\"ops\":{\"$numberLong\":\"7\"},\"since\":{\"$date\":\"2026-10-08T12:00:00Z\"}}}"
                ]),
                "GetCollectionStatsAsync" => Task.FromResult("{\"indexSizes\":{\"email_1\":{\"$numberLong\":\"4096\"}}}"),
                "GetCurrentOperationsAsync" => Task.FromResult("{\"inprog\":[{\"ns\":\"sample_mflix.movies\",\"command\":{\"createIndexes\":\"movies\",\"$db\":\"sample_mflix\",\"commitQuorum\":\"majority\",\"indexes\":[{\"name\":\"title_1\"}]},\"msg\":\"Index Build: scanning\",\"progress\":{\"done\":2,\"total\":10},\"secretQuery\":{\"email\":\"private@example.test\"}}]}"),
                _ => throw new InvalidOperationException(method)
            };
        };
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = profile,
            SelectedDatabase = "sample_mflix",
            SelectedCollection = "movies"
        };

        await viewModel.LoadIndexDiagnosticsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls.All(call => call.Database == "sample_mflix" && call.Collection == "movies"), Is.True);
            Assert.That(viewModel.IndexResults, Does.Contain("$indexStats"));
            Assert.That(viewModel.IndexResults, Does.Contain("collStats.indexSizes"));
            Assert.That(viewModel.IndexResults, Does.Contain("4096 bytes"));
            Assert.That(viewModel.IndexResults, Does.Contain("operações=7"));
            Assert.That(viewModel.IndexResults, Does.Contain("email_1 (candidato)"));
            Assert.That(viewModel.IndexResults, Does.Contain("currentOp"));
            Assert.That(viewModel.IndexResults, Does.Contain("commit quorum no comando observado=majority"));
            Assert.That(viewModel.IndexResults, Does.Contain("progresso 2/10"));
            Assert.That(viewModel.IndexResults, Does.Not.Contain("private@example.test"));
            Assert.That(viewModel.IndexResults, Does.Contain("workload"));
        });
    }

    [Test]
    public async Task DiagnosticsKeepPartialResultsAndExposePermissionFailure()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>(["{\"name\":\"_id_\",\"key\":{\"_id\":1}}"]),
            "GetIndexUsageStatsAsync" => throw new InvalidOperationException("clusterMonitor permission denied"),
            "GetCollectionStatsAsync" => Task.FromResult("{\"indexSizes\":{\"_id_\":1024}}"),
            "GetCurrentOperationsAsync" => Task.FromResult("{\"inprog\":[]}"),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            SelectedCollection = "movies"
        };

        await viewModel.LoadIndexDiagnosticsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IndexResults, Does.Contain("clusterMonitor permission denied"));
            Assert.That(viewModel.IndexResults, Does.Contain("1024 bytes"));
            Assert.That(viewModel.IndexResults, Does.Contain("$indexStats"));
            Assert.That(viewModel.StatusMessage, Does.Contain("3/4"));
        });
    }

    [Test]
    public async Task DiagnosticsDoNotPublishResultAfterSelectionChangesDuringRead()
    {
        using var context = new WorkspaceTestContext();
        var usageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUsage = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>([]),
            "GetIndexUsageStatsAsync" => StartUsage(),
            "GetCollectionStatsAsync" => Task.FromResult("{\"indexSizes\":{}}"),
            "GetCurrentOperationsAsync" => Task.FromResult("{\"inprog\":[]}"),
            _ => throw new InvalidOperationException(method)
        };
        Task<IReadOnlyList<string>> StartUsage()
        {
            usageStarted.TrySetResult();
            return releaseUsage.Task;
        }

        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            SelectedCollection = "movies",
            IndexResults = "conteúdo anterior"
        };
        var operation = viewModel.LoadIndexDiagnosticsCommand.ExecuteAsync(null);
        await usageStarted.Task;
        viewModel.SelectedCollection = "shows";
        releaseUsage.SetResult([]);
        await operation;

        Assert.That(viewModel.IndexResults, Is.EqualTo("conteúdo anterior"));
    }
}
