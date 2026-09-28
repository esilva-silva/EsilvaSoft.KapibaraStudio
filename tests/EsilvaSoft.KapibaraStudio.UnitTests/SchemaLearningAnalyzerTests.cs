using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// <see cref="BackgroundSchemaAnalyzer"/> (lote L12): extração de estrutura/estatísticas a partir de Extended JSON
/// já materializado, sem jamais reter um valor de documento.
/// </summary>
[TestFixture]
public sealed class SchemaLearningAnalyzerTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();

    [Test]
    public void UuidBinarySubtype4IsDistinctFromString()
    {
        var uuidJson = "{\"id\":{\"$binary\":{\"base64\":\"AAECAwQFBgcICQoLDA0ODw==\",\"subType\":\"04\"}}}";
        var stringJson = "{\"id\":\"not-a-uuid-just-text\"}";
        var delta = Analyze(uuidJson, stringJson);

        var field = FieldOf(delta, "id");

        Assert.Multiple(() =>
        {
            Assert.That(field.TypeObservations.GetValueOrDefault("uuid"), Is.EqualTo(1));
            Assert.That(field.TypeObservations.GetValueOrDefault("string"), Is.EqualTo(1));
            Assert.That(field.PresentDocumentObservations, Is.EqualTo(2));
        });
    }

    [Test]
    public void UuidBinarySubtype3IsAlsoRecognizedAsUuid()
    {
        var delta = Analyze("{\"id\":{\"$binary\":{\"base64\":\"AAECAwQFBgcICQoLDA0ODw==\",\"subType\":\"03\"}}}");

        Assert.That(FieldOf(delta, "id").TypeObservations.GetValueOrDefault("uuid"), Is.EqualTo(1));
    }

    [Test]
    public void StringThatLooksLikeAUuidWithoutTheWrapperStaysString()
    {
        var delta = Analyze("{\"id\":\"550e8400-e29b-41d4-a716-446655440000\"}");

        var field = FieldOf(delta, "id");

        Assert.Multiple(() =>
        {
            Assert.That(field.TypeObservations.GetValueOrDefault("string"), Is.EqualTo(1));
            Assert.That(field.TypeObservations.ContainsKey("uuid"), Is.False);
        });
    }

    [Test]
    public void NonUuidBinarySubtypeIsBinData()
    {
        var delta = Analyze("{\"payload\":{\"$binary\":{\"base64\":\"AAECAw==\",\"subType\":\"00\"}}}");

        Assert.That(FieldOf(delta, "payload").TypeObservations.GetValueOrDefault("binData"), Is.EqualTo(1));
    }

    [Test]
    public void NullIsDistinctFromMissingAndNeitherCrashes()
    {
        var delta = Analyze("{\"a\":null}", "{}");

        var field = FieldOf(delta, "a");

        Assert.Multiple(() =>
        {
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(2), "Ambos os documentos foram analisados.");
            Assert.That(field.PresentDocumentObservations, Is.EqualTo(1), "Só o documento com o campo presente conta.");
            Assert.That(field.TypeObservations.GetValueOrDefault("null"), Is.EqualTo(1));
        });
    }

    [Test]
    public void PolymorphicFieldRecordsTheDistributionAcrossDocuments()
    {
        var delta = Analyze("{\"code\":{\"$numberInt\":\"1\"}}", "{\"code\":\"ABC\"}");

        var field = FieldOf(delta, "code");

        Assert.Multiple(() =>
        {
            Assert.That(field.TypeObservations.GetValueOrDefault("int32"), Is.EqualTo(1));
            Assert.That(field.TypeObservations.GetValueOrDefault("string"), Is.EqualTo(1));
            Assert.That(field.PresentDocumentObservations, Is.EqualTo(2));
        });
    }

    [Test]
    public void DepthBeyondTheBudgetTruncatesWithoutCrashing()
    {
        var nested = "\"leaf\"";
        for (var level = 0; level < 15; level++) nested = "{\"n\":" + nested + "}";
        var deep = "{\"root\":" + nested + "}";

        var delta = Analyze(deep);

        Assert.That(delta.IsTruncated, Is.True);
    }

    [Test]
    public void TotalNodeBudgetTruncatesAWideDocumentWithoutCrashing()
    {
        var analyzer = new BackgroundSchemaAnalyzer(new MetadataClock(), maximumTotalNodes: 5);
        var properties = string.Join(',', Enumerable.Range(0, 20).Select(index => "\"f" + index + "\":1"));
        var wide = "{" + properties + "}";

        var envelope = CreateEnvelope(wide);
        var delta = analyzer.Analyze(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(delta.IsTruncated, Is.True);
            Assert.That(delta.Fields, Has.Count.EqualTo(5));
        });
    }

    [Test]
    public void RepeatedPresenceInsideTheSameDocumentArrayCountsOnce()
    {
        var delta = Analyze("{\"items\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]}");

        var field = FieldOf(delta, "items", "sku");

        Assert.Multiple(() =>
        {
            Assert.That(field.PresentDocumentObservations, Is.EqualTo(1), "Duas ocorrências no mesmo documento contam uma presença.");
            Assert.That(field.TypeObservations.GetValueOrDefault("string"), Is.EqualTo(1), "Mesmo tipo repetido não infla o contador.");
        });
    }

    [Test]
    public void ArrayOfScalarsFeedsElementDistributionSeparatelyFromPresence()
    {
        var delta = Analyze("{\"tags\":[\"a\",1,true]}");

        var field = FieldOf(delta, "tags");

        Assert.Multiple(() =>
        {
            Assert.That(field.IsArray, Is.True);
            Assert.That(field.PresentDocumentObservations, Is.EqualTo(1));
            Assert.That(field.ArrayDocumentObservations, Is.EqualTo(1));
            Assert.That(field.ArrayElementTypeObservations.GetValueOrDefault("string"), Is.EqualTo(1));
            Assert.That(field.ArrayElementTypeObservations.GetValueOrDefault("double"), Is.EqualTo(1));
            Assert.That(field.ArrayElementTypeObservations.GetValueOrDefault("bool"), Is.EqualTo(1));
        });
    }

    [Test]
    public void OversizedArrayIsTruncatedPerFieldWithoutFailingTheBatch()
    {
        var analyzer = new BackgroundSchemaAnalyzer(new MetadataClock(), maximumArrayElementsPerField: 3);
        var elements = string.Join(',', Enumerable.Range(0, 10).Select(_ => "1"));
        var envelope = CreateEnvelope("{\"tags\":[" + elements + "]}");

        var delta = analyzer.Analyze(envelope);

        var field = FieldOf(delta, "tags");
        Assert.Multiple(() =>
        {
            Assert.That(field.ArrayTruncated, Is.True);
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(1), "O documento inteiro não falha por causa de um array grande.");
        });
    }

    [Test]
    public void OversizedDocumentIsSkippedWithoutFailingTheBatch()
    {
        var analyzer = new BackgroundSchemaAnalyzer(new MetadataClock(), maximumDocumentJsonLength: 32);
        var envelope = CreateEnvelope("{\"a\":\"" + new string('x', 100) + "\"}", "{\"a\":1}");

        var delta = analyzer.Analyze(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(delta.SkippedDocuments, Is.EqualTo(1));
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(1));
        });
    }

    [Test]
    public void MalformedJsonIsSkippedNeverThrowsAndNeverCountedAsAbsence()
    {
        var analyzer = new BackgroundSchemaAnalyzer(new MetadataClock());
        var envelope = CreateEnvelope("{not-json", "{\"a\":1}");

        SchemaObservationDelta? delta = null;
        Assert.DoesNotThrow(() => delta = analyzer.Analyze(envelope));

        Assert.Multiple(() =>
        {
            Assert.That(delta!.SkippedDocuments, Is.EqualTo(1));
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(1));
        });
    }

    [Test]
    public void SentinelValueNeverAppearsAnywhereInTheProducedDelta()
    {
        const string sentinel = "SENTINELA-VALOR-SECRETO";
        var delta = Analyze("{\"secret\":\"" + sentinel + "\",\"nested\":{\"inner\":\"" + sentinel + "\"},\"tags\":[\"" + sentinel + "\"]}");

        Assert.Multiple(() =>
        {
            AssertNoSentinel(delta.Key.Database, sentinel);
            AssertNoSentinel(delta.Key.Collection, sentinel);
            foreach (var field in delta.Fields)
            {
                AssertNoSentinel(field.Path.ToString(), sentinel);
                foreach (var typeName in field.TypeObservations.Keys) AssertNoSentinel(typeName, sentinel);
                foreach (var typeName in field.ArrayElementTypeObservations.Keys) AssertNoSentinel(typeName, sentinel);
            }
        });
    }

    [Test]
    public void FirstSeenAndLastSeenAreDeterministicFromTheInjectedClock()
    {
        var clock = new MetadataClock { Now = new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero) };
        var analyzer = new BackgroundSchemaAnalyzer(clock);
        var envelope = CreateEnvelope("{\"a\":1}");

        var delta = analyzer.Analyze(envelope);
        var field = FieldOf(delta, "a");

        Assert.Multiple(() =>
        {
            Assert.That(field.FirstSeenUtc, Is.EqualTo(clock.Now));
            Assert.That(field.LastSeenUtc, Is.EqualTo(clock.Now));
        });
    }

    [Test]
    public void FieldIdMatchesTheCanonicalPathEncodingForNestedFields()
    {
        var delta = Analyze("{\"customer\":{\"id\":1}}");

        var field = FieldOf(delta, "customer", "id");

        Assert.That(field.FieldId(), Is.EqualTo(new LearnedFieldPath(["customer", "id"]).ToCanonicalId()));
    }

    private static void AssertNoSentinel(string? candidate, string sentinel) =>
        Assert.That(candidate is null || !candidate.Contains(sentinel, StringComparison.Ordinal), Is.True, candidate);

    private static SchemaFieldObservationDelta FieldOf(SchemaObservationDelta delta, params string[] path)
    {
        var expected = new LearnedFieldPath(path);
        var field = delta.Fields.SingleOrDefault(candidate => candidate.Path.Equals(expected));
        Assert.That(field, Is.Not.Null, "Campo esperado: " + expected);
        return field!;
    }

    private static SchemaObservationDelta Analyze(params string[] documents)
    {
        var analyzer = new BackgroundSchemaAnalyzer(new MetadataClock());
        return analyzer.Analyze(CreateEnvelope(documents));
    }

    private static SchemaLearningEnvelope CreateEnvelope(params string[] documents)
    {
        var key = LearnedSchemaKey.Create(ProfileId, "shop", "orders");
        var context = SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0);
        return SchemaLearningEnvelope.Create(key, context, SchemaLearningPolicy.Default, "find", documents);
    }
}
