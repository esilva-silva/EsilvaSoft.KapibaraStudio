using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class GateEvaluation
{
    private const string GatesSchema = "kapilab-gates-v1";
    private const string EvidenceSchema = "kapilab-run-evidence-v1";
    private const string EvidenceSchemaV2 = "kapilab-run-evidence-v2";
    private const string EvidenceSchemaV3 = "kapilab-run-evidence-v3";
    // v4 adds an RFC 6901 pointer to a JSON numeric value in the hashed artifact; v1-v3 retain their prior contract.
    private const string EvidenceSchemaV4 = "kapilab-run-evidence-v4";
    // Local versioned contract. The external report schema is not present in this checkout.
    private const string ReportSchema = "kapilab-report-v3";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> Operators = new(["eq", "lt", "lte", "gt", "gte"], StringComparer.Ordinal);

    public static EvaluationReport Evaluate(string gatesJson, string evidenceJson, string? workspace = null, string? outputPath = null)
    {
        var gates = ParseGates(gatesJson);
        var evidence = ParseEvidence(evidenceJson);
        var reportSchema = evidence.Schema == EvidenceSchemaV4 ? "kapilab-report-v4" : ReportSchema;
        var gateResults = new List<GateResult>();
        var verification = workspace is null ? null : new EvidenceArtifactVerification(workspace, outputPath);
        if (gates.Gates.Length == 0)
            return new(reportSchema, gates.Version, gates.SourceHash, Hash(gatesJson), evidence.RunId,
                evidence.Identity, evidence.Qualified, false, false, "inconclusive",
                [new("", "", true, "", null, "", null, "missing-gates", null)]);

        if (evidence.Status is "skipped" or "inconclusive")
        {
            var skippedResults = gates.Gates.Select(gate => Result(gate, null, evidence.Status)).ToArray();
            return new(reportSchema, gates.Version, gates.SourceHash, Hash(gatesJson), evidence.RunId,
                evidence.Identity, evidence.Qualified, false, false, evidence.Status, skippedResults);
        }

        var metrics = evidence.Metrics.ToDictionary(static metric => metric.Id, StringComparer.Ordinal);
        foreach (var gate in gates.Gates)
        {
            if (!metrics.TryGetValue(gate.MetricId, out var metric))
            {
                gateResults.Add(Result(gate, null, "missing-evidence"));
                continue;
            }
            if (!evidence.Qualified || !metric.Qualified)
            {
                gateResults.Add(Result(gate, metric, "unqualified"));
                continue;
            }
            if (metric.Value is null || string.IsNullOrWhiteSpace(metric.EvidenceRef))
            {
                gateResults.Add(Result(gate, metric, "missing-evidence"));
                continue;
            }
            if (verification is null || metric.EvidenceSha256 is null)
            {
                gateResults.Add(Result(gate, metric, "unverified-evidence"));
                continue;
            }
            if (evidence.Schema == EvidenceSchemaV4)
            {
                var pointerCheck = verification.VerifyNumber(metric.EvidenceRef, metric.EvidencePointer,
                    metric.EvidenceSha256, metric.Value.Value);
                if (pointerCheck.Status != "verified")
                {
                    gateResults.Add(Result(gate, metric, pointerCheck.Status) with { EvidenceArtifactSha256 = pointerCheck.ArtifactSha256 });
                    continue;
                }
                var passedPointer = IsGatePassed(gate, metric.Value.Value);
                gateResults.Add(Result(gate, metric, passedPointer ? "passed" : "failed") with { EvidenceArtifactSha256 = pointerCheck.ArtifactSha256 });
                continue;
            }
            var artifactSha256 = verification.Verify(metric.EvidenceRef, metric.EvidenceSha256);
            if (artifactSha256 is null)
            {
                gateResults.Add(Result(gate, metric, "evidence-integrity-failed"));
                continue;
            }
            var passed = IsGatePassed(gate, metric.Value.Value);
            gateResults.Add(Result(gate, metric, passed ? "passed" : "failed") with { EvidenceArtifactSha256 = artifactSha256 });
        }

        var complete = gateResults.Count == gates.Gates.Length && gateResults.All(static result => result.Status is "passed" or "failed");
        var requiredGates = gateResults.Where(static result => result.Required).ToArray();
        var passedAll = complete && evidence.Qualified && requiredGates.Length > 0 &&
            requiredGates.All(static result => result.Status == "passed");
        var outcome = !evidence.Qualified || gateResults.Any(static result => result.Status == "unqualified")
            ? "unqualified"
            : !complete ? "inconclusive"
            : passedAll ? "passed"
            : "failed";
        return new(reportSchema, gates.Version, gates.SourceHash, Hash(gatesJson), evidence.RunId,
            evidence.Identity, evidence.Qualified, complete, passedAll, outcome, gateResults.ToArray());
    }

    public static string Serialize(EvaluationReport report) => JsonSerializer.Serialize(report, JsonOptions);

    private static GateResult Result(GateDefinition gate, MetricEvidence? metric, string status) =>
        new(gate.Id, gate.Owner, gate.Required, gate.MetricId, metric?.Value, gate.Operator, gate.Threshold,
            status, string.IsNullOrWhiteSpace(metric?.EvidenceRef) ? null : Hash(metric.EvidenceRef));

    private static bool IsGatePassed(GateDefinition gate, double value) => gate.Operator switch
    {
        "eq" => value == gate.Threshold,
        "lt" => value < gate.Threshold,
        "lte" => value <= gate.Threshold,
        "gt" => value > gate.Threshold,
        "gte" => value >= gate.Threshold,
        _ => false
    };

    private static GateSet ParseGates(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        JsonContractValidation.RequireUniqueProperties(root);
        RequireObject(root);
        var schema = RequiredString(root, "schema");
        if (schema != GatesSchema) throw new InvalidDataException("Schema de gates incompatível.");
        var version = RequiredString(root, "version");
        var sourceHash = RequiredString(root, "source_hash");
        if (sourceHash.Length > 128) throw new InvalidDataException("Hash de origem inválido.");
        var entries = RequiredArray(root, "gates");
        if (entries.GetArrayLength() > 1000) throw new InvalidDataException("Limite de gates excedido.");
        var gates = new List<GateDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in entries.EnumerateArray())
        {
            RequireObject(item);
            var id = RequiredString(item, "id");
            var owner = RequiredString(item, "owner");
            var metricId = RequiredString(item, "metric_id");
            var op = RequiredString(item, "operator");
            if (!ids.Add(id) || !Operators.Contains(op))
                throw new InvalidDataException("Identidade de gate duplicada ou operador inválido.");
            if (!item.TryGetProperty("threshold", out var thresholdElement) || !thresholdElement.TryGetDouble(out var threshold) || !double.IsFinite(threshold))
                throw new InvalidDataException("Limiar de gate inválido.");
            if (!item.TryGetProperty("required", out var requiredElement) || requiredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Campo required inválido.");
            gates.Add(new(id, owner, metricId, op, threshold, requiredElement.GetBoolean()));
        }
        return new(version, sourceHash, gates.ToArray());
    }

    private static RunEvidence ParseEvidence(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        JsonContractValidation.RequireUniqueProperties(root);
        RequireObject(root);
        var schema = RequiredString(root, "schema");
        if (schema is not (EvidenceSchema or EvidenceSchemaV2 or EvidenceSchemaV3 or EvidenceSchemaV4)) throw new InvalidDataException("Schema de evidência incompatível.");
        var runId = RequiredString(root, "run_id");
        if (!root.TryGetProperty("qualified", out var qualified) || qualified.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Qualificação da execução ausente.");
        if (!root.TryGetProperty("identity", out var identity)) throw new InvalidDataException("Proveniência da execução ausente.");
        RequireObject(identity);
        var provenance = new RunIdentity(
            RequiredString(identity, "build_fingerprint"),
            RequiredString(identity, "package_fingerprint"),
            RequiredString(identity, "backend"),
            RequiredString(identity, "provider"));
        var status = "completed";
        if (schema is EvidenceSchemaV2 or EvidenceSchemaV3 or EvidenceSchemaV4)
        {
            if (!root.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Estado da execução inválido.");
            status = statusElement.GetString() ?? string.Empty;
        }
        else if (root.TryGetProperty("status", out _))
            throw new InvalidDataException("O campo status exige kapilab-run-evidence-v2.");
        if (status is not ("completed" or "skipped" or "inconclusive"))
            throw new InvalidDataException("Estado da execução inválido.");
        var entries = RequiredArray(root, "metrics");
        if (entries.GetArrayLength() > 20_000) throw new InvalidDataException("Limite de evidências excedido.");
        var metrics = new List<MetricEvidence>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in entries.EnumerateArray())
        {
            RequireObject(item);
            var id = RequiredString(item, "id");
            if (!ids.Add(id)) throw new InvalidDataException("Identidade de métrica duplicada.");
            double? value = null;
            if (item.TryGetProperty("value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
            {
                if (!valueElement.TryGetDouble(out var parsed) || !double.IsFinite(parsed)) throw new InvalidDataException("Valor de evidência inválido.");
                value = parsed;
            }
            var evidenceRef = item.TryGetProperty("evidence_ref", out var refElement) && refElement.ValueKind == JsonValueKind.String
                ? refElement.GetString() : null;
            if (evidenceRef?.Length > 512) throw new InvalidDataException("Referência de evidência excede o limite.");
            string? evidenceSha256 = null;
            if (item.TryGetProperty("evidence_sha256", out var hashElement))
            {
                if (schema is not (EvidenceSchemaV3 or EvidenceSchemaV4) || hashElement.ValueKind != JsonValueKind.String ||
                    hashElement.GetString() is not { Length: 64 } suppliedHash || !suppliedHash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Hash de artefato inválido; evidence_sha256 exige evidência v3/v4 e SHA-256 hexadecimal.");
                evidenceSha256 = suppliedHash;
            }
            string? evidencePointer = null;
            if (schema == EvidenceSchemaV4 && item.TryGetProperty("evidence_pointer", out var pointerElement) &&
                pointerElement.ValueKind == JsonValueKind.String)
            {
                evidencePointer = pointerElement.GetString();
                if (evidencePointer?.Length > 4096) throw new InvalidDataException("JSON Pointer excede o limite.");
            }
            var itemQualified = item.TryGetProperty("qualified", out var q) && q.ValueKind == JsonValueKind.True;
            metrics.Add(new(id, value, evidenceRef, evidencePointer, itemQualified, evidenceSha256));
        }
        return new(schema, runId, provenance, qualified.GetBoolean(), status!, metrics.ToArray());
    }

    private static JsonElement RequiredArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array
            ? element : throw new InvalidDataException($"Campo {property} ausente ou inválido.");

    private static string RequiredString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(element.GetString()) && element.GetString()!.Length <= 256
            ? element.GetString()! : throw new InvalidDataException($"Campo {property} ausente ou inválido.");

    private static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Objeto JSON esperado.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record GateSet(string Version, string SourceHash, GateDefinition[] Gates);
    private sealed record GateDefinition(string Id, string Owner, string MetricId, string Operator, double Threshold, bool Required);
    private sealed record RunEvidence(string Schema, string RunId, RunIdentity Identity, bool Qualified, string Status, MetricEvidence[] Metrics);
    private sealed record MetricEvidence(string Id, double? Value, string? EvidenceRef, string? EvidencePointer, bool Qualified, string? EvidenceSha256);

    internal sealed record GateResult(string Id, string Owner, bool Required, string MetricId, double? Value,
        string Operator, double? Threshold, string Status, string? EvidenceReferenceSha256, string? EvidenceArtifactSha256 = null);
    internal sealed record RunIdentity(string BuildFingerprint, string PackageFingerprint, string Backend, string Provider);
    internal sealed record EvaluationReport(string Schema, string GateSetVersion, string GateSourceHash,
        string GateArtifactSha256, string RunId, RunIdentity Identity, bool Qualified, bool Complete, bool Passed, string Outcome,
        IReadOnlyList<GateResult> Gates);
}
