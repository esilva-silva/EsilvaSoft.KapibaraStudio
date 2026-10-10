using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ReportMergeTests : GateEvidenceFixture
{
    private static readonly string[] ExpectedRunIds = ["run-a", "run-b"];
    private static readonly string[] ExpectedOwners = ["runtime", "runtime"];
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string Gates = """
        {"schema":"kapilab-gates-v1","version":"gates-2026-10","source_hash":"abc123","gates":[{"id":"latency-p95","owner":"runtime","required":true,"metric_id":"p95_ms","operator":"lte","threshold":100}]}
        """;

    [Test]
    public void EvaluationPreservesRunIdentityInVersionedLocalReport()
    {
        var report = Evaluate(Gates, Evidence("run-a"));

        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-report-v3"));
            Assert.That(report.Identity.BuildFingerprint, Is.EqualTo("build-a"));
            Assert.That(report.Identity.PackageFingerprint, Is.EqualTo("package-a"));
            Assert.That(report.Identity.Backend, Is.EqualTo("Cpu"));
            Assert.That(report.Identity.Provider, Is.EqualTo("local"));
            Assert.That(report.Outcome, Is.EqualTo("passed"));
        });
    }

    [Test]
    public void MergeIsDeterministicAndPreservesEachOwnerAndGate()
    {
        var later = Serialize(Evaluate(Gates, Evidence("run-b")));
        var earlier = Serialize(Evaluate(Gates, Evidence("run-a")));

        var merged = ReportMerge.Merge([later, earlier]);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Schema, Is.EqualTo("kapilab-merged-report-v1"));
            Assert.That(merged.Passed, Is.True);
            Assert.That(merged.Outcome, Is.EqualTo("passed"));
            Assert.That(merged.Reports.Select(static report => report.RunId), Is.EqualTo(ExpectedRunIds));
            Assert.That(merged.Reports.SelectMany(static report => report.Gates).Select(static gate => gate.Owner),
                Is.EqualTo(ExpectedOwners));
        });
    }

    [TestCase("build_fingerprint", "build-b")]
    [TestCase("package_fingerprint", "package-b")]
    [TestCase("backend", "WinML")]
    [TestCase("provider", "ide")]
    public void RejectsMixedExecutionIdentity(string field, string value)
    {
        var first = Serialize(Evaluate(Gates, Evidence("run-a")));
        var otherEvidence = Evidence("run-b").Replace($"\"{field}\":\"{IdentityValue(field)}\"",
            $"\"{field}\":\"{value}\"", StringComparison.Ordinal);
        var second = Serialize(Evaluate(Gates, otherEvidence));

        Assert.That(() => ReportMerge.Merge([first, second]), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsDifferentGateSetEvenWhenExecutionIdentityMatches()
    {
        var first = Serialize(Evaluate(Gates, Evidence("run-a")));
        var changedGates = Gates.Replace("gates-2026-10", "gates-2026-11", StringComparison.Ordinal);
        var second = Serialize(Evaluate(changedGates, Evidence("run-b")));

        Assert.That(() => ReportMerge.Merge([first, second]), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsReportWithoutSufficientProvenance()
    {
        var report = Serialize(Evaluate(Gates, Evidence("run-a")));
        using var document = JsonDocument.Parse(report);
        var root = document.RootElement;
        var withoutIdentity = JsonSerializer.Serialize(new
        {
            schema = root.GetProperty("schema").GetString(),
            gateSetVersion = root.GetProperty("gateSetVersion").GetString(),
            gateSourceHash = root.GetProperty("gateSourceHash").GetString(),
            gateArtifactSha256 = root.GetProperty("gateArtifactSha256").GetString(),
            runId = root.GetProperty("runId").GetString(),
            qualified = true,
            complete = true,
            passed = true,
            outcome = "passed",
            gates = root.GetProperty("gates")
        });

        Assert.That(() => ReportMerge.Merge([withoutIdentity]), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("{\"status\":\"skipped\",\"qualified\":true,\"metrics\":[]}", "inconclusive", "skipped")]
    [TestCase("{\"status\":\"inconclusive\",\"qualified\":true,\"metrics\":[]}", "inconclusive", "inconclusive")]
    [TestCase("{\"status\":\"completed\",\"qualified\":false,\"metrics\":[]}", "unqualified", "unqualified")]
    public void NonPassingMemberNeverProducesApprovedAggregate(string state, string expectedOutcome, string memberOutcome)
    {
        var passed = Serialize(Evaluate(Gates, Evidence("run-a")));
        var stateEvidence = $"{{\"schema\":\"kapilab-run-evidence-v2\",\"run_id\":\"run-b\",\"identity\":{{\"build_fingerprint\":\"build-a\",\"package_fingerprint\":\"package-a\",\"backend\":\"Cpu\",\"provider\":\"local\"}},{state[1..^1]}}}";
        var nonPassing = Serialize(Evaluate(Gates, stateEvidence));

        var merged = ReportMerge.Merge([passed, nonPassing]);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Passed, Is.False);
            Assert.That(merged.Outcome, Is.EqualTo(expectedOutcome));
            Assert.That(merged.Reports.Single(report => report.RunId == "run-b").Outcome, Is.EqualTo(memberOutcome));
        });
    }

    [Test]
    public void RejectsDuplicateRunIds()
    {
        var report = Serialize(Evaluate(Gates, Evidence("run-a")));

        Assert.That(() => ReportMerge.Merge([report, report]), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsDuplicateReportPropertiesBeforeDeserialization()
    {
        var report = Serialize(Evaluate(Gates, Evidence("run-a")));
        var ambiguous = report.Insert(report.IndexOf('{') + 1, "\"passed\":false,");

        Assert.That(() => ReportMerge.Merge([ambiguous]), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsInputCountAndAggregateCharacterBudgetUsingOnlyScalarMetadata()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => ReportMerge.ValidateInputLimits(ReportMerge.MaximumReports + 1, 0),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => ReportMerge.ValidateInputLimits(2, ReportMerge.MaximumTotalCharacters + 1),
                Throws.TypeOf<InvalidDataException>());
            Assert.DoesNotThrow(() => ReportMerge.ValidateInputLimits(ReportMerge.MaximumReports,
                ReportMerge.MaximumTotalCharacters));
        });
    }

    private static string Evidence(string runId) =>
        $"{{\"schema\":\"kapilab-run-evidence-v1\",\"run_id\":\"{runId}\",\"qualified\":true,\"identity\":{{\"build_fingerprint\":\"build-a\",\"package_fingerprint\":\"package-a\",\"backend\":\"Cpu\",\"provider\":\"local\"}},\"metrics\":[{{\"id\":\"p95_ms\",\"value\":90,\"evidence_ref\":\"metrics.json#p95\",\"qualified\":true}}]}}";

    private static string IdentityValue(string field) => field switch
    {
        "build_fingerprint" => "build-a",
        "package_fingerprint" => "package-a",
        "backend" => "Cpu",
        "provider" => "local",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string Serialize(GateEvaluation.EvaluationReport report) =>
        JsonSerializer.Serialize(report, SerializerOptions);
}
