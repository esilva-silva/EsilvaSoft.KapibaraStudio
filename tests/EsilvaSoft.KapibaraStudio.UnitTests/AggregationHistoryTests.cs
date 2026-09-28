using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AggregationHistoryTests
{
    [Test]
    public async Task CancellationStillRecordsOnlyItsOwnExecution()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (_, args) => new TaskCompletionSource<QueryPage>().Task.WaitAsync((CancellationToken)args[2]!);
        var tab = NewTab(context);
        var running = tab.ExecuteCommand.ExecuteAsync(null);
        tab.CancelCommand.Execute(null); await running;
        var entry = (await context.Repository.GetConsoleHistoryAsync()).Single();
        Assert.That(entry.Status, Is.EqualTo("Cancelado"));
        Assert.That(entry.Mode, Is.EqualTo("Agregação"));
        Assert.That(entry.Script, Is.EqualTo("[]"));
        Assert.That(tab.Messages, Does.Contain("não são revertidos"));
    }

    [Test]
    public async Task HistoryUsesCapturedSelectionAndReopensWithoutExecuting()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host", "shop");
        await context.Repository.SaveAsync(profile);
        var reply = new TaskCompletionSource<QueryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        context.Mongo.Handler = (_, args) => { calls++; Assert.That(((AggregationQuery)args[1]!).PipelineJson, Is.EqualTo("[{ $limit: 2 }]")); return reply.Task; };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "shop", Collection = "orders", Mode = "Agregação", Text = "[{ $match: {} }]", Limit = 12 };
        var running = tab.ExecuteCommand.ExecuteAsync("[{ $limit: 2 }]");
        tab.Text = "[]"; tab.Database = "changed"; tab.Collection = "other"; tab.Limit = 1;
        reply.SetResult(new(["{\"secretResult\":42}"], TimeSpan.Zero, false)); await running;
        var entry = (await context.Repository.GetConsoleHistoryAsync()).Single();
        Assert.That(entry.Mode, Is.EqualTo("Agregação"));
        Assert.That(entry.Database, Is.EqualTo("shop")); Assert.That(entry.Collection, Is.EqualTo("orders"));
        Assert.That(entry.Script, Is.EqualTo("[{ $limit: 2 }]")); Assert.That(entry.DocumentLimit, Is.EqualTo(12));
        Assert.That(JsonSerializer.Serialize(entry), Does.Not.Contain("secretResult").And.Not.Contain("mongodb://"));
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        workspace.OpenConsoleHistory(entry);
        Assert.That(workspace.ActiveTab!.Mode, Is.EqualTo("Agregação"));
        Assert.That(workspace.ActiveTab.Collection, Is.EqualTo("orders"));
        Assert.That(workspace.ActiveTab.Text, Is.EqualTo(entry.Script)); Assert.That(workspace.ActiveTab.Limit, Is.EqualTo(12));
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(false, false, 1)]
    [TestCase(true, false, 0)]
    [TestCase(false, true, 0)]
    public async Task FailedExecutionIsRecordedOnlyWhenHistoryIsAllowed(bool optOut, bool resultData, int count)
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (_, _) => Task.FromException<QueryPage>(new ArgumentException("Estágio inválido"));
        var tab = NewTab(context); tab.HistoryEnabled = !optOut; tab.ContainsResultData = resultData;
        await tab.ExecuteCommand.ExecuteAsync(null);
        var entries = await context.Repository.GetConsoleHistoryAsync();
        Assert.That(entries, Has.Count.EqualTo(count));
        if (count > 0) Assert.That(entries.Single().Status, Is.EqualTo("Falha na execução"));
        Assert.That(tab.Errors, Does.Contain("Estágio inválido"));
    }

    [Test]
    public async Task HistoryWriteFailureIsVisibleWithoutLosingResult()
    {
        using var context = new WorkspaceTestContext(new UnavailableHistory());
        context.Mongo.Handler = (_, _) => Task.FromResult(new QueryPage(["{\"value\":1}"], TimeSpan.Zero, false));
        var tab = NewTab(context); await tab.ExecuteCommand.ExecuteAsync(null);
        Assert.That(tab.Messages, Does.Contain("Histórico não salvo"));
        Assert.That(tab.Results, Does.Contain("value")); Assert.That(tab.IsRunning, Is.False);
        Assert.That(tab.ResultTabIndex, Is.EqualTo(1));
    }

    [Test]
    public async Task VersionOneEntriesWithoutNewFieldsRemainReadable()
    {
        const string json = """
            {"Version":1,"Id":"00000000-0000-0000-0000-000000000001","ExecutedAt":"2026-09-14T00:00:00Z","ProfileId":"00000000-0000-0000-0000-000000000002","Connection":"Dev","Database":"shop","Environment":"Development","Script":"42","DurationMs":0,"Status":"Concluído","ConnectionsUsed":[]}
            """;
        var entry = JsonSerializer.Deserialize<ConsoleHistoryEntry>(json)!;
        using var context = new WorkspaceTestContext(); await context.Repository.SaveConsoleHistoryAsync(entry);
        var restored = (await context.Repository.GetConsoleHistoryAsync()).Single();
        Assert.That(restored.Mode, Is.EqualTo("Console")); Assert.That(restored.DocumentLimit, Is.EqualTo(100));
        Assert.That(restored.Collection, Is.Empty);
    }

    private static WorkspaceTabViewModel NewTab(WorkspaceTestContext context) => new(context.Workspace)
    { Profile = ConnectionProfile.Create("Dev", "mongodb://host"), Database = "shop", Collection = "orders", IsConnected = true, Mode = "Agregação", Text = "[]" };

    private sealed class UnavailableHistory : IConsoleHistoryRepository
    {
        public Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default) => Task.FromException(new IOException("Disco indisponível"));
        public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConsoleHistoryEntry>>([]);
    }
}
