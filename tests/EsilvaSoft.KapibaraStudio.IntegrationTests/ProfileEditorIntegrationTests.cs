using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class ProfileEditorIntegrationTests
{
    [Test]
    public async Task ConnectionFormPersistsDirectPasswordWithReservedCharacters()
    {
        var store = new InMemoryProfileSecretStore();
        using var context = new WorkspaceTestContext(profileSecrets: store);
        var model = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            NewProfileName = "Direta", NewProfileConnectionString = "mongodb://server/database",
            NewProfileUsername = "user", NewProfilePassword = "p@ss:/?#%"
        };
        await model.SaveProfileCommand.ExecuteAsync(null);
        var profile = (await context.Repository.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(profile.ConnectionString, Is.EqualTo("mongodb://user@server/database"));
            Assert.That(profile.SecretReference, Is.Not.Null);
            Assert.That(new MongoUrl(store.Values[profile.SecretReference!]).Password, Is.EqualTo("p@ss:/?#%"));
            Assert.That(model.SelectedProfile?.ConnectionString, Is.EqualTo(profile.ConnectionString));
        });
    }

}
