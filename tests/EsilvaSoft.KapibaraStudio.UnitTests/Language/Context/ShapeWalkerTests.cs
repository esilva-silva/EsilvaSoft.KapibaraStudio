using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class ShapeWalkerTests
{
    [Test]
    public void FindFilterExpectsFieldsAndQueryOperatorsAtItsObjectKey()
    {
        var source = "db.orders.find({ ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);
        Assert.That(result.ShapeId, Is.EqualTo("Filter"));
        Assert.That(result.ExpectedKinds & SymbolKinds.Field, Is.EqualTo(SymbolKinds.Field));
        Assert.That(result.ExpectedKinds & SymbolKinds.QueryOperator, Is.EqualTo(SymbolKinds.QueryOperator));
    }

    [Test]
    public void AggregateArrayExpectsStageFromPipelineShape()
    {
        var source = "db.orders.aggregate([ ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.ArrayElement);
        Assert.That(result.ShapeId, Is.EqualTo("Stage"));
        Assert.That(result.ExpectedKinds & SymbolKinds.AggregationStage, Is.EqualTo(SymbolKinds.AggregationStage));
    }

    [Test]
    public void FilterFieldConditionUsesOperatorObjectForNestedKey()
    {
        var source = "db.orders.find({ status: { ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);
        Assert.That(result.ShapeId, Is.EqualTo("OperatorObject"));
        Assert.That(result.ExpectedKinds, Is.EqualTo(SymbolKinds.QueryOperator));
        Assert.That(result.ParentPath, Is.EqualTo("status"));
    }

    [Test]
    public void AggregateStageUsesTheSymbolValueShape()
    {
        var source = "db.orders.aggregate([{ $lookup: { ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);

        Assert.That(result.ShapeId, Is.EqualTo("LookupBody"));
    }

    [Test]
    public void UpdateOrPipelineSelectsThePipelineVariantForAnArray()
    {
        var source = "db.orders.updateOne({}, [ { ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);

        Assert.That(result.ShapeId, Is.EqualTo("UpdateStage"));
    }

    [Test]
    public void SearchCompoundMustArrayUsesSearchOperatorShape()
    {
        var source = "db.Projetos.aggregate([ { $search: { compound: { must: [ { ";
        var result = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyKey);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShapeId, Is.EqualTo("SearchOperatorBody"));
            Assert.That(result.ExpectedKinds, Is.EqualTo(SymbolKinds.SearchOperator | SymbolKinds.SearchOption));
        });
    }
}
