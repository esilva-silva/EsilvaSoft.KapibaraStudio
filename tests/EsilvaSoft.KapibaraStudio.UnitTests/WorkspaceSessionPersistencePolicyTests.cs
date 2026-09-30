using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class WorkspaceSessionPersistencePolicyTests
{
    [TestCase(false, false, 0)]
    [TestCase(true, false, 2)]
    [TestCase(true, true, 1)]
    public void DraftOptOutAndResultProtectionApplyBeforeStorage(bool recover, bool excludeProfile, int expectedCount)
    {
        var profile = Guid.NewGuid();
        var local = new WorkspaceDraft { Text = "local draft" };
        var connected = new WorkspaceDraft { ProfileId = profile, Text = "connected draft" };
        var result = new WorkspaceDraft { ContainsResultData = true, Text = "private result" };
        var source = new WorkspaceSession
        {
            ActiveTabId = result.Id,
            Preferences = new() { RecoverDrafts = recover, ExcludedProfileIds = excludeProfile ? [profile] : [] },
            Tabs = [local, connected, result]
        };

        var persisted = WorkspaceSessionPersistencePolicy.Prepare(source);

        Assert.Multiple(() =>
        {
            Assert.That(persisted.Tabs, Has.Length.EqualTo(expectedCount));
            Assert.That(persisted.Tabs.Any(tab => tab.ContainsResultData), Is.False);
            Assert.That(persisted.ActiveTabId, Is.Null, "The active result tab must not leave a dangling persisted selection.");
            Assert.That(source.Tabs, Has.Length.EqualTo(3), "Preparing storage must not discard the live editor buffers.");
        });
    }

    [Test]
    public void AllowedActiveTabAndLanguageFallbackSurviveSnapshotPreparation()
    {
        var draft = new WorkspaceDraft { Text = "retained" };
        var snapshot = WorkspaceSessionPersistencePolicy.Prepare(new WorkspaceSession
        {
            ActiveTabId = draft.Id,
            Preferences = new() { Language = "fr" },
            Tabs = [draft]
        });
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ActiveTabId, Is.EqualTo(draft.Id));
            Assert.That(snapshot.Tabs.Single().Text, Is.EqualTo("retained"));
            Assert.That(snapshot.Preferences.Language, Is.EqualTo(ApplicationLanguages.FallbackCode));
        });
    }

    [Test]
    public void InvalidFileRevisionCannotReachStorage()
    {
        var source = new WorkspaceSession { Tabs = [new WorkspaceDraft { FileRevisionLength = 10 }] };
        Assert.Throws<InvalidDataException>(() => WorkspaceSessionPersistencePolicy.Prepare(source));
    }
}
