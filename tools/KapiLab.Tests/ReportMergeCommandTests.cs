using System.Text;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ReportMergeCommandTests : GateEvidenceFixture
{
    private const string Gates = """
        {"schema":"kapilab-gates-v1","version":"v1","source_hash":"source","gates":[{"id":"g","owner":"lab","required":true,"metric_id":"m","operator":"lte","threshold":100}]}
        """;

    [Test]
    public async Task MergeRejectsInvalidUtf8WithoutReplacingExistingOutput()
    {
        var firstReport = GateEvaluation.Serialize(GateEvaluation.Evaluate(Gates, Evidence("run-a"), Workspace));
        var secondReport = GateEvaluation.Serialize(GateEvaluation.Evaluate(Gates, Evidence("run-b"), Workspace));
        var firstPath = Path.Combine(Workspace, "first.json");
        var secondPath = Path.Combine(Workspace, "second.json");
        var outputPath = Path.Combine(Workspace, "reports", "lab", "merged.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(firstPath, firstReport, new UTF8Encoding(false));
        var prefix = Encoding.UTF8.GetBytes(secondReport[..^1] + ",\"ignored\":\"");
        var suffix = Encoding.UTF8.GetBytes("\"}");
        await File.WriteAllBytesAsync(secondPath, [.. prefix, 0xFF, .. suffix]);
        await File.WriteAllTextAsync(outputPath, "previous-output", new UTF8Encoding(false));

        var parsed = KapiLabCommandLine.Build().Parse([
            "report", "merge", "first.json", "second.json", "--out", "reports/lab/merged.json",
            "--workspace", Workspace,
        ]);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = await parsed.InvokeAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(exitCode, Is.EqualTo(3));
            Assert.That(stdout.ToString(), Is.Empty);
            Assert.That(stderr.ToString(), Does.Contain("\"exit\":3"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("previous-output"));
        });
    }

    private static string Evidence(string runId) =>
        $$"""
        {"schema":"kapilab-run-evidence-v3","status":"completed","run_id":"{{runId}}","qualified":true,"identity":{"build_fingerprint":"build","package_fingerprint":"package","backend":"Cpu","provider":"local"},"metrics":[{"id":"m","value":90,"evidence_ref":"metrics.json","evidence_sha256":"{{ArtifactHash}}","qualified":true}]}
        """;
}
