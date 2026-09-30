using System.Reflection;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class SessionPreferenceStorageTests
{
    [TestCase("{")]
    [TestCase("{\"Version\":1,\"Preferences\":{\"Autocomplete\":{\"Version\":99}}}")]
    [TestCase("{\"Version\":1,\"Preferences\":{\"Autocomplete\":null}}")]
    public void CorruptOrFuturePreferencesCannotBeOverwritten(string json)
    {
        using var context = new WorkspaceTestContext();
        // Deliberately corrupt the synthetic fixture through its existing owner, never a second LiteDB connection.
        var database = (LiteDB.LiteDatabase)typeof(LiteDbConnectionProfileRepository).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context.Repository)!;
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
        Assert.That(async () => await context.Repository.SaveSessionAsync(new()), Throws.Exception);
        Assert.That(database.GetCollection("workspaceSession").FindById("current")["json"].AsString, Is.EqualTo(json));
    }

    [Test]
    public async Task OlderPreferencesWithoutAutocompleteGetConservativeDefaults()
    {
        using var context = new WorkspaceTestContext();
        var database = (LiteDB.LiteDatabase)typeof(LiteDbConnectionProfileRepository).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context.Repository)!;
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = "{\"Version\":1,\"Preferences\":{\"Theme\":\"Escuro\"}}" });
        var session = await context.Repository.LoadSessionAsync();
        Assert.That(session.Preferences.Theme, Is.EqualTo("Escuro"));
        Assert.That(session.Preferences.Autocomplete, Is.EqualTo(new AutocompleteSettings()));
    }

    [Test]
    public async Task SavingInvalidShortcutsIsRejectedBeforeWritingAndKeepsThePreviousSession()
    {
        using var context = new WorkspaceTestContext();
        await context.Repository.SaveSessionAsync(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } });
        var failure = Assert.CatchAsync<InvalidDataException>(() => context.Repository.SaveSessionAsync(
            new WorkspaceSession { Preferences = new() { Theme = "Claro", EditorKeyBindings = new() { Bindings = new() { ["editor.inline.dismiss"] = null! } } } }));
        Assert.That(failure!.Message, Does.Contain("lista de gestos nula para 'editor.inline.dismiss'"));
        var kept = (await context.Repository.LoadSessionAsync()).Preferences;
        Assert.That((kept.Theme, kept.EditorKeyBindings), Is.EqualTo(("Escuro", (EditorKeyBindings?)null)));
    }

    [Test]
    public async Task SessionBoundaryNormalizesInvalidLanguageToEnglishFallback()
    {
        using var context = new WorkspaceTestContext();

        await context.Repository.SaveSessionAsync(new WorkspaceSession
        {
            Preferences = new WorkspacePreferences { Language = "fr" }
        });

        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.Language,
            Is.EqualTo(ApplicationLanguages.FallbackCode));
    }

}
