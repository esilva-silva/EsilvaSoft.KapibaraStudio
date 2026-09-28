using System.Reflection;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Facts;

/// <summary>
/// Statement semelhante: Jaccard sobre tokens do lexer MQL, limiar de 0,3, resultado inteiro ou nenhum, desempate pelo
/// mais recente — e a pureza síncrona/offline dos dois tipos novos de <c>Facts/</c>.
/// </summary>
[TestFixture]
public sealed class SimilarStatementFinderTests
{
    private static readonly string[] FactsNamespace = ["EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts"];

    [Test]
    public void IdenticalStatementsScoreOneAndAreAlwaysReturned()
    {
        const string statement = "db.pedidos.find({status: \"pago\"});";

        var match = new SimilarStatementFinder().Find(statement, [statement]);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Similarity, Is.EqualTo(1.0));
        Assert.That(match.Text, Is.EqualTo(statement));
        Assert.That(match.Index, Is.Zero);
        Assert.That(SimilarStatementFinder.Similarity(statement, statement), Is.EqualTo(1.0));
    }

    [Test]
    public void AStatementAboveTheThresholdIsReturnedWhole()
    {
        const string current = "db.pedidos.find({status: 1, loja: 2});";
        const string neighbour = "db.pedidos.find({status: 1, cliente: 3}).sort({data: -1});";

        var finder = new SimilarStatementFinder();
        var match = finder.Find(current, [neighbour]);

        Assert.That(SimilarStatementFinder.Similarity(current, neighbour), Is.GreaterThanOrEqualTo(0.3));
        Assert.That(match, Is.Not.Null);
        // Inteiro: o que volta é o statement recebido, não um recorte dele.
        Assert.That(match!.Text, Is.EqualTo(neighbour));
    }

    [Test]
    public void BelowTheThresholdNothingComesBackInsteadOfAnAlmost()
    {
        const string current = "db.pedidos.find({status: 1});";
        const string neighbour = "printjson(ENV.get(\"regiao\"));";

        var finder = new SimilarStatementFinder();

        Assert.That(SimilarStatementFinder.Similarity(current, neighbour), Is.LessThan(0.3));
        Assert.That(finder.Find(current, [neighbour]), Is.Null);
    }

    [Test]
    public void TotallyDifferentStatementsShareNothing()
    {
        Assert.That(SimilarStatementFinder.Similarity("alfa", "beta"), Is.Zero);
        Assert.That(new SimilarStatementFinder().Find("alfa", ["beta", "gama"]), Is.Null);
    }

    [Test]
    public void ExactlyOnTheThresholdCounts()
    {
        // Três tokens em comum e dez na união: 0,3 exato entra, porque o limiar é "maior ou igual".
        const string current = "a b c d e f g";
        const string neighbour = "a b c h i j";

        Assert.That(SimilarStatementFinder.Similarity(current, neighbour), Is.EqualTo(0.3));
        Assert.That(new SimilarStatementFinder().Find(current, [neighbour]), Is.Not.Null);
        Assert.That(new SimilarStatementFinder(0.31).Find(current, [neighbour]), Is.Null);
    }

    [Test]
    public void ATieIsWonByTheMostRecentCandidate()
    {
        const string current = "db.pedidos.find({status: 1});";
        const string older = "db.pedidos.find({loja: 1});";
        const string newer = "db.pedidos.find({cliente: 1});";

        var match = new SimilarStatementFinder().Find(current, [older, newer]);

        Assert.That(SimilarStatementFinder.Similarity(current, older), Is.EqualTo(SimilarStatementFinder.Similarity(current, newer)));
        Assert.That(match!.Text, Is.EqualTo(newer), "o histórico é cronológico; entre iguais vence o de maior índice");
        Assert.That(match.Index, Is.EqualTo(1));
        // A ordem inversa prova que a escolha é pela posição no histórico e não pelo texto.
        Assert.That(new SimilarStatementFinder().Find(current, [newer, older])!.Text, Is.EqualTo(older));
    }

    [Test]
    public void TheBestCandidateWinsEvenWhenItIsNotTheMostRecent()
    {
        const string current = "db.pedidos.find({status: 1, loja: 2});";
        const string best = "db.pedidos.find({status: 1, loja: 3});";
        const string weaker = "db.pedidos.count({});";

        var match = new SimilarStatementFinder().Find(current, [best, weaker]);

        Assert.That(match!.Text, Is.EqualTo(best));
    }

    [Test]
    public void StructuralPunctuationCountsAsToken()
    {
        // Um Split(' ') veria um token só de cada lado e daria similaridade zero; o lexer separa '.', '(' e '{'.
        Assert.That("db.pedidos.find({a:1});".Split(' '), Has.Length.EqualTo(1));
        Assert.That(SimilarStatementFinder.Similarity("db.pedidos.find({a:1});", "db.pedidos.find({b:1});"),
            Is.GreaterThan(0.5));
    }

    [Test]
    public void LiteralValuesAndCommentsDoNotDecideSimilarity()
    {
        const string left = "db.pedidos.find({cpf: \"11122233344\"}); // busca do dia";
        const string right = "db.pedidos.find({cpf: \"99988877766\"});";

        Assert.That(SimilarStatementFinder.Similarity(left, right), Is.EqualTo(1.0));
    }

    [Test]
    public void ATruncatedNeighbourIsNotACandidate()
    {
        var statement = "db.pedidos.find({status: 1});";
        var window = new EditorWindow(new(new(0, statement.Length), statement),
            [new EditorStatement(new(0, statement.Length), statement, truncated: true)], statement.Length);

        Assert.That(new SimilarStatementFinder().Find(window), Is.Null, "um fragmento não é um exemplo inteiro");
    }

    [Test]
    public void TheWindowOverloadUsesTheCurrentStatementAsTheQuery()
    {
        const string text = "db.pedidos.find({loja: 1});\nprintjson(1);\ndb.pedidos.find({loja: 2});";
        var snapshot = new StringTextSnapshot(text);
        var window = new EditorWindowBuilder().Build(
            new CompletionContext(new(1, 1), EditorDialects.MongoshScript, SymbolKinds.Field, "", new(text.Length, 0)), snapshot);

        var match = new SimilarStatementFinder().Find(window);

        Assert.That(window.Preceding, Has.Count.EqualTo(2));
        Assert.That(match!.Text, Is.EqualTo("db.pedidos.find({loja: 1});"));
        Assert.That(match.Index, Is.Zero);
        Assert.That(new SimilarStatementFinder().Find(EditorWindow.Empty), Is.Null);
    }

    [Test]
    public void TheSameInputAlwaysProducesTheSameAnswer()
    {
        const string current = "db.pedidos.aggregate([{$match: {status: 1}}]);";
        string?[] history = ["db.pedidos.aggregate([{$match: {loja: 1}}]);", null, "db.pedidos.aggregate([{$group: {_id: 1}}]);"];
        var finder = new SimilarStatementFinder();

        var first = finder.Find(current, history);
        var second = finder.Find(current, history);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second!.Index, Is.EqualTo(first!.Index));
        Assert.That(second.Similarity, Is.EqualTo(first.Similarity));
    }

    [Test]
    public void EmptyOrMissingInputsNeverProduceAMatch()
    {
        var finder = new SimilarStatementFinder();
        Assert.Multiple(() =>
        {
            Assert.That(finder.Find("", ["db.pedidos.find({});"]), Is.Null);
            Assert.That(finder.Find("db.pedidos.find({});", []), Is.Null);
            Assert.That(finder.Find("db.pedidos.find({});", [null, ""]), Is.Null);
            Assert.That(() => finder.Find(null!, []), Throws.ArgumentNullException);
            Assert.That(() => finder.Find("a", null!), Throws.ArgumentNullException);
            Assert.That(() => finder.Find((EditorWindow)null!), Throws.ArgumentNullException);
            Assert.That(() => new SimilarStatementFinder(1.5), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new SimilarStatementFinder().MinimumSimilarity, Is.EqualTo(SimilarStatementFinder.DefaultMinimumSimilarity));
        });
    }

    [Test]
    public void ACancelledSearchStopsInsteadOfAnsweringLate()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.That(() => new SimilarStatementFinder().Find("db.pedidos.find({});", ["db.pedidos.find({});"],
            cancellationToken: source.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void TheEditorWindowAndTheSimilarityFinderAreSynchronousAndOffline()
    {
        // Mesmo guarda de pureza do lote anterior, apontado aos tipos novos: nada de I/O, rede ou Task nas assinaturas.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static bool IsForbidden(Type type)
        {
            var name = (type.IsByRef || type.IsArray ? type.GetElementType() ?? type : type).FullName ?? "";
            return name.StartsWith("System.IO.", StringComparison.Ordinal) || name.StartsWith("System.Net.", StringComparison.Ordinal)
                || name.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
                || name.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal);
        }
        Type[] types = [typeof(EditorWindow), typeof(EditorStatement), typeof(EditorWindowBuilder), typeof(EditorWindowLimits),
            typeof(SimilarStatementFinder), typeof(SimilarStatement)];
        var offenders = types
            .SelectMany(type => type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                .Concat(type.GetFields(all).Select(field => field.FieldType))
                .Concat(type.GetProperties(all).Select(property => property.PropertyType))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
                .Select(used => (type, used)))
            .Where(pair => IsForbidden(pair.used) || (pair.used.IsGenericType && pair.used.GetGenericArguments().Any(IsForbidden)))
            .Select(pair => pair.type.Name + " -> " + pair.used.FullName).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty);
            Assert.That(types.Select(type => type.Namespace).Distinct(), Is.EqualTo(FactsNamespace));
            Assert.That(types.All(type => type.Assembly == typeof(AiFact).Assembly), Is.True);
        });
    }
}
