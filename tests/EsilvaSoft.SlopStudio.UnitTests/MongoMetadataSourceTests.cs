using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoMetadataSourceTests
{
    private static readonly string[] FirstThreeNames = ["a", "b", "c"];
    private static readonly string[][] OverflowBatches = [["a", "b"], ["c"], ["d", "e"]];
    private static readonly string[][] ExactBatches = [["a"], ["b", "c"]];

    [Test]
    public async Task BoundedNamesStopAtFirstOverflowAcrossCursorBatches()
    {
        using var cursor = new BatchCursor(OverflowBatches);
        var result = await MongoMetadataSource.ReadBoundedNamesAsync(cursor, 3, CancellationToken.None);
        Assert.That(result.Items, Is.EqualTo(FirstThreeNames));
        Assert.That(result.Overflow, Is.True);
        Assert.That(cursor.BatchesRead, Is.EqualTo(3));
        Assert.That(cursor.ItemsObserved, Is.EqualTo(4));
    }

    [Test]
    public async Task BoundedNamesDoNotClaimOverflowAtExactLimit()
    {
        using var cursor = new BatchCursor(ExactBatches);
        var result = await MongoMetadataSource.ReadBoundedNamesAsync(cursor, 3, CancellationToken.None);
        Assert.That(result.Items.Count, Is.EqualTo(3));
        Assert.That(result.Overflow, Is.False);
        Assert.That(cursor.BatchesRead, Is.EqualTo(2));
    }

    private sealed class BatchCursor(IReadOnlyList<IReadOnlyList<string>> batches) : IAsyncCursor<string>
    {
        private int _index = -1;
        private IReadOnlyList<string> _current = [];
        public int BatchesRead { get; private set; }
        public int ItemsObserved { get; private set; }
        public IEnumerable<string> Current => Counted();
        private IEnumerable<string> Counted()
        {
            foreach (var item in _current)
            {
                ItemsObserved++;
                yield return item;
            }
        }
        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_index >= batches.Count) return false;
            _current = batches[_index];
            BatchesRead++;
            return true;
        }
        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MoveNext(cancellationToken));
        public void Dispose() { }
    }
    [Test]
    public void SamplePipelineProjectsOnlyNamesAndTypesToTheRequestedDepth()
    {
        var pipeline = MongoMetadataSource.BuildSamplePipeline(new SchemaSampleOptions(Size: 50, Depth: 3));
        Assert.That(pipeline[0], Is.EqualTo(BsonDocument.Parse("{ $sample: { size: 50 } }")));
        var projection = pipeline[1]["$project"].AsBsonDocument;
        Assert.That(projection.Names, Is.EqualTo(Expect.Words("_id f")));
        var depth = 0;
        Visit(projection["f"], 0);
        Assert.That(depth, Is.EqualTo(3));

        // Depth is the nesting of $map bodies whose input expands a document with $objectToArray.
        void Visit(BsonValue value, int level)
        {
            if (value.IsBsonArray) { foreach (var item in value.AsBsonArray) Visit(item, level); return; }
            if (!value.IsBsonDocument) return;
            var document = value.AsBsonDocument;
            if (document.TryGetValue("input", out var input) && input.IsBsonDocument && input.AsBsonDocument.Contains("$objectToArray"))
            {
                level++;
                depth = Math.Max(depth, level);
            }
            // Emitted objects carry only the name, the $type and nested structure: no expression returns a field value.
            if (document.Contains("k")) Assert.That(document.Names, Is.SubsetOf(Expect.Words("k t c e")));
            if (document.Contains("t") && document["t"].IsBsonDocument) Assert.That(document["t"].AsBsonDocument.Names, Is.EqualTo(Expect.Words("$type")));
            foreach (var element in document) Visit(element.Value, level);
        }
    }

    [Test]
    public void SampleDocumentsParseIntoNestedFieldsAndElements()
    {
        var document = BsonDocument.Parse("""
            { "f": [ { "k": "_id", "t": "objectId" },
                     { "k": "Cliente", "t": "object", "c": [ { "k": "Nome", "t": "string" } ] },
                     { "k": "Itens", "t": "array", "e": [ { "t": "object", "c": [ { "k": "Sku", "t": "string" } ] }, { "t": "int" } ] } ] }
            """);
        var schema = new SchemaBuilder().AddSample([MongoMetadataSource.ParseSample(document)]).Build();
        Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("_id Cliente Cliente.Nome Itens Itens.Sku")));
        Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
        Assert.That(schema.Find("Cliente.Nome")!.Occurrence, Is.EqualTo(1));
    }

    [TestCase(0, 4, 2000)]
    [TestCase(100, 9, 2000)]
    [TestCase(100, 4, 0)]
    public void SampleOptionsAreBounded(int size, int depth, int maxTime) =>
        Assert.Throws<ArgumentException>(() => new SchemaSampleOptions(size, depth, maxTime).Validate());
}
