using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AggregationFieldInferenceTests
{
    private static readonly string[] Input = ["_id", "customer", "price", "address", "address.city", "items", "items.name"];
    private static readonly string[] Foreign = ["_id", "name", "code", "address.city"];

    [Test]
    public void GroupReplacesInputShapeAndSetPreservesItsInputForAllAssignments()
    {
        var fields = Infer("[{ $group: { _id: '$customer', total: { $sum: '$price' } } }, { $project: {");
        Assert.That(fields, Is.EquivalentTo(new List<string> { "_id", "total" }));
        fields = Infer("[{ $set: { location: '$address', address: '$customer' } }, { $match: {");
        Assert.That(fields, Does.Contain("location.city").And.Not.Contain("address.city").And.Contain("price"));
    }

    [Test]
    public void ProjectionAndUnsetRemoveFieldsAndKeepRenamedSubtrees()
    {
        var fields = Infer("[{ $project: { _id: 0, town: '$address.city', location: '$address' } }, { $match: {");
        Assert.That(fields, Is.EquivalentTo(new List<string> { "town", "location", "location.city" }));
        Assert.That(Infer("[{ $project: { _id: 1 } }, { $match: {"), Is.EquivalentTo(new List<string> { "_id" }));
        Assert.That(Infer("[{ $project: { _id: 1, price: 0 } }, { $match: {"), Does.Not.Contain("price").And.Contain("customer"));
        Assert.That(Infer("[{ $unset: ['address', 'price'] }, { $match: {"), Does.Not.Contain("address.city").And.Not.Contain("price"));
        Assert.That(Infer("[{ $project: { address: { city: 0 } } }, { $match: {"), Does.Not.Contain("address.city").And.Contain("price"));
    }

    [Test]
    public void LookupUsesForeignFieldsOnlyAtForeignPositionsAndPublishesAlias()
    {
        Assert.That(Infer("db.orders.aggregate([{ $lookup: { from: 'customers', localField: '"), Does.Contain("price").And.Not.Contain("code"));
        Assert.That(Infer("db.orders.aggregate([{ $lookup: { from: 'customers', foreignField: '"), Does.Contain("code").And.Not.Contain("price"));
        Assert.That(Infer("[{ $lookup: { from: 'customers', pipeline: [{ $project: { _id: 0, label: '$name' } }, { $match: {"), Is.EquivalentTo(new List<string> { "label" }));
        Assert.That(Infer("[{ $lookup: { from: 'customers', let: { customerId: '$customer' }, pipeline: [{ $match: { $expr: { $eq: ['$_id', '$$cu"), Does.Contain("$customerId").And.Not.Contain("customer"));
        Assert.That(Infer("[{ $lookup: { from: 'customers', as: 'joined', localField: 'customer', foreignField: '_id' } }, { $unwind: '$joined' }, { $match: {"), Does.Contain("joined.code").And.Contain("price"));
    }

    [Test]
    public void FacetBranchesAreIndependentAndOutputExposesEachBranchShape()
    {
        var fields = Infer("[{ $facet: { totals: [{ $count: 'count' }], page: [{ $project: { _id: 0, price: 1 } }] } }, { $match: {");
        Assert.That(fields, Is.EquivalentTo(new List<string> { "totals", "totals.count", "page", "page.price" }));
        Assert.That(Infer("[{ $facet: { totals: [{ $count: 'count' }], page: [{ $match: {"), Does.Contain("price").And.Not.Contain("count"));
    }

    [Test]
    public void ConstructorsAreOpaqueAndUnknownTransformationsDoNotInventFields()
    {
        Assert.That(Infer("[{ $set: { id: ObjectId('507f1f77bcf86cd799439011'), exact: NumberLong('9007199254740993') } }, { $match: {"), Does.Contain("exact").And.Contain("id"));
        Assert.That(Infer("[{ $futureTransform: {} }, { $match: {"), Is.Empty);
        Assert.That(Infer("[{ $lookup: { from: 'missing', foreignField: '"), Is.Empty);
    }

    private static IReadOnlyList<string> Infer(string prefix) => AggregationFieldInference.Infer(prefix, Input, collection => collection == "customers" ? Foreign : []);
}
