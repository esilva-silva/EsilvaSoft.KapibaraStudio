using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ConsoleWorkspaceTests
{
    [Test] public async Task ConsoleTabsKeepCapturedDestinationAndCancelIndependently()
    {
        using var context = new WorkspaceTestContext();
        var aProfile = ConnectionProfile.Create("A", "mongodb://a"); var bProfile = ConnectionProfile.Create("B", "mongodb://b");
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseA = new TaskCompletionSource<QueryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseB = new TaskCompletionSource<QueryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, args) => {
            var query = (MongoQuery)args[1]!;
            if (query.Database == "a") { startedA.TrySetResult(); return responseA.Task.WaitAsync((CancellationToken)args[2]!); }
            Assert.That(query.Database, Is.EqualTo("b")); startedB.TrySetResult(); return responseB.Task.WaitAsync((CancellationToken)args[2]!);
        };
        var a = new WorkspaceTabViewModel(context.Workspace) { Profile = aProfile, Database = "a", IsConnected = true, Text = "db.Customers.find({})" };
        var b = new WorkspaceTabViewModel(context.Workspace) { Profile = bProfile, Database = "b", IsConnected = true, Text = "db.Customers.find({})" };
        var runA = a.ExecuteCommand.ExecuteAsync(null); var runB = b.ExecuteCommand.ExecuteAsync(null);
        await Task.WhenAll(startedA.Task, startedB.Task).WaitAsync(TimeSpan.FromSeconds(5));
        a.Database = "changed-after-start";
        responseB.SetResult(new(["{\"_id\":2,\"side\":\"B\"}"], TimeSpan.Zero, false)); await runB;
        Assert.That(b.Results, Does.Contain("B")); Assert.That(a.IsRunning, Is.True);
        a.CancelCommand.Execute(null); await runA;
        Assert.That(a.Status, Is.EqualTo("Cancelado")); Assert.That(b.Results, Does.Contain("B"));
        Assert.That(b.SelectedConsoleResult!.SourceProfile!.Id, Is.EqualTo(bProfile.Id));
        var entries = await context.Repository.GetConsoleHistoryAsync();
        Assert.That(entries.Single(e => e.ProfileId == aProfile.Id).Database, Is.EqualTo("a"));
    }

    [Test] public async Task LegacyDraftBecomesConsoleWithoutExecutionOrLosingQueryOptions()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var draft = new WorkspaceDraft { ProfileId = profile.Id, Mode = "Consulta JSON", Database = "db", Collection = "customer-history", Text = "{\"Active\":true}", Projection = "{\"Name\":1}", Sort = "{\"Name\":-1}", Skip = 20, Limit = 10, TargetHost = "host:27017" };
        var tab = new WorkspaceTabViewModel(context.Workspace); tab.Restore(draft, profile);
        Assert.That(tab.Mode, Is.EqualTo("Console")); Assert.That(tab.IsConnected, Is.False);
        Assert.That(tab.Text, Does.Contain(".project({\"Name\":1})").And.Contain(".sort({\"Name\":-1})").And.Contain(".skip(20).limit(10)"));
        Assert.That(tab.Context, Is.EqualTo("Dev › db")); Assert.That(tab.IsDirty, Is.True);
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Tabs = [tab.Snapshot()] });
        var restored = (await context.Repository.LoadSessionAsync()).Tabs.Single();
        Assert.That(restored.TargetHost, Is.EqualTo("host:27017")); Assert.That(restored.Text, Is.EqualTo(tab.Text));
        Assert.That(context.Scripts.Calls, Is.Empty);
    }
}
