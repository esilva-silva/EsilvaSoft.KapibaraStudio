using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Jint;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LanguageDefinitionTests
{
    // Literal highlighting vocabulary before it became a projection of the language data. It must stay a subset.
    private const string PreviousFunctions = "find findOne aggregate count countDocuments estimatedDocumentCount distinct insertOne insertMany updateOne updateMany replaceOne deleteOne deleteMany bulkWrite createIndex createIndexes dropIndex dropIndexes getIndexes getIndexSpecs findOneAndUpdate findOneAndReplace findOneAndDelete renameCollection drop stats explain limit skip sort project hint collation maxTimeMS batchSize toArray forEach map getCollection getSiblingDB getDatabase getDB getCollectionNames getName print printjson show use help";
    private const string PreviousOperators = "$eq $ne $gt $gte $lt $lte $in $nin $and $or $nor $not $exists $type $regex $options $expr $elemMatch $size $all $sum $avg $min $max $push $addToSet $first $last $cond $ifNull $set $unset $inc $mul $rename $setOnInsert $pull $pullAll $pop $each $slice $position $currentDate $jsonSchema $text $where";
    private const string PreviousStages = "$match $group $project $sort $limit $skip $lookup $unwind $facet $count $set $unset $addFields $replaceRoot $replaceWith $bucket $bucketAuto $merge $out $search $searchMeta $unionWith $sample $sortByCount $geoNear $setWindowFields $densify $fill $documents $graphLookup";
    private const string PreviousAtlas = "compound must mustNot should filter text autocomplete equals range near phrase regex wildcard exists embeddedDocument moreLikeThis queryString path query value index score minimumShouldMatch";
    private const string PreviousTypes = "ObjectId ISODate NumberLong NumberInt NumberDecimal UUID CGUUID JUUID GUUID BinData Timestamp MinKey MaxKey Date RegExp Decimal128 Long Int32 Double Binary $oid $date $numberLong $numberInt $numberDecimal $numberDouble $binary $timestamp $minKey $maxKey $regularExpression $undefined $code $scope $dbPointer $symbol";
    private const string PreviousKeywords = "const let var async await if else for while do return try catch finally throw function new break continue switch case default of in this typeof instanceof void delete yield class extends true false null undefined";

    [Test]
    public void EmbeddedLanguageIsStructurallyValid() => Assert.That(LanguageDefinition.Default.Validate(), Is.Empty);

    [Test]
    public void SymbolKindFlagsFollowEnumNames()
    {
        foreach (var kind in Enum.GetValues<SymbolKind>()) Assert.That(kind.ToFlag().ToString(), Is.EqualTo(kind.ToString()));
    }

    [Test]
    public void HighlightingProjectionKeepsEveryPreviousWordWithTheSameAliases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MongoSyntaxVocabulary.Functions, Is.SupersetOf(Words(PreviousFunctions)));
            Assert.That(MongoSyntaxVocabulary.Operators, Is.SupersetOf(Words(PreviousOperators)));
            Assert.That(MongoSyntaxVocabulary.AggregationStages, Is.SupersetOf(Words(PreviousStages)));
            Assert.That(MongoSyntaxVocabulary.AtlasSearchOperators, Is.SupersetOf(Words(PreviousAtlas)));
            Assert.That(MongoSyntaxVocabulary.ExtendedJsonTypes, Is.SupersetOf(Words(PreviousTypes)));
            Assert.That(MongoSyntaxVocabulary.Keywords, Is.EquivalentTo(Words(PreviousKeywords)));
            // Roots and DSL functions drive namespace classification: they must not gain db, ENV or other roots.
            Assert.That(MongoSyntaxVocabulary.DslFunctions, Is.EquivalentTo(Words("getConnection getDatabase GetDatabase getCollection GetCollection")));
            Assert.That(MongoSyntaxVocabulary.DslRoots, Is.EquivalentTo(Words("ConnectionPool ConnectionPull")));
        });
    }

    [Test]
    public void LanguageCoversSuggestionsOfTheCurrentAutocomplete()
    {
        var names = LanguageDefinition.Default.Symbols.Select(symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
        Assert.That(Words("db getCollection getConnection ConnectionPool find findOne aggregate limit sort countDocuments insertOne updateOne deleteOne console const let function return ObjectId NumberLong NumberDecimal UUID CGUUID JUUID GUUID true false null"),
            Is.SubsetOf(names));
    }

    [Test]
    public void ConsoleSymbolsMatchTheRuntimeSurface()
    {
        using var engine = new Engine();
        engine.SetValue("__hostCall", new Func<string, string>(_ => "{}"));
        engine.SetValue("__hostOutput", new Action<string>(_ => { }));
        engine.SetValue("__hostMessage", new Action<string>(_ => { }));
        engine.SetValue("__hostEnvironment", new Func<string, string>(_ => "{}"));
        engine.SetValue("__hostUuid", new Func<string, string, string>((_, _) => "{}"));
        engine.SetValue("__hostUuidJson", new Func<string, string>(_ => "{}"));
        engine.Execute("var __profiles = [{\"id\":\"p\",\"name\":\"A\"}];");
        engine.SetValue("__primary", "p"); engine.SetValue("__database", "db"); engine.SetValue("__captureName", "__capture"); engine.SetValue("__maxDocuments", 10);
        var before = Keys(engine, "Object.getOwnPropertyNames(globalThis)").ToHashSet(StringComparer.Ordinal);
        engine.Execute(ConsoleBootstrap.Source);
        var added = Keys(engine, "Object.getOwnPropertyNames(globalThis)").Where(name => !before.Contains(name) && name != "__capture").ToArray();
        var console = LanguageDefinition.Default.Symbols.Where(symbol => symbol.Dialects.HasFlag(EditorDialects.Console)).ToArray();
        string[] Names(SymbolKind kind) => console.Where(symbol => symbol.Kind == kind).Select(symbol => symbol.Name).ToArray();
        var globals = console.Where(symbol => symbol.Kind is SymbolKind.DslRoot or SymbolKind.GlobalFunction or SymbolKind.BsonConstructor).Select(symbol => symbol.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(added, Is.SubsetOf(globals), "Toda global exposta pelo Console precisa estar na linguagem.");
            foreach (var name in globals)
                Assert.That(engine.Evaluate("typeof globalThis[" + JsonSerializer.Serialize(name) + "]").AsString(), Is.Not.EqualTo("undefined"), name);
            Assert.That(Keys(engine, "Object.keys(getConnection('A'))"), Is.EquivalentTo(Names(SymbolKind.ConnectionMethod)));
            Assert.That(Keys(engine, "Object.keys(db)"), Is.EquivalentTo(Names(SymbolKind.DatabaseMethod)));
            Assert.That(Keys(engine, "Object.keys(db.getCollection('c'))"), Is.EquivalentTo(Names(SymbolKind.CollectionMethod)));
            Assert.That(Keys(engine, "Object.keys(db.getCollection('c').find())"), Is.EquivalentTo(Names(SymbolKind.CursorMethod)));
        });
    }

    [TestCase("""{"version":2,"groups":[],"snippets":[],"shapes":{}}""")]
    [TestCase("""{"version":1,"groups":[{"kind":"Unknown","symbols":["x"]}],"snippets":[],"shapes":{}}""")]
    [TestCase("{")]
    public void MalformedLanguageDataIsRejected(string json) => Assert.Throws<InvalidDataException>(() => LanguageDefinition.Parse(json));

    [Test]
    public void ValidationReportsUnknownShapesDuplicatesAndBrokenSnippets()
    {
        var language = LanguageDefinition.Parse("""
            {"version":1,
             "groups":[{"kind":"QueryOperator","valueShape":"Missing","symbols":[["$eq","a"],["$eq","b"]]}],
             "snippets":[{"id":"s","detail":"d","body":"${1:campo"}],
             "shapes":{"Empty":{}}}
            """);
        var errors = language.Validate();
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Some.Contains("Missing"));
            Assert.That(errors, Has.Some.Contains("duplicado"));
            Assert.That(errors, Has.Some.Contains("Snippet inválido"));
            Assert.That(errors, Has.Some.Contains("Shape vazio"));
        });
    }

    private static string[] Words(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Keys(Engine engine, string expression) =>
        JsonSerializer.Deserialize<string[]>(engine.Evaluate("JSON.stringify(" + expression + ")").AsString())!;
}
