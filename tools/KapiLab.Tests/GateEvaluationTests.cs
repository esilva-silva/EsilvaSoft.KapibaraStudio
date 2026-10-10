using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class GateEvaluationTests : GateEvidenceFixture
{
    private static readonly string[] OptionalFailureStatuses = ["passed", "failed"];
    private const string Gates = """
        {"schema":"kapilab-gates-v1","version":"gates-2026-10","source_hash":"abc123","gates":[{"id":"latency-p95","owner":"lab","required":true,"metric_id":"p95_ms","operator":"lte","threshold":100}]}
        """;

    private const string QualifiedEvidence = """
        {"schema":"kapilab-run-evidence-v1","run_id":"run-1","qualified":true,"identity":{"build_fingerprint":"build-a","package_fingerprint":"package-a","backend":"Cpu","provider":"local"},"metrics":[{"id":"p95_ms","value":90,"evidence_ref":"metrics.json#p95","qualified":true}]}
        """;

    [Test]
    public void PassesOnlyWhenGateHasQualifiedEvidenceAndThresholdPasses()
    {
        var report = Evaluate(Gates, QualifiedEvidence);

        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-report-v3"));
            Assert.That(report.Complete, Is.True);
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Gates.Single().Status, Is.EqualTo("passed"));
            Assert.That(report.Gates.Single().EvidenceReferenceSha256, Is.Not.Null);
        });
    }

    [TestCase("[]", "missing-evidence")]
    [TestCase("[{\"id\":\"p95_ms\",\"value\":90,\"evidence_ref\":null,\"qualified\":true}]", "missing-evidence")]
    [TestCase("[{\"id\":\"p95_ms\",\"value\":90,\"evidence_ref\":\"metrics.json\",\"qualified\":false}]", "unqualified")]
    public void MissingOrUnqualifiedEvidenceNeverPasses(string metrics, string expectedStatus)
    {
        var evidence = QualifiedEvidence.Replace(
            "[{\"id\":\"p95_ms\",\"value\":90,\"evidence_ref\":\"metrics.json#p95\",\"qualified\":true}]", metrics,
            StringComparison.Ordinal);
        var report = Evaluate(Gates, evidence);

        Assert.That(report.Passed, Is.False);
        Assert.That(report.Gates.Single().Status, Is.EqualTo(expectedStatus));
    }

    [Test]
    public void UnqualifiedRunCannotPassEvenWhenTheNumericThresholdPasses()
    {
        var evidence = QualifiedEvidence.Replace("\"qualified\":true", "\"qualified\":false", StringComparison.Ordinal);
        var report = Evaluate(Gates, evidence);

        Assert.That(report.Passed, Is.False);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("unqualified"));
    }

    [Test]
    public void MissingGateDefinitionsAreIncompleteAndCannotPass()
    {
        var gates = Gates.Replace("[{\"id\":\"latency-p95\",\"owner\":\"lab\",\"required\":true,\"metric_id\":\"p95_ms\",\"operator\":\"lte\",\"threshold\":100}]", "[]", StringComparison.Ordinal);
        var report = Evaluate(gates, QualifiedEvidence);

        Assert.That(report.Passed, Is.False);
        Assert.That(report.Complete, Is.False);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("missing-gates"));
    }

    [Test]
    public void FailedOptionalGateDoesNotFailAQualifiedRequiredGate()
    {
        var gates = Gates.Replace(
            "\"threshold\":100}]}", "\"threshold\":100},{\"id\":\"optional-quality\",\"owner\":\"lab\",\"required\":false,\"metric_id\":\"quality\",\"operator\":\"gte\",\"threshold\":0.9}]}",
            StringComparison.Ordinal);
        var evidence = QualifiedEvidence.Replace(
            "\"qualified\":true}]}", "\"qualified\":true},{\"id\":\"quality\",\"value\":0.5,\"evidence_ref\":\"metrics.json#quality\",\"qualified\":true}]}",
            StringComparison.Ordinal);

        var report = Evaluate(gates, evidence);

        Assert.Multiple(() =>
        {
            Assert.That(report.Complete, Is.True);
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Gates.Select(gate => gate.Status), Is.EqualTo(OptionalFailureStatuses));
        });
    }

    [Test]
    public void InvalidOrDuplicateGateDefinitionsAreRejected()
    {
        var duplicate = Gates.Replace("]}", ", {\"id\":\"latency-p95\",\"owner\":\"other\",\"required\":true,\"metric_id\":\"p95_ms\",\"operator\":\"lte\",\"threshold\":100}]}", StringComparison.Ordinal);
        Assert.That(() => Evaluate(duplicate, QualifiedEvidence), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Evaluate("{}", QualifiedEvidence), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("\"required\":true,\"required\":false")]
    [TestCase("\"required\":true,\"Required\":false")]
    public void DuplicateGatePropertiesAreRejectedEvenWhenCaseDiffers(string duplicateProperties)
    {
        var gates = Gates.Replace("\"required\":true", duplicateProperties, StringComparison.Ordinal);
        Assert.That(() => Evaluate(gates, QualifiedEvidence), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void DuplicateEvidencePropertiesAreRejected()
    {
        var evidence = QualifiedEvidence.Replace("\"qualified\":true", "\"qualified\":true,\"qualified\":false", StringComparison.Ordinal);
        Assert.That(() => Evaluate(Gates, evidence), Throws.TypeOf<InvalidDataException>());
    }
}
