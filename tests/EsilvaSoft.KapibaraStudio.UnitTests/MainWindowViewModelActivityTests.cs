using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelActivityTests
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
    public async Task ExplicitReadShowsSanitizedCommentSessionsAndLocksAndRateLimitsRefresh()
    {
        using var context = new WorkspaceTestContext();
        var reads = 0;
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetCurrentOperationsAsync" => Read(),
            _ => throw new InvalidOperationException(method)
        };
        Task<string> Read()
        {
            reads++;
            return Task.FromResult("""
                {"source":"$currentOp","inprog":[
                  {"type":"op","opid":9,"op":"query","ns":"sample_mflix.movies","command":{"find":"movies","filter":{"email":"private@example.test"},"comment":"batch-7"},"waitingForLock":true,"locks":{"Collection":"r"}},
                  {"type":"idleSession","lsid":{"id":"private-session"},"locks":{"Global":"W"}}
                ]}
                """);
        }
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            OperationCommentFilter = "batch-7"
        };

        await viewModel.LoadCurrentOperationsCommand.ExecuteAsync(null);
        var firstResult = viewModel.AdministrationResults;
        await viewModel.LoadCurrentOperationsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(firstResult, Does.Contain("comment#"));
            Assert.That(firstResult, Does.Contain("locks=Collection:r"));
            Assert.That(firstResult, Does.Not.Contain("private@example.test")
                .And.Not.Contain("batch-7").And.Not.Contain("private-session"));
            Assert.That(firstResult, Does.Not.Contain("sessão ociosa"));
            Assert.That(viewModel.StatusMessage, Does.Contain("Aguarde"));
        });
    }

    [Test]
    public async Task DeniedReadClearsKillSnapshotAndReportsPermission()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetCurrentOperationsAsync" => throw new AdministrationReadException(
                AdministrationReadFailure.PermissionDenied, "$currentOp", new InvalidOperationException()),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost")
        };

        await viewModel.LoadCurrentOperationsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AdministrationResults, Does.Contain("inprog"));
            Assert.That(viewModel.CanKillOperation, Is.False);
        });
    }

    [Test]
    public async Task LateReplyDoesNotPublishAfterConnectionChange()
    {
        using var context = new WorkspaceTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetCurrentOperationsAsync" => Read(),
            _ => throw new InvalidOperationException(method)
        };
        Task<string> Read()
        {
            started.TrySetResult();
            return release.Task;
        }
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            AdministrationResults = "previous"
        };

        var operation = viewModel.LoadCurrentOperationsCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.SelectedProfile = ConnectionProfile.Create("second", "mongodb://localhost");
        release.SetResult("{\"inprog\":[]}");
        await operation;

        Assert.That(viewModel.AdministrationResults, Is.EqualTo("previous"));
    }

    [Test]
    public async Task KillRequiresVisibleCompleteSnapshotWithCapturedServerAndInvalidatesOnFilterChange()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetCurrentOperationsAsync" => Task.FromResult("""
                {"source":"$currentOp","inprog":[{"opid":45,"host":"server-a:27017","connectionId":5,"op":"query","ns":"db.items","command":{"find":"items","comment":"batch-7"}}]}
                """),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost")
        };

        await viewModel.LoadCurrentOperationsCommand.ExecuteAsync(null);
        viewModel.OperationIdToKill = "45";
        viewModel.OperationKillConfirmation = "45";
        Assert.That(viewModel.CanKillOperation, Is.True);

        viewModel.OperationCommentFilter = "different-batch";
        Assert.That(viewModel.CanKillOperation, Is.False);
    }
}
