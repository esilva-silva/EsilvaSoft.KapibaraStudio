using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Serializa um <see cref="AiEvaluationReport"/> nos dois formatos de saída: JSON (para diff e pós-processamento) e
/// Markdown (para leitura humana e colagem em decisão). Os dois carregam exatamente o mesmo conteúdo; o Markdown não
/// acrescenta nada que o JSON não tenha.
/// </summary>
/// <remarks>
/// A saída é <b>efêmera</b>: mora em <c>tests/EsilvaSoft.KapibaraStudio.Benchmarks/Ai/output/</c>, é ignorada pelo git
/// (ver o <c>.gitignore</c> local) e não deve ser commitada. Relatório de medição só vale junto com a máquina que o
/// produziu, e a máquina está dentro do próprio arquivo.
/// </remarks>
public static class AiEvaluationReportWriter
{
    /// <summary>Nome base dos dois arquivos de saída.</summary>
    public const string FileBaseName = "ai-context-evaluation";

    /// <summary>Títulos das seções do Markdown, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> MarkdownSections { get; } =
    [
        "## Execução",
        "## Máquina",
        "## Distribuição do dataset",
        "## Métricas agregadas",
        "## Orçamento e determinismo",
        "## Recortes"
    ];

    // Sem BOM: o JSON é lido por ferramenta e um BOM só atrapalha quem faz diff ou pipe do arquivo.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializa o relatório em JSON indentado com nomes em <c>camelCase</c>.</summary>
    public static string ToJson(AiEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Json);
    }

    /// <summary>Serializa o relatório em Markdown com as seções de <see cref="MarkdownSections"/>.</summary>
    public static string ToMarkdown(AiEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder(4096);
        builder.Append("# Avaliação de contexto para IA — ").Append(report.ContractId).Append("\n\n")
            .Append("Saída efêmera do `AiContextEvaluationHarness`; não commitar.\n\n");

        builder.Append(MarkdownSections[0]).Append("\n\n")
            .Append(culture, $"- Versão do formato: {report.Version}\n")
            .Append(culture, $"- Gerado em (UTC): {report.GeneratedAtUtc:O}\n")
            .Append(culture, $"- Contrato: `{report.ContractId}`\n")
            .Append(culture, $"- Contador de tokens: `{report.TokenCounter}` (contagem em tokens reais é A34c)\n")
            .Append(culture, $"- Semente raiz: {report.RootSeed}\n")
            .Append(culture, $"- Repetições cronometradas por caso: {report.Repetitions} (aquecimento: {report.WarmupRepetitions})\n\n");

        builder.Append(MarkdownSections[1]).Append("\n\n")
            .Append(culture, $"- Sistema: {report.Environment.OperatingSystem}\n")
            .Append(culture, $"- Arquitetura: {report.Environment.Architecture}\n")
            .Append(culture, $"- Processadores lógicos: {report.Environment.LogicalProcessors}\n")
            .Append(culture, $"- Runtime: {report.Environment.Runtime}\n")
            .Append(culture, $"- GC de servidor: {report.Environment.ServerGarbageCollection}\n")
            .Append(culture, $"- Depurador anexado: {report.Environment.DebuggerAttached}\n")
            .Append(culture, $"- Configuração: {report.Environment.Configuration}\n\n");

        var distribution = report.Distribution;
        builder.Append(MarkdownSections[2]).Append("\n\n")
            .Append("| Categoria | Valor | Casos |\n| --- | --- | --- |\n")
            .Append(culture, $"| total | — | {distribution.Total} |\n");
        foreach (var entry in distribution.ByShape.OrderBy(entry => entry.Key))
            builder.Append(culture, $"| forma | {entry.Key} | {entry.Value} |\n");
        foreach (var entry in distribution.BySchemaSize.OrderBy(entry => entry.Key))
            builder.Append(culture, $"| schema | {entry.Key} | {entry.Value} |\n");
        builder.Append(culture, $"| lookup | com | {distribution.WithLookup} |\n")
            .Append(culture, $"| lookup | sem | {distribution.WithoutLookup} |\n")
            .Append(culture, $"| schema aprendido | com | {distribution.WithLearnedSchema} |\n")
            .Append(culture, $"| schema aprendido | sem | {distribution.WithoutLearnedSchema} |\n")
            .Append(culture, $"| coleções alvo distintas | — | {distribution.DistinctCollections} |\n\n");

        builder.Append(MarkdownSections[3]).Append("\n\n")
            .Append("| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |\n| --- | --- | --- | --- | --- | --- | --- |\n");
        AppendMetric(builder, "tokens do prompt", report.PromptTokens);
        AppendMetric(builder, "tokens dos fatos", report.FactTokens);
        AppendMetric(builder, "fatos selecionados", report.FactCount);
        AppendMetric(builder, "seleção (µs)", report.SelectionMicroseconds);
        AppendMetric(builder, "montagem (µs)", report.AssemblyMicroseconds);
        AppendMetric(builder, "total (µs)", report.TotalMicroseconds);
        builder.Append('\n');

        builder.Append(MarkdownSections[4]).Append("\n\n")
            .Append(culture, $"- Casos que precisariam de corte: {report.BudgetOverflowCases} de {distribution.Total} ({report.BudgetOverflowRate:P2})\n")
            .Append(culture, $"- Maior excesso: {report.MaximumOverflowTokens} tokens\n")
            .Append(culture, $"- Casos não determinísticos: {report.NonDeterministicCases}\n");
        if (report.NonDeterministicCaseIds.Count > 0)
            builder.Append(culture, $"- Divergentes: {string.Join(", ", report.NonDeterministicCaseIds)}\n");
        builder.Append('\n');

        builder.Append(MarkdownSections[5]).Append("\n\n")
            .Append("| Dimensão | Valor | Casos | Tokens (média) | Tokens (desvio) | Total µs (média) | Estouros |\n")
            .Append("| --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (var segment in report.Segments)
            builder.Append(culture,
                $"| {segment.Dimension} | {segment.Value} | {segment.Cases} | {segment.PromptTokens.Mean:F1} | {segment.PromptTokens.StandardDeviation:F1} | {segment.TotalMicroseconds.Mean:F1} | {segment.BudgetOverflowCases} |\n");

        return builder.ToString();
    }

    /// <summary>
    /// Escreve os dois arquivos em <paramref name="directory"/> e devolve os caminhos, na ordem JSON, Markdown.
    /// </summary>
    public static async Task<IReadOnlyList<string>> WriteAsync(AiEvaluationReport report, string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, FileBaseName + ".json");
        var markdown = Path.Combine(directory, FileBaseName + ".md");
        await File.WriteAllTextAsync(json, ToJson(report), Utf8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdown, ToMarkdown(report), Utf8, cancellationToken).ConfigureAwait(false);
        return [json, markdown];
    }

    /// <summary>
    /// Diretório de saída padrão: <c>Ai/output/</c> dentro do projeto de benchmarks. Resolvido subindo a partir do
    /// diretório de execução até encontrar o <c>.csproj</c>; quando o projeto não está disponível (saída copiada para
    /// outra máquina), cai no diretório de execução.
    /// </summary>
    public static string DefaultDirectory()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (current.GetFiles("EsilvaSoft.KapibaraStudio.Benchmarks.csproj").Length > 0)
                return Path.Combine(current.FullName, "Ai", "output");
        return Path.Combine(AppContext.BaseDirectory, "Ai", "output");
    }

    private static void AppendMetric(StringBuilder builder, string name, AiMetricSummary metric) =>
        builder.Append(CultureInfo.InvariantCulture,
            $"| {name} | {metric.Mean:F2} | {metric.StandardDeviation:F2} | {metric.Minimum:F2} | {metric.Median:F2} | {metric.Percentile95:F2} | {metric.Maximum:F2} |\n");
}
