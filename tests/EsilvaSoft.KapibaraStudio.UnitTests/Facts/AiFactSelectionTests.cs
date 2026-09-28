using System.Globalization;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Facts;

/// <summary>
/// Seleção de fatos para a IA: escopo, determinismo, filtros, proveniência e pureza de <c>Facts/</c>.
/// O catálogo de teste responde o banco inteiro (40 coleções) a qualquer consulta — o que prova que o recorte por
/// escopo é do seletor e não uma consequência de o catálogo já vir filtrado.
/// </summary>
[TestFixture]
public sealed class AiFactSelectionTests
{
    private const string Database = "loja";
    private const string Target = "pedidos";
    private static readonly string[] Collections = [.. Enumerable.Range(0, 39)
        .Select(index => "colecao" + index.ToString("00", CultureInfo.InvariantCulture)).Append(Target).Order(StringComparer.Ordinal)];

    [Test]
    public void OnlyTheTargetCollectionProducesFactsInADatabaseWithFortyCollections()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog());

        var facts = selector.SelectFacts(Request("aba-1"));

        Assert.That(Collections, Has.Length.EqualTo(40));
        Assert.That(facts.Select(fact => fact.Scope).Where(scope => scope.Kind == AiFactScopeKind.Collection)
            .Select(scope => scope.Collection).Distinct(StringComparer.Ordinal), Is.EquivalentTo(new[] { Target }));
        Assert.That(facts.OfKind(AiFactKind.FieldSchema).Select(fact => fact.Payload.Name), Does.Contain(Target + "Campo"));
    }

    [TestCase("$lookup", "from")]
    [TestCase("$graphLookup", "from")]
    [TestCase("$unionWith", "coll")]
    [TestCase("$unionWith", "$unionWith")]
    public void ForeignCollectionsOfJoinStagesAreTheOnlyException(string stageName, string propertyName)
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog());
        var stage = new PipelineStage(stageName, new PipelineStageProperty(propertyName, PipelineStageValue.Literal("colecao07")));

        var facts = selector.SelectFacts(Request("aba-1") with { Pipeline = [stage] });

        Assert.That(facts.Where(fact => fact.Scope.Kind == AiFactScopeKind.Collection).Select(fact => fact.Scope.Collection)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), Is.EqualTo(new[] { "colecao07", Target }));
    }

    [Test]
    public void AStageThatDoesNotJoinNeverWidensTheScope()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog());
        // "colecao07" aqui é só um literal de $match; citar um nome não é ler a coleção.
        var stage = new PipelineStage("$match", new PipelineStageProperty("from", PipelineStageValue.Literal("colecao07")));

        var facts = selector.SelectFacts(Request("aba-1") with { Pipeline = [stage] });

        Assert.That(facts.Where(fact => fact.Scope.Kind == AiFactScopeKind.Collection).Select(fact => fact.Scope.Collection)
            .Distinct(StringComparer.Ordinal), Is.EquivalentTo(new[] { Target }));
    }

    [Test]
    public void TheSameRequestProducesTheSameFactsInTheSameOrder()
    {
        var catalog = new WholeDatabaseCatalog();
        var selector = new RelevantContextSelector(catalog, [new ShuffledLearnedSource()]);
        var request = Request("aba-1") with
        {
            Pipeline = [new PipelineStage("$lookup", new PipelineStageProperty("from", PipelineStageValue.Literal("colecao07")))]
        };

        var first = selector.SelectFacts(request);
        var second = selector.SelectFacts(request);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second.Select(fact => fact.Scope.Key + "|" + fact.Payload.Name).ToArray(),
            Is.EqualTo(first.Select(fact => fact.Scope.Key + "|" + fact.Payload.Name).ToArray()));
    }

    [Test]
    public void FilteringReturnsANewSetAndLeavesTheOriginalIntact()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog());
        var facts = selector.SelectFacts(Request("aba-1") with
        {
            Pipeline = [new PipelineStage("$match", new PipelineStageProperty("total", PipelineStageValue.Expression))]
        });

        var fields = facts.OfKind(AiFactKind.FieldSchema);
        var catalogOnly = facts.FromOrigin(AiFactOrigin.CatalogSchema);

        Assert.That(facts.OfKind(AiFactKind.Operator), Is.Not.Empty);
        Assert.That(fields, Is.Not.Empty);
        Assert.That(fields.All(fact => fact.Kind == AiFactKind.FieldSchema));
        Assert.That(fields.Count, Is.LessThan(facts.Count));
        Assert.That(catalogOnly.All(fact => fact.Origin == AiFactOrigin.CatalogSchema));
        Assert.That(facts.Count, Is.EqualTo(selector.SelectFacts(Request("aba-1") with
        {
            Pipeline = [new PipelineStage("$match", new PipelineStageProperty("total", PipelineStageValue.Expression))]
        }).Count), "filtrar não pode alterar o conjunto de origem");
        Assert.That(facts.Where(_ => false), Is.SameAs(AiFactSet.Empty));
    }

    [Test]
    public void EveryFactDeclaresOriginAndScope()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog(), [new ShuffledLearnedSource()]);

        var facts = selector.SelectFacts(Request("aba-1") with
        {
            Pipeline = [new PipelineStage("$group", new PipelineStageProperty("total", PipelineStageValue.Accumulator("$sum", numeric: true)))]
        });

        Assert.That(facts, Is.Not.Empty);
        Assert.That(facts.All(fact => fact.Origin != AiFactOrigin.Unspecified));
        Assert.That(facts.All(fact => fact.Scope.Key.Length > "document:".Length));
        Assert.That(facts.Select(fact => fact.Origin).Distinct(),
            Is.EquivalentTo(new[] { AiFactOrigin.CatalogSchema, AiFactOrigin.LearnedSchema, AiFactOrigin.EditorSyntax }));
        Assert.That(() => new AiFact(AiFactKind.Collection, AiFactOrigin.Unspecified, AiFactScope.ForDocument("aba-1"), new("x")),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => new AiFact(AiFactKind.AnyFieldSchema, AiFactOrigin.CatalogSchema, AiFactScope.ForDocument("aba-1"), new("x")),
            Throws.InstanceOf<ArgumentOutOfRangeException>(), "uma combinação de categorias serve para filtrar, não para descrever um fato");
    }

    [Test]
    public void TabsWithTheSameCollectionNeverShareLocalFacts()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog());
        var shapeOfFirstTab = new SchemaBuilder().AddDocuments(["{\"somenteDaAba1\":1}"]).Build();
        var first = new AiFactRequest("aba-1", Context() with { LocalSchemas = [shapeOfFirstTab] });
        var second = Request("aba-2");

        var factsOfFirst = selector.SelectFacts(first);
        var factsOfSecond = selector.SelectFacts(second);
        var factsOfFirstAgain = selector.SelectFacts(first);

        Assert.That(factsOfFirst.Select(fact => fact.Payload.Name), Does.Contain("somenteDaAba1"));
        Assert.That(factsOfSecond.Select(fact => fact.Payload.Name), Does.Not.Contain("somenteDaAba1"));
        Assert.That(factsOfSecond.Where(fact => fact.Scope.Kind == AiFactScopeKind.Document)
            .All(fact => fact.Scope.DocumentId == "aba-2"));
        Assert.That(factsOfFirst.Where(fact => fact.Scope.Kind == AiFactScopeKind.Document)
            .All(fact => fact.Scope.DocumentId == "aba-1"));
        // Os fatos de coleção se repetem por serem da mesma coleção; os da aba, não.
        Assert.That(factsOfSecond.OfKind(AiFactKind.FieldSchema).Where(fact => fact.Scope.Kind == AiFactScopeKind.Collection),
            Is.EqualTo(factsOfFirst.OfKind(AiFactKind.FieldSchema).Where(fact => fact.Scope.Kind == AiFactScopeKind.Collection)));
        Assert.That(factsOfFirstAgain, Is.EqualTo(factsOfFirst), "a seleção da segunda aba não pode ter deixado estado para trás");
    }

    [Test]
    public void TheSelectorKeepsNoMutableStateBetweenRequests()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var mutable = typeof(RelevantContextSelector).GetFields(all).Where(field => field.IsStatic && !field.IsInitOnly && !field.IsLiteral)
            .Select(field => field.Name).ToArray();
        Assert.That(mutable, Is.Empty, "um cache estático guardaria fatos de uma aba para responder a outra");
    }

    private static readonly string[] OnlyLearnedFact = ["aprendido"];

    [Test]
    public void ALearnedSourceOnlySpeaksForItsOwnOriginAndScope()
    {
        var selector = new RelevantContextSelector(new WholeDatabaseCatalog(), [new TrespassingSource()]);

        var facts = selector.SelectFacts(Request("aba-1"));

        Assert.That(facts.FromOrigin(AiFactOrigin.LearnedSchema).Select(fact => fact.Payload.Name).ToArray(), Is.EqualTo(OnlyLearnedFact));
        Assert.That(facts.Where(fact => fact.Scope.Collection == "colecao13"), Is.Empty, "a fonte não escolhe o escopo");
        Assert.That(facts.Where(fact => fact.Scope.DocumentId == "outra-aba"), Is.Empty);
    }

    [Test]
    public void ARatioEstimatorScalesLinearlyWithTheInjectedRatio()
    {
        var sparse = new TokenRatioEstimator(0.25);
        var dense = new TokenRatioEstimator(0.5);
        var text = new string('a', 400);

        Assert.Multiple(() =>
        {
            Assert.That(sparse.Estimate(text), Is.EqualTo(100));
            Assert.That(dense.Estimate(text), Is.EqualTo(200));
            Assert.That(dense.Estimate(""), Is.Zero);
            Assert.That(new TokenRatioEstimator(0.3).Estimate("abc"), Is.EqualTo(1), "a estimativa arredonda para cima");
            Assert.That(() => new TokenRatioEstimator(0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new TokenRatioEstimator(double.NaN), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ABudgetReservesGenerationAndOverheadBeforeAnyFact()
    {
        var budget = new AiBudget(4096, 256, 64);

        Assert.Multiple(() =>
        {
            Assert.That(budget.AvailableTokens, Is.EqualTo(3776));
            Assert.That(budget.Fits(3776), Is.True);
            Assert.That(budget.Fits(3777), Is.False);
            Assert.That(budget.Remaining(3800), Is.EqualTo(-24));
            Assert.That(() => new AiBudget(100, 80, 40), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new AiBudget(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void APayloadNeverCarriesDocumentsOrUnboundedText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new AiFactPayload("campo") { Values = [.. Enumerable.Range(0, AiFactPayload.MaximumValues + 1).Select(index => "t" + index)] },
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new AiFactPayload("campo") { Detail = new string('d', 1000) }.Detail, Has.Length.EqualTo(AiFactPayload.MaximumTextLength));
            Assert.That(() => new AiFactPayload("campo") { Presence = 1.5 }, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new AiFactPayload("campo") { Values = ["int"] }, Is.EqualTo(new AiFactPayload("campo") { Values = ["int"] }));
        });
    }

    [Test]
    public void EverythingUnderFactsIsSynchronousAndOffline()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static bool IsForbidden(Type type)
        {
            var name = (type.IsByRef || type.IsArray ? type.GetElementType() ?? type : type).FullName ?? "";
            return name.StartsWith("System.IO.", StringComparison.Ordinal) || name.StartsWith("System.Net.", StringComparison.Ordinal)
                || name.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
                || name.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal);
        }
        var types = typeof(AiFact).Assembly.GetTypes()
            .Where(type => type.Namespace == "EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts").ToArray();
        var offenders = types
            .SelectMany(type => type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                .Concat(type.GetFields(all).Select(field => field.FieldType))
                .Concat(type.GetProperties(all).Select(property => property.PropertyType))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
                .Select(used => (type, used)))
            .Where(pair => IsForbidden(pair.used) || (pair.used.IsGenericType && pair.used.GetGenericArguments().Any(IsForbidden)))
            .Select(pair => pair.type.Name + " -> " + pair.used.FullName).Distinct(StringComparer.Ordinal).ToArray();

        Assert.That(offenders, Is.Empty);
        Assert.That(types, Has.Length.GreaterThan(5), "o filtro por namespace precisa estar varrendo algo");
    }

    private static AiFactRequest Request(string documentId) => new(documentId, Context());

    private static CompletionContext Context() => new(new(1, 1), EditorDialects.Mql, SymbolKinds.Field, "", new(0, 0))
    {
        Scope = new(ConnectionIdentity.From(Profile), Database, Target)
    };

    private static ConnectionProfile Profile { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "local", "mongodb://localhost:27017");

    /// <summary>Catálogo que devolve o banco inteiro a qualquer consulta; recortar é responsabilidade do seletor.</summary>
    private sealed class WholeDatabaseCatalog : IKnowledgeCatalog
    {
        public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
        {
            var candidates = new List<CatalogCandidate>();
            foreach (var collection in Collections)
            {
                var scope = new CatalogScope(ConnectionIdentity.From(Profile), Database, collection);
                if (query.Kinds.HasFlag(SymbolKinds.Collection))
                    candidates.Add(new(new(collection, SymbolKind.Collection, collection, "coleção " + collection) { Scope = scope }, CatalogMatch.Prefix));
                if (!query.Kinds.HasFlag(SymbolKinds.Field)) continue;
                var schema = new SchemaBuilder().AddDocuments(["{\"_id\":1,\"" + collection + "Campo\":\"texto\"}"]).Build();
                foreach (var field in schema.Descendants())
                    candidates.Add(new(new(collection + ":" + field.Path, SymbolKind.Field, field.Name, "") { Scope = scope, Field = field }, CatalogMatch.Prefix));
            }
            return new(candidates, CatalogCompleteness.Complete);
        }
    }

    /// <summary>Fonte "aprendida" que responde fora de ordem: se o seletor for determinístico, isso não aparece.</summary>
    private sealed class ShuffledLearnedSource : IAiFactSource
    {
        private int _turn;
        public AiFactOrigin Origin => AiFactOrigin.LearnedSchema;
        public AiFactKind ProvidedKinds => AiFactKind.LearnedFieldSchema;

        public void Collect(AiFactRequest request, AiFactScope scope, ICollection<AiFact> facts, CancellationToken cancellationToken)
        {
            if (scope.Kind != AiFactScopeKind.Collection) return;
            string[] names = ["zeta", "alfa", "meio"];
            foreach (var name in _turn++ % 2 == 0 ? names : names.Reverse())
                facts.Add(new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, scope, new(name) { Confidence = 0.5 }));
        }
    }

    /// <summary>Fonte que tenta declarar outra origem, outra coleção e outra aba; o seletor descarta os três.</summary>
    private sealed class TrespassingSource : IAiFactSource
    {
        public AiFactOrigin Origin => AiFactOrigin.LearnedSchema;
        public AiFactKind ProvidedKinds => AiFactKind.LearnedFieldSchema;

        public void Collect(AiFactRequest request, AiFactScope scope, ICollection<AiFact> facts, CancellationToken cancellationToken)
        {
            if (scope.Kind != AiFactScopeKind.Collection) return;
            facts.Add(new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, scope, new("aprendido")));
            facts.Add(new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, AiFactScope.ForCollection(Database, "colecao13"), new("vizinha")));
            facts.Add(new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, AiFactScope.ForDocument("outra-aba"), new("de-outra-aba")));
            facts.Add(new(AiFactKind.Collection, AiFactOrigin.CatalogSchema, scope, new("origem-mentida")));
        }
    }
}
