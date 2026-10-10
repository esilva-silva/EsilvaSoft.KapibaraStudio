using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;
using System.Security.Cryptography;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class EvidenceArtifactVerificationTests : GateEvidenceFixture
{
    private const string Gates = """
        {"schema":"kapilab-gates-v1","version":"v1","source_hash":"source","gates":[{"id":"g","owner":"lab","required":true,"metric_id":"m","operator":"lte","threshold":100}]}
        """;

    [Test]
    public void MatchingArtifactBytesQualifyTheReferenceWithoutPublishingThePath()
    {
        var report = GateEvaluation.Evaluate(Gates, Evidence("metrics.json#p95"), Workspace);
        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-report-v3"));
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Gates.Single().EvidenceArtifactSha256, Is.EqualTo(ArtifactHash));
            Assert.That(GateEvaluation.Serialize(report), Does.Not.Contain("metrics.json"));
        });
    }

    [TestCase("{\"outer/key~name\":{\"score\":90}}", "/outer~1key~0name/score", "passed")]
    [TestCase("{\"scores\":[10,90]}", "/scores/1", "passed")]
    [TestCase("{\"scores\":[10,90]}", "/scores/01", "evidence-value-invalid")]
    [TestCase("{\"score\":null}", "/score", "evidence-value-invalid")]
    [TestCase("{\"score\":\"90\"}", "/score", "evidence-value-invalid")]
    [TestCase("{\"other\":90}", "/score", "evidence-value-invalid")]
    [TestCase("{\"score\":91}", "/score", "evidence-value-mismatch")]
    [TestCase("{\"score\":NaN}", "/score", "evidence-value-invalid")]
    [TestCase("{\"score\":90,\"score\":90}", "/score", "evidence-value-invalid")]
    [TestCase("{\"score\":90}", "score", "evidence-value-invalid")]
    public void V4RequiresAValidPointerToTheDeclaredFiniteNumber(string artifact, string jsonPointer, string expectedStatus)
    {
        var hash = WriteArtifact(artifact);
        var report = GateEvaluation.Evaluate(Gates, EvidenceV4("metrics.json", jsonPointer, hash, 90), Workspace);
        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-report-v4"));
            Assert.That(report.Gates.Single().Status, Is.EqualTo(expectedStatus));
            Assert.That(report.Passed, Is.EqualTo(expectedStatus == "passed"));
            Assert.That(GateEvaluation.Serialize(report), Does.Not.Contain("metrics.json"));
            Assert.That(GateEvaluation.Serialize(report), Does.Not.Contain(jsonPointer));
        });
    }

    [Test]
    public void V4MissingPointerCannotPass()
    {
        var hash = WriteArtifact("{\"score\":90}");
        var evidence = EvidenceV4("metrics.json", "/score", hash, 90).Replace(",\"evidence_pointer\":\"/score\"", "", StringComparison.Ordinal);
        var report = GateEvaluation.Evaluate(Gates, evidence, Workspace);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("evidence-value-invalid"));
        Assert.That(report.Passed, Is.False);
    }

    [Test]
    public void V4MalformedJsonCannotPassEvenWhenArtifactHashMatches()
    {
        var hash = WriteArtifact("{broken");
        var report = GateEvaluation.Evaluate(Gates, EvidenceV4("metrics.json", "/score", hash, 90), Workspace);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("evidence-value-invalid"));
        Assert.That(report.Passed, Is.False);
    }

    [Test]
    public void V4OversizedArtifactCannotPass()
    {
        using (var stream = new FileStream(Path.Combine(Workspace, "metrics.json"), FileMode.Open, FileAccess.Write))
            stream.SetLength(EvidenceArtifactVerification.MaximumArtifactBytes + 1);
        var report = GateEvaluation.Evaluate(Gates, EvidenceV4("metrics.json", "/score", new string('a', 64), 90), Workspace);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("evidence-integrity-failed"));
        Assert.That(report.Passed, Is.False);
    }

    [Test]
    public void ReportMergeAcceptsV4OnlyWithItsExistingContentHashProofs()
    {
        var hash = WriteArtifact("{\"score\":90}");
        var json = GateEvaluation.Serialize(GateEvaluation.Evaluate(Gates, EvidenceV4("metrics.json", "/score", hash, 90), Workspace));
        Assert.That(ReportMerge.Merge([json]).Passed, Is.True);
        Assert.That(() => ReportMerge.Merge([json.Replace(hash, "", StringComparison.Ordinal)]),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("missing.json")]
    [TestCase("missing-directory/metrics.json")]
    public void MissingArtifactIsIncompleteAndCannotPass(string reference)
    {
        var report = GateEvaluation.Evaluate(Gates, Evidence(reference), Workspace);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Complete, Is.False);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("evidence-integrity-failed"));
    }

    [Test]
    public void ChangedBytesCannotPassEvenWhenNumericThresholdPasses()
    {
        File.WriteAllText(Path.Combine(Workspace, "metrics.json"), "abd");
        var report = GateEvaluation.Evaluate(Gates, Evidence("metrics.json"), Workspace);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Gates.Single().EvidenceArtifactSha256, Is.Null);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("evidence-integrity-failed"));
    }

    [TestCase("../outside.json")]
    [TestCase("sub/../../outside.json")]
    [TestCase("C:/outside.json")]
    [TestCase("metrics.json:stream")]
    [TestCase("data/eval/blind/metrics.json")]
    public void UnsafeReferenceIsRejectedBeforeArtifactAccess(string reference)
    {
        Assert.That(() => GateEvaluation.Evaluate(Gates, Evidence(reference), Workspace),
            Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    public void MissingHashOrMissingWorkspaceCannotApproveEvidence()
    {
        var noHash = Evidence("metrics.json").Replace($",\"evidence_sha256\":\"{ArtifactHash}\"", "", StringComparison.Ordinal);
        var report = GateEvaluation.Evaluate(Gates, noHash, Workspace);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("unverified-evidence"));
        Assert.That(report.Passed, Is.False);
        Assert.That(GateEvaluation.Evaluate(Gates, Evidence("metrics.json")).Passed, Is.False);
    }

    [Test]
    public void ReportOutputCannotOverwriteItsReferencedEvidenceThroughAnAlias()
    {
        var output = Path.Combine(Workspace, "sub", "..", "metrics.json");
        Assert.That(() => GateEvaluation.Evaluate(Gates, Evidence("metrics.json"), Workspace, output),
            Throws.TypeOf<UnauthorizedAccessException>());
        Assert.That(File.ReadAllText(Path.Combine(Workspace, "metrics.json")), Is.EqualTo("abc"));
    }

    [TestCase("not-a-sha256")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaz")]
    public void InvalidExpectedHashCannotBeUsedAsEvidence(string hash)
    {
        Assert.That(() => GateEvaluation.Evaluate(Gates, Evidence("metrics.json").Replace(ArtifactHash, hash, StringComparison.Ordinal), Workspace),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ArtifactOverTheReadBudgetIsRejectedBeforeHashing()
    {
        using (var stream = new FileStream(Path.Combine(Workspace, "metrics.json"), FileMode.Open, FileAccess.Write))
            stream.SetLength(EvidenceArtifactVerification.MaximumArtifactBytes + 1);
        Assert.That(() => GateEvaluation.Evaluate(Gates, Evidence("metrics.json"), Workspace),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("kapilab-run-evidence-v1")]
    [TestCase("kapilab-run-evidence-v2")]
    public void LegacyEvidenceRemainsReadableButUnverified(string schema)
    {
        var evidence = Evidence("metrics.json").Replace("kapilab-run-evidence-v3", schema, StringComparison.Ordinal)
            .Replace($",\"evidence_sha256\":\"{ArtifactHash}\"", "", StringComparison.Ordinal);
        if (schema.EndsWith("v1", StringComparison.Ordinal))
            evidence = evidence.Replace(",\"status\":\"completed\"", "", StringComparison.Ordinal);
        var report = GateEvaluation.Evaluate(Gates, evidence, Workspace);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Complete, Is.False);
        Assert.That(report.Gates.Single().Status, Is.EqualTo("unverified-evidence"));
    }

    [Test]
    public void MergeCannotAcceptLegacyApprovalOrApprovalWithMissingContentHash()
    {
        var json = GateEvaluation.Serialize(GateEvaluation.Evaluate(Gates, Evidence("metrics.json"), Workspace));
        Assert.That(() => ReportMerge.Merge([json.Replace("kapilab-report-v3", "kapilab-report-v2", StringComparison.Ordinal)]),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ReportMerge.Merge([json.Replace(ArtifactHash, "", StringComparison.Ordinal)]),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void LegacyIncompleteReportCanBePreservedButCannotApproveMergedResult()
    {
        var noHash = Evidence("metrics.json").Replace($",\"evidence_sha256\":\"{ArtifactHash}\"", "", StringComparison.Ordinal);
        var json = GateEvaluation.Serialize(GateEvaluation.Evaluate(Gates, noHash, Workspace))
            .Replace("kapilab-report-v3", "kapilab-report-v2", StringComparison.Ordinal);
        var merged = ReportMerge.Merge([json]);
        Assert.That(merged.Passed, Is.False);
        Assert.That(merged.Outcome, Is.EqualTo("inconclusive"));
    }

    [TestCase("metrics.json", 0)]
    [TestCase("missing.json", 6)]
    [TestCase("../outside.json", 8)]
    public async Task CliMapsArtifactVerificationToExitCodesAndPublishesOnlySafeReports(string reference, int expectedExit)
    {
        File.WriteAllText(Path.Combine(Workspace, "gates.json"), Gates);
        File.WriteAllText(Path.Combine(Workspace, "evidence.json"), Evidence(reference));
        var parsed = KapiLabCommandLine.Build().Parse([
            "report", "evaluate", "--gates", "gates.json", "--evidence", "evidence.json",
            "--out", "reports/lab/report.json", "--workspace", Workspace]);
        var exit = await parsed.InvokeAsync();
        Assert.That(exit, Is.EqualTo(expectedExit));
        var reportPath = Path.Combine(Workspace, "reports", "lab", "report.json");
        Assert.That(File.Exists(reportPath), Is.EqualTo(expectedExit != 8));
        if (expectedExit == 0)
        {
            var merged = ReportMerge.Merge([File.ReadAllText(reportPath)]);
            Assert.That(merged.Passed, Is.True);
        }
    }

    private static string Evidence(string reference) =>
        $$"""
        {"schema":"kapilab-run-evidence-v3","status":"completed","run_id":"run-1","qualified":true,"identity":{"build_fingerprint":"b","package_fingerprint":"p","backend":"Cpu","provider":"local"},"metrics":[{"id":"m","value":90,"evidence_ref":"{{reference}}","evidence_sha256":"{{ArtifactHash}}","qualified":true}]}
        """;

    private static string EvidenceV4(string reference, string? jsonPointer, string hash, double value)
    {
        var pointerMember = jsonPointer is null ? "" : $",\"evidence_pointer\":{System.Text.Json.JsonSerializer.Serialize(jsonPointer)}";
        return $$"""
            {"schema":"kapilab-run-evidence-v4","status":"completed","run_id":"run-1","qualified":true,"identity":{"build_fingerprint":"b","package_fingerprint":"p","backend":"Cpu","provider":"local"},"metrics":[{"id":"m","value":{{value}},"evidence_ref":"{{reference}}"{{pointerMember}},"evidence_sha256":"{{hash}}","qualified":true}]}
            """;
    }

    private string WriteArtifact(string content)
    {
        File.WriteAllText(Path.Combine(Workspace, "metrics.json"), content, new UTF8Encoding(false));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
