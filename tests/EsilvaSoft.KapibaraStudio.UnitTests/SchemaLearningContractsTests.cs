using System.Globalization;
using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Contracts of the persisted schema learning (lote L11): canonical key encoding, envelope without retention of
/// the result set, probabilistic delta and the admission rule.
/// </summary>
[TestFixture]
public sealed class SchemaLearningContractsTests
{
    /// <summary>Chosen so the RFC 4122 big-endian order reads exactly like the textual form.</summary>
    private static readonly Guid SampleProfileId = new("00112233-4455-6677-8899-aabbccddeeff");

    private const string SampleProfileIdBigEndianHex = "00112233445566778899AABBCCDDEEFF";

    private static string Hex(byte[] value) => Convert.ToHexString(value);

    [Test]
    public void CanonicalIdMatchesTheDocumentedByteVector()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "a", "b.c");

        // 0x01 | ProfileId big-endian | len32be(1) | "a" | len32be(3) | "b.c"
        Assert.That(Hex(key.ToCanonicalId()), Is.EqualTo("01" + SampleProfileIdBigEndianHex + "00000001" + "61" + "00000003" + "622E63"));
    }

    [Test]
    public void CanonicalIdSeparatesDotsByLengthPrefix()
    {
        var left = LearnedSchemaKey.Create(SampleProfileId, "a", "b.c");
        var right = LearnedSchemaKey.Create(SampleProfileId, "a.b", "c");

        Assert.Multiple(() =>
        {
            Assert.That(Hex(right.ToCanonicalId()), Is.EqualTo("01" + SampleProfileIdBigEndianHex + "00000003" + "612E62" + "00000001" + "63"));
            Assert.That(Hex(left.ToCanonicalId()), Is.Not.EqualTo(Hex(right.ToCanonicalId())));
            Assert.That(left, Is.Not.EqualTo(right));
        });
    }

    [Test]
    public void CanonicalIdIsCaseSensitiveBecauseNamespacesAre()
    {
        var upper = LearnedSchemaKey.Create(SampleProfileId, "shop", "Orders");
        var lower = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");

        Assert.Multiple(() =>
        {
            Assert.That(Hex(upper.ToCanonicalId()), Is.Not.EqualTo(Hex(lower.ToCanonicalId())));
            Assert.That(upper, Is.Not.EqualTo(lower));
            Assert.That(upper.GetHashCode(), Is.Not.EqualTo(lower.GetHashCode()));
        });
    }

    [Test]
    public void KeyEqualityIsOrdinalAndConsistentWithTheHash()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var same = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var otherProfile = LearnedSchemaKey.Create(Guid.NewGuid(), "shop", "orders");
        var set = new HashSet<LearnedSchemaKey> { key, same, otherProfile };

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo(same));
            Assert.That(key.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(key == same, Is.True);
            Assert.That(key, Is.Not.EqualTo(otherProfile));
            Assert.That(set, Has.Count.EqualTo(2));
            Assert.That(default(LearnedSchemaKey), Is.EqualTo(default(LearnedSchemaKey)));
            Assert.That(default(LearnedSchemaKey).GetHashCode(), Is.EqualTo(default(LearnedSchemaKey).GetHashCode()));
        });
    }

    [Test]
    public void GuidByteOrderIsBigEndianAndNotToByteArrayOrder()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "d", "c");
        var encoded = key.ToCanonicalId().AsSpan(1, 16).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Hex(encoded), Is.EqualTo(SampleProfileIdBigEndianHex));
            // Guid.ToByteArray() is little-endian in the first three groups; the difference is locked by literal.
            Assert.That(Hex(SampleProfileId.ToByteArray()), Is.EqualTo("33221100554477668899AABBCCDDEEFF"));
            Assert.That(Hex(encoded), Is.Not.EqualTo(Hex(SampleProfileId.ToByteArray())));
        });
    }

    [Test]
    public void LengthPrefixIsBigEndian()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, new string('x', 258), "c");
        var length = key.ToCanonicalId().AsSpan(17, 4).ToArray();

        Assert.That(Hex(length), Is.EqualTo("00000102"));
    }

    [Test]
    public void CanonicalIdSurvivesAccentsCjkAndSurrogatePairs()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "coleção", "订单𝒳🙂");
        var id = key.ToCanonicalId();

        Assert.Multiple(() =>
        {
            // "coleção" -> 9 bytes UTF-8; "订单𝒳🙂" -> 3 + 3 + 4 + 4 = 14 bytes, two of them surrogate pairs in UTF-16.
            Assert.That(Hex(id), Is.EqualTo("01" + SampleProfileIdBigEndianHex
                + "00000009" + "636F6C65C3A7C3A36F"
                + "0000000E" + "E8AEA2E58D95F09D92B3F09F9982"));
            Assert.That(LearnedSchemaKey.TryFromCanonicalId(id, out var roundTrip), Is.True);
            Assert.That(roundTrip, Is.EqualTo(key));
            Assert.That(roundTrip.Collection.Length, Is.EqualTo(6), "Quatro runas, duas delas em pares substitutos.");
        });
    }

    [Test]
    public void CanonicalIdIsReversibleAndRejectsForeignEncodings()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var id = key.ToCanonicalId();
        var foreign = id.ToArray();
        foreign[0] = 0x02;

        Assert.Multiple(() =>
        {
            Assert.That(LearnedSchemaKey.TryFromCanonicalId(id, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(key));
            Assert.That(LearnedSchemaKey.TryFromCanonicalId(foreign, out _), Is.False);
            Assert.That(LearnedSchemaKey.TryFromCanonicalId(id.AsSpan(0, id.Length - 1), out _), Is.False);
        });
    }

    [Test]
    public void KeyCreationRejectsIncompleteIdentity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => LearnedSchemaKey.Create(Guid.Empty, "shop", "orders"), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => LearnedSchemaKey.Create(SampleProfileId, " ", "orders"), Throws.TypeOf<ArgumentException>());
            Assert.That(() => LearnedSchemaKey.Create(SampleProfileId, "shop", ""), Throws.TypeOf<ArgumentException>());
            Assert.That(default(LearnedSchemaKey).IsComplete, Is.False);
        });
    }

    [Test]
    public void FieldPathSeparatesNestedPathFromLiteralDottedName()
    {
        var nested = new LearnedFieldPath(["Customer", "Id"]);
        var literal = new LearnedFieldPath(["Customer.Id"]);

        Assert.Multiple(() =>
        {
            Assert.That(Hex(nested.ToCanonicalId()), Is.EqualTo("01" + "00000002" + "00000008" + "437573746F6D6572" + "00000002" + "4964"));
            Assert.That(Hex(literal.ToCanonicalId()), Is.EqualTo("01" + "00000001" + "0000000B" + "437573746F6D65722E4964"));
            Assert.That(nested, Is.Not.EqualTo(literal));
            Assert.That(nested.ToString(), Is.EqualTo(literal.ToString()));
            Assert.That(nested.Parent, Is.EqualTo(new LearnedFieldPath(["Customer"])));
            Assert.That(literal.Parent, Is.Null);
        });
    }

    [Test]
    public void FieldPathEqualityIsOrdinal()
    {
        var path = new LearnedFieldPath(["Items", "Sku"]);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(new LearnedFieldPath(["Items", "Sku"])));
            Assert.That(path.GetHashCode(), Is.EqualTo(new LearnedFieldPath(["Items", "Sku"]).GetHashCode()));
            Assert.That(path, Is.Not.EqualTo(new LearnedFieldPath(["items", "sku"])));
            Assert.That(path.Append("Code"), Is.EqualTo(new LearnedFieldPath(["Items", "Sku", "Code"])));
            Assert.That(() => new LearnedFieldPath([]), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void AdmissionAcceptsCompleteFindWithFullOrigin()
    {
        var admission = Evaluate("find", ResultCompleteness.Complete);

        Assert.Multiple(() =>
        {
            Assert.That(admission.IsAdmitted, Is.True);
            Assert.That(admission.Reason, Is.EqualTo(SchemaLearningRejectionReason.None));
            Assert.That(admission.Envelope!.Key, Is.EqualTo(LearnedSchemaKey.Create(SampleProfileId, "shop", "orders")));
            Assert.That(admission.Envelope.Documents, Has.Count.EqualTo(2));
            Assert.That(admission.Envelope.Completeness, Is.EqualTo(ResultCompleteness.Complete));
            Assert.That(admission.Envelope.Method, Is.EqualTo("find"));
        });
    }

    [Test]
    public void AdmissionAcceptsFindOne()
    {
        Assert.That(Evaluate("findOne", ResultCompleteness.Complete).IsAdmitted, Is.True);
    }

    [TestCase("find", ResultCompleteness.PartialProjection, SchemaLearningRejectionReason.ProjectedResult)]
    [TestCase("findOne", ResultCompleteness.PartialProjection, SchemaLearningRejectionReason.ProjectedResult)]
    [TestCase("find", ResultCompleteness.Derived, SchemaLearningRejectionReason.DerivedResult)]
    [TestCase("findOne", ResultCompleteness.Derived, SchemaLearningRejectionReason.DerivedResult)]
    [TestCase("find", ResultCompleteness.Unknown, SchemaLearningRejectionReason.UnknownCompleteness)]
    [TestCase("findOne", ResultCompleteness.Unknown, SchemaLearningRejectionReason.UnknownCompleteness)]
    [TestCase("aggregate", ResultCompleteness.Derived, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase("aggregate", ResultCompleteness.Complete, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase("countDocuments", ResultCompleteness.Complete, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase("distinct", ResultCompleteness.Complete, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase("updateOne", ResultCompleteness.Unknown, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase(null, ResultCompleteness.Complete, SchemaLearningRejectionReason.MethodNotEligible)]
    [TestCase("Find", ResultCompleteness.Complete, SchemaLearningRejectionReason.MethodNotEligible)]
    public void AdmissionRejectsWithTypedReason(string? method, ResultCompleteness completeness, SchemaLearningRejectionReason expected)
    {
        var admission = Evaluate(method, completeness);

        Assert.Multiple(() =>
        {
            Assert.That(admission.IsAdmitted, Is.False);
            Assert.That(admission.Envelope, Is.Null);
            Assert.That(admission.Reason, Is.EqualTo(expected));
        });
    }

    [TestCase(null, "shop", "orders", SchemaLearningRejectionReason.OriginWithoutProfile)]
    [TestCase("00000000-0000-0000-0000-000000000000", "shop", "orders", SchemaLearningRejectionReason.OriginWithoutProfile)]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff", null, "orders", SchemaLearningRejectionReason.OriginWithoutDatabase)]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff", "   ", "orders", SchemaLearningRejectionReason.OriginWithoutDatabase)]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff", "shop", null, SchemaLearningRejectionReason.OriginWithoutCollection)]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff", "shop", "   ", SchemaLearningRejectionReason.OriginWithoutCollection)]
    public void AdmissionRejectsIncompleteOrigin(string? profileId, string? database, string? collection, SchemaLearningRejectionReason expected)
    {
        var origin = new ResultOrigin("Console", profileId is null ? null : Guid.Parse(profileId, CultureInfo.InvariantCulture), null, database, collection);
        var set = StructuredResultSet.FromDocuments(1, origin, ["{\"a\":1}"], false, ResultCompleteness.Complete, "find");

        var admission = SchemaLearningAdmissionPolicy.Evaluate(set, Context(), SchemaLearningPolicy.Default);

        Assert.Multiple(() =>
        {
            Assert.That(admission.IsAdmitted, Is.False);
            Assert.That(admission.Reason, Is.EqualTo(expected));
        });
    }

    [Test]
    public void AdmissionRejectsEmptyPageAndDisabledCollection()
    {
        var origin = new ResultOrigin("Console", SampleProfileId, null, "shop", "orders");
        var empty = StructuredResultSet.FromDocuments(1, origin, [], false, ResultCompleteness.Complete, "find");
        var full = StructuredResultSet.FromDocuments(1, origin, ["{\"a\":1}"], false, ResultCompleteness.Complete, "find");

        Assert.Multiple(() =>
        {
            Assert.That(SchemaLearningAdmissionPolicy.Evaluate(empty, Context(), SchemaLearningPolicy.Default).Reason,
                Is.EqualTo(SchemaLearningRejectionReason.NoDocuments));
            Assert.That(SchemaLearningAdmissionPolicy.Evaluate(full, Context(), SchemaLearningPolicy.Disabled).Reason,
                Is.EqualTo(SchemaLearningRejectionReason.CollectionDisabled));
            Assert.That(SchemaLearningAdmissionPolicy.Evaluate(full, Context(), SchemaLearningPolicy.TransientOnly).IsAdmitted, Is.True,
                "Desligar persistência não impede coletar; impede gravar.");
        });
    }

    [Test]
    public void AdmissionCarriesBatchAndTruncationWithoutTheOriginObject()
    {
        var context = new SchemaLearningBatchContext(Guid.NewGuid(), Guid.NewGuid(), 3, 7, null, DateTimeOffset.UtcNow);
        var origin = new ResultOrigin("Console", SampleProfileId, null, "shop", "orders");
        var set = StructuredResultSet.FromDocuments(2, origin, ["{\"a\":1}"], true, ResultCompleteness.Complete, "find");

        var envelope = SchemaLearningAdmissionPolicy.Evaluate(set, context, SchemaLearningPolicy.Default).Envelope!;

        Assert.Multiple(() =>
        {
            Assert.That(envelope.BatchId, Is.EqualTo(context.BatchId));
            Assert.That(envelope.Context.PageSequence, Is.EqualTo(7));
            Assert.That(envelope.Context.ResultSetNumber, Is.EqualTo(3));
            Assert.That(envelope.IsTruncated, Is.True);
            Assert.That(envelope.SkippedDocuments, Is.Zero);
            Assert.That(envelope.Policy.PersistenceEnabled, Is.True);
        });
    }

    [Test]
    public void EnvelopeCopiesDocumentsInsteadOfAliasingTheCallerList()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var documents = new List<string> { "{\"a\":1}" };
        var envelope = SchemaLearningEnvelope.Create(key, Context(), SchemaLearningPolicy.Default, "find", documents);
        documents.Add("{\"b\":2}");

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Documents, Has.Count.EqualTo(1));
            Assert.That(() => SchemaLearningEnvelope.Create(default, Context(), SchemaLearningPolicy.Default, "find", documents),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void EnvelopeDoesNotRetainTheStructuredResultSet()
    {
        var (envelope, reference) = BuildEnvelopeFromDisposableResultSet();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Documents, Has.Count.EqualTo(500), "O envelope continua utilizável depois da coleta.");
            Assert.That(reference.IsAlive, Is.False, "O StructuredResultSet ficou preso pela fila de aprendizado.");
        });
        GC.KeepAlive(envelope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SchemaLearningEnvelope Envelope, WeakReference Reference) BuildEnvelopeFromDisposableResultSet()
    {
        var documents = new string[500];
        for (var index = 0; index < documents.Length; index++)
            documents[index] = "{\"a\":" + index.ToString(CultureInfo.InvariantCulture) + ",\"padding\":\"" + new string('x', 256) + "\"}";
        var origin = new ResultOrigin("Console", SampleProfileId, null, "shop", "orders");
        var set = StructuredResultSet.FromDocuments(1, origin, documents, false, ResultCompleteness.Complete, "find");
        var envelope = SchemaLearningAdmissionPolicy.Evaluate(set, Context(), SchemaLearningPolicy.Default).Envelope!;
        return (envelope, new WeakReference(set));
    }

    [Test]
    public void DeltaKeepsCountersWithoutValuesAndCopiesFields()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var seen = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(-3));
        var fields = new List<SchemaFieldObservationDelta>
        {
            new(new LearnedFieldPath(["Customer", "Id"]), 1200,
                new Dictionary<string, long>(StringComparer.Ordinal) { ["uuid"] = 1164, ["string"] = 36 }, seen, seen),
            new(new LearnedFieldPath(["Items"]), 800,
                new Dictionary<string, long>(StringComparer.Ordinal) { ["array"] = 800 }, seen, seen,
                isArray: true,
                arrayElementTypeObservations: new Dictionary<string, long>(StringComparer.Ordinal) { ["object"] = 3100 },
                arrayDocumentObservations: 800,
                arrayTruncated: true)
        };
        var delta = new SchemaObservationDelta(key, Context(), 1200, 4, true, fields);
        fields.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(delta.Fields, Has.Count.EqualTo(2));
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(1200));
            Assert.That(delta.SkippedDocuments, Is.EqualTo(4));
            Assert.That(delta.IsTruncated, Is.True);
            Assert.That(delta.FirstSeenUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(delta.LastSeenUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(delta.Fields[0].TypeObservations["uuid"], Is.EqualTo(1164));
            Assert.That(delta.Fields[1].ArrayElementTypeObservations["object"], Is.EqualTo(3100),
                "Distribuição de elementos tem denominador próprio; não soma 100% com presença.");
            Assert.That(delta.Fields[1].ArrayTruncated, Is.True);
            Assert.That(Hex(delta.Fields[0].FieldId()), Is.EqualTo(Hex(new LearnedFieldPath(["Customer", "Id"]).ToCanonicalId())));
        });
    }

    [Test]
    public void DeltaRejectsNegativeCountersAndIncompleteKey()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");

        Assert.Multiple(() =>
        {
            Assert.That(() => new SchemaObservationDelta(key, Context(), -1, 0, false, []), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new SchemaObservationDelta(key, Context(), 0, -1, false, []), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new SchemaObservationDelta(default, Context(), 0, 0, false, []), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void FieldStatisticsExposeFrequencyOnlyWithADenominator()
    {
        var types = new Dictionary<string, long>(StringComparer.Ordinal) { ["uuid"] = 1164, ["string"] = 36 };
        var path = new LearnedFieldPath(["Customer", "Id"]);
        var withDenominator = new LearnedFieldStatistics(path, 1164, 1200, types, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var withoutDenominator = new LearnedFieldStatistics(path, 0, 0, types, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(withDenominator.Frequency, Is.EqualTo(0.97).Within(0.001));
            Assert.That(withoutDenominator.Frequency, Is.Null);
        });
    }

    [Test]
    public void SnapshotKeepsObservationalCountersAndOpaqueGeneration()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var generation = Guid.NewGuid();
        var snapshot = new LearnedSchemaSnapshot(key, LearnedSchemaSnapshot.CurrentFormatVersion, 7, generation,
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, 1200, 12, 4, false, []);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FormatVersion, Is.EqualTo(1));
            Assert.That(snapshot.Revision, Is.EqualTo(7));
            Assert.That(snapshot.LastObservedGenerationId, Is.EqualTo(generation));
            Assert.That(snapshot.CompleteDocumentObservations, Is.EqualTo(1200));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(12));
            Assert.That(snapshot.LastObservedUtc.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void CommitResultDistinguishesDurabilityFromAcceptance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new SchemaCommitResult(SchemaCommitOutcome.Applied, 3, null).IsPersisted, Is.True);
            Assert.That(new SchemaCommitResult(SchemaCommitOutcome.RolledOver, 1, null).IsPersisted, Is.True);
            Assert.That(new SchemaCommitResult(SchemaCommitOutcome.AlreadyCommitted, 3, null).IsPersisted, Is.False);
            Assert.That(new SchemaCommitResult(SchemaCommitOutcome.NotPersisted, 0, null).IsPersisted, Is.False);
            Assert.That(new SchemaCommitResult(SchemaCommitOutcome.Failed, 0, "disco cheio").IsPersisted, Is.False);
        });
    }

    [Test]
    public void RepositoryContractTakesACancellationTokenOnEveryCall()
    {
        var methods = typeof(ILearnedSchemaRepository).GetMethods();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Is.Not.Empty);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                Assert.That(parameters[^1].ParameterType, Is.EqualTo(typeof(CancellationToken)), method.Name);
                Assert.That(typeof(Task).IsAssignableFrom(method.ReturnType), Is.True, method.Name);
            }
        });
    }

    [Test]
    public void LearnedContractsExposeNoConnectionOrValueSurface()
    {
        string[] forbidden =
        [
            "Uri", "ConnectionString", "Credential", "Password", "Secret", "Host", "TargetHost",
            "Fingerprint", "ConnectionIdentity", "Environment", "DocumentId", "DocumentHash",
            "Filter", "Literal", "Prompt", "CollectionKind", "ProfileName", "Json"
        ];
        Type[] contracts =
        [
            typeof(LearnedSchemaKey), typeof(SchemaLearningBatchContext), typeof(SchemaObservationDelta),
            typeof(SchemaFieldObservationDelta), typeof(LearnedSchemaSnapshot), typeof(LearnedFieldStatistics),
            typeof(LearnedFieldPath)
        ];

        Assert.Multiple(() =>
        {
            foreach (var contract in contracts)
                foreach (var property in contract.GetProperties())
                    foreach (var name in forbidden)
                        Assert.That(property.Name.Contains(name, StringComparison.OrdinalIgnoreCase), Is.False,
                            contract.Name + "." + property.Name);
        });
    }

    [Test]
    public void EnvelopeExposesJsonButNoResultSetOrOriginObject()
    {
        var properties = typeof(SchemaLearningEnvelope).GetProperties().Select(property => property.PropertyType).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(properties, Has.No.Member(typeof(StructuredResultSet)));
            Assert.That(properties, Has.No.Member(typeof(StructuredResultDocument)));
            Assert.That(properties, Has.No.Member(typeof(IReadOnlyList<StructuredResultDocument>)));
            Assert.That(properties, Has.No.Member(typeof(ResultOrigin)));
            Assert.That(properties, Has.No.Member(typeof(ConnectionProfile)));
            Assert.That(properties, Has.Member(typeof(IReadOnlyList<string>)));
        });
    }

    [Test]
    public void SourceGenerationIsNotPartOfTheIdentity()
    {
        var key = LearnedSchemaKey.Create(SampleProfileId, "shop", "orders");
        var first = new SchemaLearningBatchContext(Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var second = first with { ObservedGenerationId = Guid.NewGuid() };

        Assert.Multiple(() =>
        {
            Assert.That(typeof(LearnedSchemaKey).GetProperties().Select(property => property.Name),
                Is.EquivalentTo(new[]
                {
                    nameof(LearnedSchemaKey.ProfileId), nameof(LearnedSchemaKey.Database),
                    nameof(LearnedSchemaKey.Collection), nameof(LearnedSchemaKey.IsComplete)
                }));
            Assert.That(Hex(new SchemaObservationDelta(key, first, 1, 0, false, []).Key.ToCanonicalId()),
                Is.EqualTo(Hex(new SchemaObservationDelta(key, second, 1, 0, false, []).Key.ToCanonicalId())),
                "Trocar a geração não pode mudar o _id: isso produziria órfãos inalcançáveis.");
        });
    }

    private static SchemaLearningBatchContext Context() => SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0);

    private static SchemaLearningAdmission Evaluate(string? method, ResultCompleteness completeness)
    {
        var origin = new ResultOrigin("Console", SampleProfileId, null, "shop", "orders");
        var set = StructuredResultSet.FromDocuments(1, origin, ["{\"a\":1}", "{\"b\":2}"], false, completeness, method);
        return SchemaLearningAdmissionPolicy.Evaluate(set, Context(), SchemaLearningPolicy.Default);
    }
}
