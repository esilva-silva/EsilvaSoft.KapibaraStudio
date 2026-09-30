using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class WorkspaceStorageContractTests
{
    [Test]
    public async Task OldWorkspaceProfilesRemainReadableAfterSessionMigration()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Original", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { Theme = "Escuro", CodeFontSize = 18 } });
        var reloaded = (await context.Repository.GetAllAsync()).Single();
        Assert.That(reloaded.SourceGenerationId, Is.Not.Null);
        Assert.That(reloaded, Is.EqualTo(profile with { SourceGenerationId = reloaded.SourceGenerationId }));
        var preferences = (await context.Repository.LoadSessionAsync()).Preferences;
        Assert.That(preferences.Theme, Is.EqualTo("Escuro"));
        Assert.That(preferences.CodeFontSize, Is.EqualTo(18));
    }

    [Test]
    public async Task PersistencePolicyFiltersExcludedProfilesAndCanRemoveAllDrafts()
    {
        using var context = new WorkspaceTestContext();
        var excluded = Guid.NewGuid(); var retained = Guid.NewGuid();
        var session = new WorkspaceSession { Preferences = new() { ExcludedProfileIds = [excluded] }, Tabs = [new() { ProfileId = excluded, Text = "omit" }, new() { ProfileId = retained, Text = "keep" }] };
        await context.Repository.SaveSessionAsync(session);
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs.Select(t => t.Text), Is.EqualTo(Enumerable.Repeat("keep", 1)));
        await context.Repository.SaveSessionAsync(session with { Preferences = new() { RecoverDrafts = false } });
        Assert.That((await context.Repository.LoadSessionAsync()).Tabs, Is.Empty);
    }

}
