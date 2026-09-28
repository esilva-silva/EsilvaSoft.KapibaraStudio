using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoCompletionTargetTests
{
    [Test]
    public async Task LookupUsesRelatedCollectionResultsFromTheSameConnectionAndDatabase()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        context.Mongo.Handler = (_, args) => Task.FromResult(new QueryPage(
            ((MongoQuery)args[1]!).Collection == "customers" ? ["{\"customerCode\":1}"] : ["{\"orderTotal\":12}"], TimeSpan.Zero, false));
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "shop", Text = "db.customers.find({}); db.orders.find({});" };
        await tab.ExecuteCommand.ExecuteAsync(null);
        Assert.That(tab.GetObservedCompletionFields("db.orders.aggregate([{ $lookup: { from: 'customers', foreignField: '"), Does.Contain("customerCode").And.Not.Contain("orderTotal"));
        Assert.That(tab.GetObservedCompletionFields("db.orders.aggregate([{ $lookup: { from: 'customers', localField: '"), Does.Contain("orderTotal").And.Not.Contain("customerCode"));
    }

    [Test]
    public void BsonWrappersAreNotSuggestedAsDocumentFields()
    {
        var fields = MqlAutocompleteService.InferFieldPaths(["{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"total\":{\"$numberLong\":\"9007199254740993\"}}"]);
        Assert.That(fields, Does.Contain("_id").And.Contain("total"));
        Assert.That(fields.Any(field => field.Contains('$')), Is.False);
    }
    [TestCase("db.orders.aggregate([{ $match: {", "Dev", "shop", "orders")]
    [TestCase("db.getCollection('customer-history').find({", "Dev", "shop", "customer-history")]
    [TestCase("ConnectionPool.Prod.other.orders.aggregate([", "Prod", "other", "orders")]
    [TestCase("getConnection('Prod').getDatabase('other').getCollection('orders').aggregate([", "Prod", "other", "orders")]
    [TestCase("db.orders.find({}); db.customers.find({", "Dev", "shop", "customers")]
    public void ExplicitPathsResolveWithoutEvaluatingCode(string prefix, string connection, string database, string collection)
    {
        Assert.That(MongoCompletionTarget.Resolve(prefix, new([], "Dev", "shop")), Is.EqualTo(new SyntaxNamespace(connection, database, collection)));
    }

    [TestCase("// db.orders.aggregate([")]
    [TestCase("'db.orders.aggregate(['")]
    [TestCase("db.getCollection(variable).find({")]
    [TestCase("getConnection(variable).getDatabase('other').orders.find({")]
    [TestCase("db.orders.find({}); alias.aggregate([")]
    public void UnknownOrCommentedPathsDoNotReuseAnotherCollection(string prefix) =>
        Assert.That(MongoCompletionTarget.Resolve(prefix, new([], "Dev", "shop")), Is.Null);

    [Test]
    public async Task ResultFieldsFollowCollectionAndDiscardOldProfileSnapshot()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        context.Mongo.Handler = (_, _) => Task.FromResult(new QueryPage(["{\"orderTotal\":12,\"customer\":{\"name\":\"Ana\"}}"], TimeSpan.Zero, false));
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, IsConnected = true, Database = "shop", Collection = "orders", Mode = "Agregação", Text = "[]" };
        await tab.ExecuteCommand.ExecuteAsync(null);
        Assert.That(tab.GetObservedCompletionFields("["), Does.Contain("orderTotal").And.Contain("customer.name"));
        tab.Mode = "Console";
        Assert.That(tab.GetObservedCompletionFields("db.orders.aggregate(["), Does.Contain("orderTotal"));
        Assert.That(tab.GetObservedCompletionFields("db.customers.aggregate(["), Is.Empty);
        tab.Profile = profile with { ConnectionString = "mongodb://another-host" };
        Assert.That(tab.GetObservedCompletionFields("db.orders.aggregate(["), Is.Empty);
    }
}
