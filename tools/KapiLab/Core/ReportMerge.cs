using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// Deterministic local merge contract. It preserves the member reports rather than
/// synthesizing metric aggregates; the external §5.11/§9.4 report schema is not in this checkout.
/// </summary>
internal static class ReportMerge
{
    internal const string Schema = "kapilab-merged-report-v1";
    internal const int MaximumReports = 1000;
    internal const int MaximumReportCharacters = 4 * 1024 * 1024;
    internal const long MaximumTotalCharacters = 16L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static MergedEvaluationReport Merge(IEnumerable<string> reportsJson)
    {
        ArgumentNullException.ThrowIfNull(reportsJson);
        var collected = new List<GateEvaluation.EvaluationReport>();
        long totalCharacters = 0;
        foreach (var json in reportsJson)
        {
            if (collected.Count == MaximumReports)
                throw new InvalidDataException("Quantidade de relatórios inválida para merge.");
            if (json is null)
                throw new InvalidDataException("Relatório nulo no merge.");
            totalCharacters += json.Length;
            if (totalCharacters > MaximumTotalCharacters)
                throw new InvalidDataException("Tamanho total dos relatórios excede o limite permitido.");
            collected.Add(ParseReport(json));
        }
        if (collected.Count == 0)
            throw new InvalidDataException("Quantidade de relatórios inválida para merge.");
        var reports = collected.ToArray();

        var first = reports[0];
        EnsureUniqueRunIds(reports);
        foreach (var report in reports.Skip(1))
        {
            if (!StringComparer.Ordinal.Equals(report.GateSetVersion, first.GateSetVersion) ||
                !StringComparer.Ordinal.Equals(report.GateSourceHash, first.GateSourceHash) ||
                !StringComparer.Ordinal.Equals(report.GateArtifactSha256, first.GateArtifactSha256))
                throw new InvalidDataException("Relatórios de conjuntos de gates diferentes não podem ser unidos.");
            if (report.Identity != first.Identity)
                throw new InvalidDataException("Relatórios de builds, pacotes, backends ou providers diferentes não podem ser unidos.");
            if (!SameGateDefinitions(report.Gates, first.Gates))
                throw new InvalidDataException("Definições de gates divergentes entre relatórios.");
        }

        var ordered = reports.OrderBy(static report => report.RunId, StringComparer.Ordinal).ToArray();
        var qualified = ordered.All(static report => report.Qualified);
        var complete = ordered.All(static report => report.Complete);
        var passed = ordered.All(static report => report.Outcome == "passed" && report.Qualified && report.Complete && report.Passed);
        var outcome = passed ? "passed"
            : ordered.Any(static report => !report.Qualified || report.Outcome == "unqualified") ? "unqualified"
            : ordered.All(static report => report.Outcome == "skipped") ? "skipped"
            : !complete || ordered.Any(static report => report.Outcome is "skipped" or "inconclusive") ? "inconclusive"
            : "failed";

        return new(Schema, first.GateSetVersion, first.GateSourceHash, first.GateArtifactSha256,
            first.Identity, qualified, complete, passed, outcome, ordered);
    }

    internal static void ValidateInputLimits(int reportCount, long totalCharacters)
    {
        if (reportCount is < 1 or > MaximumReports)
            throw new InvalidDataException("Quantidade de relatórios inválida para merge.");
        if (totalCharacters < 0 || totalCharacters > MaximumTotalCharacters)
            throw new InvalidDataException("Tamanho total dos relatórios excede o limite permitido.");
    }

    public static string Serialize(MergedEvaluationReport report) => JsonSerializer.Serialize(report, JsonOptions);

    private static GateEvaluation.EvaluationReport ParseReport(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumReportCharacters)
            throw new InvalidDataException("Relatório vazio ou acima do limite permitido.");

        GateEvaluation.EvaluationReport? report;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
            report = JsonSerializer.Deserialize<GateEvaluation.EvaluationReport>(document.RootElement, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Relatório inválido para merge.", exception);
        }

        if (report is null || report.Schema is not ("kapilab-report-v2" or "kapilab-report-v3" or "kapilab-report-v4") || report.Identity is null || report.Gates is null)
            throw new InvalidDataException("Relatório sem schema local compatível ou proveniência suficiente.");
        RequireText(report.GateSetVersion, "Versão do conjunto de gates");
        RequireText(report.GateSourceHash, "Hash de origem do conjunto de gates");
        RequireSha256(report.GateArtifactSha256, "Hash do artefato de gates");
        RequireText(report.RunId, "ID da execução");
        RequireText(report.Identity.BuildFingerprint, "Fingerprint do build");
        RequireText(report.Identity.PackageFingerprint, "Fingerprint do pacote");
        RequireText(report.Identity.Backend, "Backend");
        RequireText(report.Identity.Provider, "Provider");
        if (report.Gates.Count is < 1 or > 20_000)
            throw new InvalidDataException("Quantidade de gates inválida no relatório.");
        if (report.Outcome is not ("passed" or "failed" or "unqualified" or "skipped" or "inconclusive"))
            throw new InvalidDataException("Estado do relatório inválido.");
        if (report.Passed && (!report.Qualified || !report.Complete || report.Outcome != "passed"))
            throw new InvalidDataException("Relatório inconsistente tenta aprovar uma execução incompleta ou desqualificada.");
        if (report.Outcome == "passed" && !report.Passed)
            throw new InvalidDataException("Estado passed sem aprovação do relatório.");

        foreach (var gate in report.Gates)
        {
            if (gate is null || gate.Status is not ("passed" or "failed" or "unqualified" or "missing-evidence" or "missing-gates" or "skipped" or "inconclusive" or "unverified-evidence" or "evidence-integrity-failed" or "evidence-value-invalid" or "evidence-value-mismatch"))
                throw new InvalidDataException("Gate com estado ausente ou desconhecido.");
            if (gate.Status is "evidence-value-invalid" or "evidence-value-mismatch" && report.Schema != "kapilab-report-v4")
                throw new InvalidDataException("Estado de evidência v4 em relatório de versão anterior.");
            if (gate.Status == "missing-gates") continue;
            RequireText(gate.Id, "ID do gate");
            RequireText(gate.Owner, "Owner do gate");
            RequireText(gate.MetricId, "ID da métrica");
            if (gate.EvidenceReferenceSha256 is not null)
                RequireSha256(gate.EvidenceReferenceSha256, "Hash da referência de evidência");
            if (gate.EvidenceArtifactSha256 is not null)
                RequireSha256(gate.EvidenceArtifactSha256, "Hash do conteúdo da evidência");
            if (gate.Status is "passed" or "failed")
            {
                if (report.Schema is not ("kapilab-report-v3" or "kapilab-report-v4"))
                    throw new InvalidDataException("Relatório antigo sem comprovação da integridade dos artefatos; reavalie a rodada.");
                RequireSha256(gate.EvidenceReferenceSha256, "Hash da referência de evidência");
                RequireSha256(gate.EvidenceArtifactSha256, "Hash do conteúdo da evidência");
            }
        }

        var statusComplete = report.Gates.All(static gate => gate.Status is "passed" or "failed");
        if (report.Complete != statusComplete)
            throw new InvalidDataException("Estado complete incompatível com os gates do relatório.");
        var requiredGates = report.Gates.Where(static gate => gate.Required).ToArray();
        if (report.Passed && (requiredGates.Length == 0 || requiredGates.Any(static gate => gate.Status != "passed")))
            throw new InvalidDataException("Relatório aprovado sem todos os gates obrigatórios aprovados.");
        if (report.Outcome == "unqualified" && report.Qualified && report.Gates.All(static gate => gate.Status != "unqualified"))
            throw new InvalidDataException("Relatório desqualificado sem causa declarada.");
        if (report.Outcome == "skipped" && report.Gates.Any(static gate => gate.Status != "skipped"))
            throw new InvalidDataException("Relatório skipped contém gates com outro estado.");
        if (report.Outcome == "inconclusive" && report.Complete)
            throw new InvalidDataException("Relatório inconclusivo não pode declarar todos os gates completos.");
        if (report.Outcome == "failed" && (!report.Qualified || !report.Complete || report.Passed))
            throw new InvalidDataException("Estado failed inconsistente com qualificação, completude ou aprovação.");

        return report;
    }

    private static bool SameGateDefinitions(IReadOnlyList<GateEvaluation.GateResult> left,
        IReadOnlyList<GateEvaluation.GateResult> right) =>
        left.Count == right.Count && left.Zip(right).All(static pair =>
            pair.First.Id == pair.Second.Id && pair.First.Owner == pair.Second.Owner &&
            pair.First.Required == pair.Second.Required && pair.First.MetricId == pair.Second.MetricId &&
            pair.First.Operator == pair.Second.Operator && pair.First.Threshold == pair.Second.Threshold);

    private static void EnsureUniqueRunIds(GateEvaluation.EvaluationReport[] reports)
    {
        if (reports.Select(static report => report.RunId).Distinct(StringComparer.Ordinal).Count() != reports.Length)
            throw new InvalidDataException("IDs de execução duplicados no merge.");
    }

    private static void RequireText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new InvalidDataException($"{label} ausente ou inválido.");
    }

    private static void RequireSha256(string? value, string label)
    {
        RequireText(value, label);
        if (value!.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{label} inválido.");
        try { _ = Convert.FromHexString(value); }
        catch (FormatException exception) { throw new InvalidDataException($"{label} inválido.", exception); }
    }

    internal sealed record MergedEvaluationReport(string Schema, string GateSetVersion, string GateSourceHash,
        string GateArtifactSha256, GateEvaluation.RunIdentity Identity, bool Qualified, bool Complete,
        bool Passed, string Outcome, IReadOnlyList<GateEvaluation.EvaluationReport> Reports);
}
