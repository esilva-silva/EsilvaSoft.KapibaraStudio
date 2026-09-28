using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SchemaLearningWorkspaceRetentionTests
{
    [Test]
    public async Task ConfirmedDdlUpdatesTheDurableLearnedSchemaNamespaceBeforePublishingCompletionInvalidation()
    {
        using var context = new WorkspaceTestContext();
        var learned = new FakeLearnedSchemaRepository();
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = (name, _) => name switch
        {
            "RenameCollectionAsync" or "DropCollectionAsync" or "DropDatabaseAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(name)
        };
        var workspace = new WorkspaceService(context.Repository, context.Repository, context.Repository, context.Repository,
            context.Repository, mongo, context.Scripts, new LocalScriptFileService(), new SessionConnectionSecretStore(),
            learnedSchemaRepository: learned);
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");

        await workspace.RenameCollectionAsync(profile, new("shop", "orders", "orders_archive"));
        await workspace.DropCollectionAsync(profile, new("shop", "orders_archive", "orders_archive"));
        await workspace.DropDatabaseAsync(profile, new("audit", "audit"));

        Assert.Multiple(() =>
        {
            Assert.That(learned.Renamed, Is.EqualTo(new[]
            {
                (LearnedSchemaKey.Create(profile.Id, "shop", "orders"), LearnedSchemaKey.Create(profile.Id, "shop", "orders_archive"))
            }));
            Assert.That(learned.Removed, Is.EqualTo(new[] { LearnedSchemaKey.Create(profile.Id, "shop", "orders_archive") }));
            Assert.That(learned.RemovedDatabases, Is.EqualTo(new[] { (profile.Id, "audit") }));
        });
    }
}
