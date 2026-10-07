using System.Text.Json;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AgentQueryExecutionCaptureTests
{
    private const string FirstDocument = "{\"tab\":\"first\",\"n\":{\"$numberLong\":\"9007199254740993\"}}";
    private const string SecondDocument = "{\"tab\":\"second\"}";

    [Test]
    public async Task ConcurrentUserQueriesCaptureTheirOwnCompletedOutputsAndNeverPersistThem()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Synthetic", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };
            await context.Repository.SaveAsync(profile);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<QueryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Mongo.Handler = (name, arguments) =>
            {
                Assert.That(name, Is.EqualTo("QueryAsync"));
                if (((MongoQuery)arguments[1]!).Collection == "first")
                {
                    entered.TrySetResult();
                    return release.Task;
                }
                return Task.FromResult(new QueryPage([SecondDocument], TimeSpan.FromMilliseconds(1), false));
            };
            using var first = new WorkspaceTabViewModel(context.Workspace)
            {
                Profile = profile, IsConnected = true, Database = "app", Text = "console.log(['first', 'log'].join('-')); db.first.find({});", HistoryEnabled = false
            };
            using var second = new WorkspaceTabViewModel(context.Workspace)
            {
                Profile = profile, IsConnected = true, Database = "app", Text = "db.second.find({});", HistoryEnabled = false
            };
            var firstRun = first.ExecuteCommand.ExecuteAsync(null);
            var reached = await Task.WhenAny(entered.Task, firstRun).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(reached, Is.SameAs(entered.Task), $"Execution ended before the query: {first.Errors}; {first.Status}");
            Assert.That(first.CaptureAgentChatSnapshot().QueryExecution, Is.Null, "An in-flight execution is not a completed result.");
            try
            {
                await second.ExecuteCommand.ExecuteAsync(null);
                var secondCapture = second.CaptureAgentChatSnapshot();
                release.TrySetResult(new([FirstDocument], TimeSpan.FromMilliseconds(2), false));
                await firstRun;
                var firstCapture = first.CaptureAgentChatSnapshot();
                Assert.Multiple(() =>
                {
                    Assert.That(firstCapture.QueryExecution, Is.Not.Null);
                    Assert.That(secondCapture.QueryExecution, Is.Not.Null);
                    Assert.That(firstCapture.QueryExecution!.TabId, Is.EqualTo(first.Id.ToString("N")));
                    Assert.That(secondCapture.QueryExecution!.TabId, Is.EqualTo(second.Id.ToString("N")));
                    Assert.That(firstCapture.QueryExecution.Results[0].ValuesEjson[0], Is.EqualTo(FirstDocument));
                    Assert.That(secondCapture.QueryExecution.Results[0].ValuesEjson[0], Is.EqualTo(SecondDocument));
                    Assert.That(firstCapture.QueryExecution.Messages, Does.Contain("first-log"));
                    Assert.That(firstCapture.QueryExecution.ExecutionId, Is.Not.EqualTo(secondCapture.QueryExecution.ExecutionId));
                    Assert.That(JsonSerializer.Serialize(first.Snapshot()), Does.Not.Contain("9007199254740993").And.Not.Contain("first-log"));
                });
                first.Database = "other";
                Assert.That(first.CaptureAgentChatSnapshot().QueryExecution, Is.Null);
                Assert.That(firstCapture.QueryExecution!.Results[0].ValuesEjson[0], Is.EqualTo(FirstDocument), "Changing the destination cannot redirect an already captured turn.");
            }
            finally { release.TrySetResult(new([], TimeSpan.Zero, false)); await firstRun; }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task FailedExecutionCapturesDiagnosticAndExplicitNewExecutionRecovers()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Synthetic", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };
            await context.Repository.SaveAsync(profile);
            context.Mongo.Handler = static (_, _) => Task.FromException<QueryPage>(new InvalidOperationException("Synthetic query failed"));
            using var tab = new WorkspaceTabViewModel(context.Workspace)
            {
                Profile = profile, IsConnected = true, Database = "app", Text = "db.items.find({});", HistoryEnabled = false
            };
            await tab.ExecuteCommand.ExecuteAsync(null);
            var failed = tab.CaptureAgentChatSnapshot().QueryExecution;
            Assert.That(failed, Is.Not.Null);
            Assert.That(failed!.ErrorCode, Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(failed.Errors, Is.Not.Empty);
            Assert.That(failed.Results, Is.Empty);
            context.Mongo.Handler = static (_, _) => Task.FromResult(new QueryPage([SecondDocument], TimeSpan.Zero, false));
            await tab.ExecuteCommand.ExecuteAsync(null);
            var recovered = tab.CaptureAgentChatSnapshot().QueryExecution;
            Assert.That(recovered!.ErrorCode, Is.Null);
            Assert.That(recovered.Results[0].ValuesEjson[0], Is.EqualTo(SecondDocument));
            Assert.That(recovered.ExecutionId, Is.Not.EqualTo(failed.ExecutionId));
            Assert.That(failed.Errors, Is.Not.Empty, "A resumed turn retains its original failure snapshot.");
            return true;
        }, CancellationToken.None);
    }
}
