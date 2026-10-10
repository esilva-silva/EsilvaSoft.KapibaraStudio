using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

public abstract class GateEvidenceFixture
{
    // Independent SHA-256 vector for the UTF-8 bytes "abc".
    protected const string ArtifactHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    protected string Workspace { get; private set; } = "";

    [SetUp]
    public void CreateEvidenceWorkspace()
    {
        Workspace = Path.Combine(Path.GetTempPath(), "kapilab-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Workspace);
        File.WriteAllText(Path.Combine(Workspace, "metrics.json"), "abc", new System.Text.UTF8Encoding(false));
    }

    [TearDown]
    public void RemoveEvidenceWorkspace() => Directory.Delete(Workspace, recursive: true);

    private protected GateEvaluation.EvaluationReport Evaluate(string gates, string evidence)
    {
        var upgraded = evidence.Replace("\"schema\":\"kapilab-run-evidence-v1\"",
            "\"schema\":\"kapilab-run-evidence-v3\",\"status\":\"completed\"", StringComparison.Ordinal);
        foreach (var reference in new[] { "metrics.json#p95", "metrics.json#quality" })
            upgraded = upgraded.Replace($"\"evidence_ref\":\"{reference}\"",
                $"\"evidence_ref\":\"{reference}\",\"evidence_sha256\":\"{ArtifactHash}\"", StringComparison.Ordinal);
        return GateEvaluation.Evaluate(gates, upgraded, Workspace);
    }
}
