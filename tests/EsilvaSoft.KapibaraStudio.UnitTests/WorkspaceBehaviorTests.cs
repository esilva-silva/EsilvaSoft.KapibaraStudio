using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class WorkspaceBehaviorTests
{
    [Test]
    public async Task DebouncePersistsLatestDraftAndOnlyOptedInInput()
    {
        using var context = new WorkspaceTestContext();
        var clock = new ManualTimeProvider();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, timeProvider: clock);
        await vm.InitializeAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SessionStatus)) saved.TrySetResult(); };
        var writesBefore = context.Repository.SessionWriteAttempts;
        vm.ActiveTab!.Text = "first";
        clock.Advance(TimeSpan.FromMilliseconds(500));
        vm.ActiveTab.Text = "latest";
        vm.ActiveTab.InputJson = "{\"parameter\":42}"; vm.ActiveTab.PersistInput = true;
        clock.Advance(TimeSpan.FromMilliseconds(749));
        Assert.That(context.Repository.SessionWriteAttempts, Is.EqualTo(writesBefore), "A última edição reinicia o debounce.");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var session = await context.Repository.LoadSessionAsync();
        Assert.That(context.Repository.SessionWriteAttempts, Is.EqualTo(writesBefore + 1));
        Assert.That(session.Tabs.Single().Text, Is.EqualTo("latest"));
        Assert.That(session.Tabs.Single().InputJson, Is.EqualTo("{\"parameter\":42}"));
    }
    [Test]
    public async Task DebouncedSaveFailureIsVisibleAndTheDraftCanBeRetried()
    {
        using var context = new WorkspaceTestContext();
        var clock = new ManualTimeProvider();
        var repository = new FailingSessionRepository();
        using var vm = new WorkspaceViewModel(context.Workspace, repository, timeProvider: clock);
        await vm.InitializeAsync();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SessionStatus) && vm.SessionStatus.Contains("não salvo", StringComparison.Ordinal)) failed.TrySetResult(); };
        vm.ActiveTab!.Text = "keep this draft";

        clock.Advance(TimeSpan.FromMilliseconds(750));
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(repository.SaveAttempts, Is.EqualTo(1));
        Assert.That(vm.ActiveTab.Text, Is.EqualTo("keep this draft"));
        repository.FailSave = false;
        await vm.SaveSessionAsync();
        Assert.That(repository.SaveAttempts, Is.EqualTo(2));
        Assert.That(vm.SessionStatus, Does.Contain("atualizados"));
    }

    [Test]
    public async Task DisposingWorkspaceCancelsPendingDebouncedSave()
    {
        using var context = new WorkspaceTestContext();
        var clock = new ManualTimeProvider();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, timeProvider: clock);
        await vm.InitializeAsync();
        var writesBefore = context.Repository.SessionWriteAttempts;
        vm.ActiveTab!.Text = "pending draft";

        vm.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.That(context.Repository.SessionWriteAttempts, Is.EqualTo(writesBefore));
    }

    [Test]
    public async Task FailedSaveIsVisibleKeepsDraftAndCanBeRetried()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository();
        using var vm = new WorkspaceViewModel(context.Workspace, repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "keep this draft";
        Assert.ThrowsAsync<IOException>(() => vm.SaveSessionAsync());
        Assert.That(vm.SessionStatus, Does.Contain("não salvo"));
        Assert.That(vm.ActiveTab.Text, Is.EqualTo("keep this draft"));
        repository.FailSave = false; await vm.SaveSessionAsync();
        Assert.That(vm.SessionStatus, Does.Contain("atualizados"));
    }

    [Test]
    public async Task UnreadableSessionIsNeverOverwrittenByAutomaticDefaults()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository { FailLoad = true };
        using var vm = new WorkspaceViewModel(context.Workspace, repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "new content";
        Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveSessionAsync());
        Assert.That(repository.SaveAttempts, Is.Zero);
        Assert.That(vm.SessionStatus, Does.Contain("recuperar"));
    }

    [Test]
    public async Task EditingProfileDisconnectsOldExplorerAndUpdatesReadOnlyPolicy()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); await vm.OpenConnectionAsync(profile); vm.BindActiveTab(profile, "loja", "clientes");
        await context.Repository.SaveAsync(profile with { ConnectionString = "mongodb://other-host", IsReadOnly = true });
        await vm.ReloadProfilesAsync();
        Assert.That(vm.Roots, Has.Count.EqualTo(1));
            Assert.That(vm.Roots[0].IsConnected, Is.False);
            Assert.That(vm.Roots[0].Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
        Assert.That(vm.ActiveTab!.IsConnected, Is.False);
        Assert.That(vm.ActiveTab.Profile!.IsReadOnly, Is.True);
        Assert.That(vm.ActiveTab.Database, Is.EqualTo("loja"));
    }

    [Test]
    public async Task FailedSelectionNeverFallsBackToExecutingWholeScript()
    {
        using var context = new WorkspaceTestContext();
        var tab = MakeTab(context, "A");
        var run = tab.ExecuteCommand.ExecuteAsync("invalid partial }");
        context.Scripts.Calls[0].Completion.SetException(new InvalidOperationException("syntax error"));
        await run;
        Assert.That(context.Scripts.Calls, Has.Count.EqualTo(1));
        Assert.That(context.Scripts.Calls[0].Script, Is.EqualTo("invalid partial }"));
        Assert.That(tab.Errors, Is.EqualTo("Operação não concluída: syntax error"));
    }

    [Test]
    public async Task TabsKeepDestinationAndResultsWhenCompletionsArriveOutOfOrder()
    {
        using var context = new WorkspaceTestContext();
        var a = MakeTab(context, "A"); var b = MakeTab(context, "B");
        var first = a.ExecuteCommand.ExecuteAsync(null); var second = b.ExecuteCommand.ExecuteAsync("slop.results.emit({ b: 2 });");
        a.Database = "edited-after-start";
        context.Scripts.Calls[1].Completion.SetResult(new(0, ["{\"b\":2}"], "B", "", TimeSpan.FromMilliseconds(5)));
        await second;
        Assert.Multiple(() =>
        {
            Assert.That(a.IsRunning, Is.True);
            Assert.That(b.Results, Does.Contain("\"b\": 2"));
            Assert.That(context.Scripts.Calls[0].Database, Is.EqualTo("A"));
            Assert.That(context.Scripts.Calls[1].Script, Is.EqualTo("slop.results.emit({ b: 2 });"));
        });
        context.Scripts.Calls[0].Completion.SetResult(new(0, ["{\"a\":1}"], "A", "", TimeSpan.FromMilliseconds(20)));
        await first;
        Assert.That(a.Results, Does.Contain("\"a\": 1"));
        Assert.That(b.Results, Does.Not.Contain("\"a\": 1"));
    }

    [Test]
    public async Task CancelOnlyAffectsItsOwnTabAndReportsUncertainEffects()
    {
        using var context = new WorkspaceTestContext();
        var a = MakeTab(context, "A"); var b = MakeTab(context, "B");
        var first = a.ExecuteCommand.ExecuteAsync(null); var second = b.ExecuteCommand.ExecuteAsync(null);
        a.CancelCommand.Execute(null); await first;
        Assert.That(a.Messages, Does.Contain("incerto"));
        Assert.That(b.IsRunning, Is.True);
        b.CancelCommand.Execute(null); await second;
    }

    [Test]
    public void ReadOnlyAndDisconnectedScriptsCannotExecute()
    {
        using var context = new WorkspaceTestContext();
        var tab = MakeTab(context, "A"); tab.Profile = tab.Profile! with { IsReadOnly = true };
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
        tab.Profile = tab.Profile with { IsReadOnly = false }; tab.IsConnected = false;
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task SessionRestoresOrderTextAndDestinationWithoutConnectingOrResults()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        using (var vm = new WorkspaceViewModel(context.Workspace, context.Repository))
        {
            await vm.InitializeAsync(); await vm.OpenConnectionAsync(profile);
            vm.BindActiveTab(profile, "loja", "clientes");
            vm.ActiveTab!.Text = "const rascunho = 1;"; vm.ActiveTab.InputJson = "{\"sensitive\":1}";
            vm.NewTabCommand.Execute(null); vm.ActiveTab!.Text = "const segundo = 2;";
            await vm.SaveSessionAsync();
        }
        using var restored = new WorkspaceViewModel(context.Workspace, context.Repository);
        await restored.InitializeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restored.Tabs, Has.Count.EqualTo(2));
            Assert.That(restored.Tabs[0].Text, Is.EqualTo("const rascunho = 1;"));
            Assert.That(restored.Tabs[0].Database, Is.EqualTo("loja"));
            Assert.That(restored.Tabs[0].InputJson, Is.EqualTo("{}"));
            Assert.That(restored.ActiveTab, Is.SameAs(restored.Tabs[1]));
            Assert.That(restored.Roots, Has.Count.EqualTo(1));
            Assert.That(restored.Roots[0].IsConnected, Is.False);
            Assert.That(restored.Roots[0].Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
            Assert.That(restored.Tabs.All(t => !t.IsConnected && t.Documents.Count == 0), Is.True);
        });
    }

    [Test]
    public async Task RemovingTabRemovesItsPersistedDraft()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); vm.ActiveTab!.Text = "discard me"; await vm.SaveSessionAsync();
        vm.RemoveTab(vm.ActiveTab); await vm.SaveSessionAsync();
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
    }

    [Test]
    public async Task ExplorerLoadsOnlyExpandedBankAndNeverExecutesQuery()
    {
        using var context = new WorkspaceTestContext();
        using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
        await vm.InitializeAsync(); await vm.OpenConnectionAsync(ConnectionProfile.Create("Local", "mongodb://localhost"));
        var bank = vm.Roots[0].Children[0]; await bank.LoadAsync();
        vm.OpenCollection(bank.Children[0]);
        Assert.That(vm.ActiveTab!.Mode, Is.EqualTo("Console"));
        Assert.That(vm.ActiveTab.Text, Does.Contain("db.getCollection("));
        Assert.That(vm.ActiveTab.Text, Is.EqualTo("db.getCollection(\"clientes\").find({}).limit(100)"));
        Assert.That(context.Scripts.Calls, Is.Empty);
        Assert.That(vm.Roots[0].Children[1].Children[0].Name, Is.EqualTo("Expandir para carregar"));
    }

    [Test]
    public void MongoshSelectsDatabaseUsingJsonLiteralWithoutChangingAuthenticationUri()
    {
        const string database = "tenant\"; throw 123; //";
        var script = MongoshScriptTemplate.BuildScript("{}", "print(db.getName());", database);
        var selection = "db = db.getSiblingDB(" + System.Text.Json.JsonSerializer.Serialize(database) + ");";
        Assert.That(script, Does.Contain(selection));
        Assert.That(script.IndexOf("db = connect(__slopUri)", StringComparison.Ordinal), Is.LessThan(script.IndexOf(selection, StringComparison.Ordinal)));
        Assert.That(script.IndexOf(selection, StringComparison.Ordinal), Is.LessThan(script.IndexOf("print(db.getName());", StringComparison.Ordinal)));
        Assert.That(script, Does.Contain("print(db.getName());"));
    }

    private static WorkspaceTabViewModel MakeTab(WorkspaceTestContext context, string name) => new(context.Workspace) { Mode = "Script", Profile = ConnectionProfile.Create(name, "mongodb://localhost"), Database = name, Text = "slop.results.emit({ ok: 1 });", IsConnected = true };
}
