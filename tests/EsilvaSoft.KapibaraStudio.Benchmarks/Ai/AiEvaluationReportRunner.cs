using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Ferramenta manual: roda o dataset sintético inteiro pelo harness e escreve o relatório em JSON e Markdown.
/// </summary>
/// <remarks>
/// <para>
/// É <see cref="ExplicitAttribute"/> de propósito e não participa da suíte regular: mede latência, e medir latência em
/// máquina compartilhada com um agente de CI produz número sem significado. A corretude do instrumento é coberta por
/// <see cref="AiContextEvaluationHarnessTests"/> e <see cref="AiEvaluationReportTests"/>, esses sim automáticos.
/// </para>
/// <para>
/// Executar com
/// <c>dotnet test tests/EsilvaSoft.KapibaraStudio.Benchmarks -c Release --filter "FullyQualifiedName~AiEvaluationReportRunner"</c>.
/// A saída vai para <c>tests/EsilvaSoft.KapibaraStudio.Benchmarks/Ai/output/</c>, é ignorada pelo git e não deve ser
/// commitada: o relatório só vale acompanhado da máquina que o produziu, e a máquina está dentro do arquivo.
/// </para>
/// </remarks>
[TestFixture, Explicit("Ferramenta de avaliação manual; escreve relatório e mede latência.")]
public sealed class AiEvaluationReportRunner
{
    [Test]
    public async Task GenerateReportForTheDefaultDataset()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var dataset = AiEvaluationDataset.CreateDefault();
        var harness = new AiContextEvaluationHarness(dataset) { Repetitions = 5, WarmupRepetitions = 2 };

        var report = harness.Run(cancellationToken);
        var files = await AiEvaluationReportWriter.WriteAsync(report, AiEvaluationReportWriter.DefaultDirectory(), cancellationToken);

        await TestContext.Out.WriteLineAsync(AiEvaluationReportWriter.ToMarkdown(report));
        foreach (var file in files) await TestContext.Out.WriteLineAsync("Relatório: " + file);
        Assert.That(report.NonDeterministicCases, Is.Zero, "Mesma entrada precisa produzir o mesmo prompt.");
    }
}
