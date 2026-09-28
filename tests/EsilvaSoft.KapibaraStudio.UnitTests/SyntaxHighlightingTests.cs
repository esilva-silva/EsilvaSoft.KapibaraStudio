using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SyntaxHighlightingTests
{
    private readonly SyntaxHighlightingService _service = new();
    private static SyntaxContext Context => new([
        new("Production", "Connect", "Projects", "AccountId_CreatedOn"),
        new("Other", "Logs", "Events")], "Production", "Connect", "Projects");
    [Test]
    public void JsonAndExtendedJsonPreserveValuesAndSeparateProperties()
    {
        const string text = "{\"Name\":\"Project\", \"n\":[10,10.5,-100,1.2e-10], \"Active\":true, \"gone\":false, \"empty\":null, \"id\":{\"$oid\":\"64d1\"}, \"date\":ISODate(\"2026-09-12\"), \"count\":NumberLong(9223372036854775807)}";
        var result = _service.Highlight(text, SyntaxLanguage.Json);
        Check(result, "\"Name\"", SyntaxTokenType.PropertyName);
        Check(result, "\"Project\"", SyntaxTokenType.String);
        foreach (var number in new[] { "10", "10.5", "-100", "1.2e-10", "9223372036854775807" }) Check(result, number, SyntaxTokenType.Number);
        Check(result, "true", SyntaxTokenType.Boolean); Check(result, "false", SyntaxTokenType.Boolean); Check(result, "null", SyntaxTokenType.Null);
        Check(result, "\"$oid\"", SyntaxTokenType.MongoType); Check(result, "ISODate", SyntaxTokenType.MongoType); Check(result, "NumberLong", SyntaxTokenType.MongoType);
        Assert.That(result.Text, Is.EqualTo(text));
    }
    [TestCase("db.Projects.find({\"Active\":true});", "find", SyntaxTokenType.MongoFunction)]
    [TestCase("db.Projects.find({CreatedOn:{$gte:ISODate('2026-01-01')}})", "$gte", SyntaxTokenType.MongoOperator)]
    [TestCase("db.Projects.aggregate([{$match:{'System.Id':'122'}},{$group:{_id:'$Account.Id',count:{$sum:1}}}]);", "$group", SyntaxTokenType.MongoStage)]
    [TestCase("db.Projects.aggregate([{$group:{count:{$sum:1}}}]);", "$sum", SyntaxTokenType.MongoOperator)]
    [TestCase("db.Projects.updateOne({},{$set:{Name:'New name'}});", "$set", SyntaxTokenType.MongoOperator)]
    [TestCase("db.Projects.aggregate([{$set:{Name:'New name'}}]);", "$set", SyntaxTokenType.MongoStage)]
    [TestCase("{$search:{compound:{filter:[{equals:{path:'System.Id',value:'122'}}]}}}", "equals", SyntaxTokenType.AtlasSearchOperator)]
    [TestCase("{text:'normal field',filter:[]}", "filter", SyntaxTokenType.PropertyName)]
    [TestCase("const regex = /^project[\\/]/i; let ratio = 10 / 2;", "/^project[\\/]/i", SyntaxTokenType.Regex)]
    [TestCase("async function run(){await db.Projects.find({});return `template`;}", "await", SyntaxTokenType.Keyword)]
    [TestCase("/* ObjectId( */\n// db.find()\nconst x = 'find';", "/* ObjectId( */", SyntaxTokenType.Comment)]
    public void MongoScriptClassifiesOnlyTokensOutsideStringsAndComments(string text, string token, SyntaxTokenType expected) =>
        Check(_service.Highlight(text, SyntaxLanguage.MongoScript, Context), token, expected);

    [TestCase("ConnectionPool.Production.Connect.Projects.find({})")]
    [TestCase("ConnectionPull.Production.Connect.Projects.find({})")]
    [TestCase("getConnection('Production').getDatabase('Connect').getCollection('Projects').find({})")]
    public void DslUsesLoadedNamespaceMetadata(string text)
    {
        var result = _service.Highlight(text, SyntaxLanguage.MongoScript, Context);
        foreach (var (name, type) in new[] { ("Production", SyntaxTokenType.Connection), ("Connect", SyntaxTokenType.Database), ("Projects", SyntaxTokenType.Collection) })
            Assert.That(result.Tokens.Any(t => t.Type == type && result.Text.Substring(t.Start, t.Length).Trim('\'') == name), Is.True, name);
        var unknown = _service.Highlight(text, SyntaxLanguage.MongoScript);
        Assert.That(unknown.Tokens.Any(t => t.Type is SyntaxTokenType.Connection or SyntaxTokenType.Database or SyntaxTokenType.Collection), Is.False);
        Check(_service.Highlight("db.Projects.dropIndex('AccountId_CreatedOn')", SyntaxLanguage.MongoScript, Context), "'AccountId_CreatedOn'", SyntaxTokenType.Index);
    }
    [Test]
    public void BracketsIgnoreStringsCommentsAndRegexAndDetectMismatches()
    {
        var result = _service.Highlight("find({x:'[', r:/[()]/, y:[1]}) // {\n]", SyntaxLanguage.MongoScript);
        Assert.That(result.Brackets.Count, Is.EqualTo(7));
        Assert.That(result.Brackets.Values.Count(v => v == -1), Is.EqualTo(1));
        Assert.That(result.Brackets[result.Text.Length - 1], Is.EqualTo(-1));
    }
    [Test]
    public void IncrementalCacheReusesSuffixAndPropagatesMultilineState()
    {
        var text = string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\"},", 5000));
        var original = _service.Highlight(text, SyntaxLanguage.Json);
        var changed = "{\"Name\":\"Updated\"},\n" + text[(text.IndexOf('\n') + 1)..];
        var result = _service.Highlight(changed, SyntaxLanguage.Json, previous: original);
        Assert.That(result.TokenizedLines, Is.LessThan(4));
        var fresh = _service.Highlight(changed, SyntaxLanguage.Json);
        Assert.That(result.Tokens, Is.EqualTo(fresh.Tokens));
        var comment = _service.Highlight("/*\n" + text + "\n*/", SyntaxLanguage.Json, previous: result);
        Assert.That(comment.Tokens.All(t => t.Type == SyntaxTokenType.Comment), Is.True);
        var recovered = _service.Highlight(changed, SyntaxLanguage.Json, previous: comment);
        Assert.That(recovered.Tokens, Is.EqualTo(fresh.Tokens));
    }
    [Test]
    public void CancellationAndIndependentSnapshotsDoNotCrossEditors()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => _service.Highlight(new string('x', 100000), SyntaxLanguage.MongoScript, cancellationToken: cancelled.Token));
        var a = _service.Highlight("db.Projects.find({})", SyntaxLanguage.MongoScript, Context);
        var b = _service.Highlight(a.Text, SyntaxLanguage.MongoScript, previous: a);
        Check(a, "Projects", SyntaxTokenType.Collection); Check(b, "Projects", SyntaxTokenType.Identifier);
    }
    private static void Check(SyntaxSnapshot snapshot, string text, SyntaxTokenType type) =>
        Assert.That(snapshot.Tokens.Any(t => t.Type == type && snapshot.Text.Substring(t.Start, t.Length) == text), Is.True, text + " → " + type);
}
