using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LegacyDraftRecoveryTests
{
    private static readonly string[] OfferedModes = ["Console", "Texto"];
    [TestCase("Script")]
    [TestCase("Agregação")]
    public async Task RecoveryKeepsTextOriginAndFileStateAcrossReopening(string mode)
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Origem", "mongodb://localhost");
        var draft = new WorkspaceDraft { Mode = mode, ProfileId = profile.Id, TargetHost = "node-original",
            Database = "tenant", Collection = "orders", Text = "// não executar\n[{ $match: {} }]",
            FilePath = "/recovered/original.js", FileEncoding = "Utf16LittleEndian", FileHasBom = true,
            SavedText = "original", IsDirty = true, InputJson = "{\"optin\":1}" };
        using var tab = new WorkspaceTabViewModel(context.Workspace);
        tab.Restore(draft, null);
        Assert.That(tab.Mode, Is.EqualTo("Texto"));
        Assert.That(tab.HasLegacyRecovery, Is.True);
        Assert.That(tab.LegacyRecoveryNotice, Does.Contain(mode));
        tab.Profile = profile; tab.IsConnected = true;
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
        await tab.ExecuteCommand.ExecuteAsync(null);
        Assert.That(tab.Documents, Is.Empty);
        tab.Restore(draft, null);
        var snapshot = tab.Snapshot();
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Tabs = [snapshot] });
        var persisted = (await context.Repository.LoadSessionAsync()).Tabs.Single();
        using var reopened = new WorkspaceTabViewModel(context.Workspace);
        reopened.Restore(persisted, null);
        Assert.Multiple(() =>
        {
            Assert.That(reopened.Snapshot(), Is.EqualTo(snapshot));
            Assert.That(snapshot.Text, Is.EqualTo(draft.Text));
            Assert.That(snapshot.LegacyMode, Is.EqualTo(mode));
            Assert.That(snapshot.Mode, Is.EqualTo("Texto"));
            Assert.That(snapshot.IsDirty, Is.True);
            Assert.That(snapshot.FileEncoding, Is.EqualTo(draft.FileEncoding));
            Assert.That(snapshot.FilePath, Is.EqualTo(draft.FilePath));
            Assert.That(snapshot.FileHasBom, Is.True);
            Assert.That(snapshot.TargetHost, Is.EqualTo(draft.TargetHost));
            Assert.That(snapshot.ProfileId, Is.EqualTo(draft.ProfileId));
            Assert.That(snapshot.InputJson, Is.EqualTo(draft.InputJson));
        });
        reopened.AdaptLegacyInConsoleCommand.Execute(null);
        Assert.That(reopened.HasLegacyRecovery, Is.False);
        Assert.That(reopened.Snapshot().LegacyMode, Is.EqualTo(mode));
        reopened.PersistInput = false;
        Assert.That(reopened.Snapshot().InputJson, Is.Null);
    }

    [TestCase("Script")]
    [TestCase("Agregação")]
    public void DirectAssignmentCannotReactivateRetiredExecution(string mode)
    {
        using var context = new WorkspaceTestContext();
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = mode,
            Profile = ConnectionProfile.Create("Local", "mongodb://localhost"), IsConnected = true,
            Database = "db", Collection = "orders", Text = "throw new Error('never')" };
        Assert.That(tab.ExecuteCommand.CanExecute(null), Is.False);
        Assert.That(tab.SelectableModes, Is.EquivalentTo(OfferedModes));
    }

    [Test]
    public async Task FailedConsoleSelectionNeverExecutesTheWholeDocument()
    {
        using var context = new WorkspaceTestContext();
        var calls = 0;
        context.Mongo.Handler = (_, _) => { calls++; throw new AssertionException("Fallback ao documento inteiro é proibido."); };
        using var tab = new WorkspaceTabViewModel(context.Workspace) { Mode = "Console",
            Profile = ConnectionProfile.Create("Local", "mongodb://localhost"), IsConnected = true,
            Database = "db", Text = "db.orders.find({})" };
        await tab.ExecuteCommand.ExecuteAsync("invalid partial }");
        Assert.That(calls, Is.Zero);
        Assert.That(tab.Errors, Is.Not.Empty);
        Assert.That(tab.Text, Is.EqualTo("db.orders.find({})"));
        Assert.That((await context.Repository.GetConsoleHistoryAsync()).Single().Script, Is.EqualTo("invalid partial }"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RecentFileWithPersistedInputOffersExplicitRecovery(bool hasInput)
    {
        using var context = new WorkspaceTestContext();
        const string path = "/recent.js";
        await context.Workspace.SaveScriptAsync(path, "// original file");
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        await workspace.OpenScriptHistoryAsync(new ScriptHistoryEntry(Guid.NewGuid(), path, DateTimeOffset.UtcNow, hasInput ? "{\"legacy\":1}" : null));
        var tab = workspace.ActiveTab!;
        Assert.That(tab.Text, Is.EqualTo("// original file"));
        Assert.That(tab.FilePath, Is.EqualTo(path));
        Assert.That(tab.Mode, Is.EqualTo(hasInput ? "Texto" : "Console"));
        Assert.That(tab.LegacyMode, Is.EqualTo(hasInput ? "Script" : null));
        Assert.That(tab.PersistInput, Is.EqualTo(hasInput));
        Assert.That(tab.Snapshot().InputJson, Is.EqualTo(hasInput ? "{\"legacy\":1}" : null));
        Assert.That(tab.IsRunning, Is.False);
        Assert.That(tab.IsDirty, Is.False);
    }

    [Test]
    public void RecoveryRespectsSessionAndConnectionOptOuts()
    {
        var profileId = Guid.NewGuid();
        var session = new WorkspaceSession { Tabs = [new() { Mode = "Texto", LegacyMode = "Script", ProfileId = profileId, Text = "keep privately" }] };
        Assert.That(WorkspaceSessionPersistencePolicy.Prepare(session with { Preferences = session.Preferences with { RecoverDrafts = false } }).Tabs, Is.Empty);
        Assert.That(WorkspaceSessionPersistencePolicy.Prepare(session with { Preferences = session.Preferences with { ExcludedProfileIds = [profileId] } }).Tabs, Is.Empty);
    }
}
