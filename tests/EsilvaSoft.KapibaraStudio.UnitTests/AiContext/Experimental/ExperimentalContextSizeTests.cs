using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.AiContext.Experimental;

/// <summary>
/// Registra o tamanho de cabeçalho de cada formato contra o v1, para a mesma aba capturada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Isto não elege formato.</b> Caractere não é token, e nenhum destes números diz qual formato produz melhor
/// completação — só um modelo base rodando o harness de A34c pode dizer isso, e ele não existe neste lote. O que este
/// arquivo faz é congelar o dado de entrada dessa avaliação e falhar se um formato deixar de cumprir a única promessa
/// de tamanho que ele mesmo faz (o minimalista ser realmente o menor).
/// </para>
/// <para>A comparação é sobre <c>Context</c>: <c>Prefix</c>/<c>Suffix</c> saem da mesma janela para todos.</para>
/// </remarks>
[TestFixture]
public sealed class ExperimentalContextSizeTests
{
    private static AutocompleteRequestSizes Measure()
    {
        var settings = new AutocompleteSettings();
        var snapshot = ExperimentalContextCorpus.RichSnapshot;
        var v1 = EditorContextV1Contract.Instance.Build(snapshot, settings).Context.Length;
        var formats = ExperimentalContextContracts.All
            .ToDictionary(contract => contract.ContractId, contract => contract.Build(snapshot, settings).Context.Length, StringComparer.Ordinal);
        return new(v1, formats);
    }

    private sealed record AutocompleteRequestSizes(int V1, IReadOnlyDictionary<string, int> Formats);

    [Test]
    public void EveryFormatReportsItsSizeAgainstV1()
    {
        var sizes = Measure();
        TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"editor-context-v1: {sizes.V1} caracteres de cabeçalho"));
        foreach (var (id, length) in sizes.Formats)
            TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{id}: {length} caracteres ({(double)length / sizes.V1:0.00}x do v1)"));
        Assert.Multiple(() =>
        {
            Assert.That(sizes.V1, Is.GreaterThan(0));
            Assert.That(sizes.Formats.Values, Is.All.GreaterThan(0), "Com esta aba, nenhum formato pode sair vazio.");
        });
    }

    /// <summary>Quatro serializações diferentes do mesmo material precisam produzir quatro textos diferentes.</summary>
    [Test]
    public void FormatsAreActuallyDifferentTexts()
    {
        var settings = new AutocompleteSettings();
        var headers = ExperimentalContextContracts.All
            .Select(contract => contract.Build(ExperimentalContextCorpus.RichSnapshot, settings).Context)
            .Append(EditorContextV1Contract.Instance.Build(ExperimentalContextCorpus.RichSnapshot, settings).Context)
            .ToArray();
        Assert.That(headers, Is.Unique);
    }

    /// <summary>A única promessa de tamanho de um formato deste lote: o minimalista é menor que o v1 e que os outros três.</summary>
    [Test]
    public void TheMinimalFormatIsTheSmallest()
    {
        var sizes = Measure();
        var minimal = sizes.Formats[MinimalContextContract.Instance.ContractId];
        Assert.Multiple(() =>
        {
            Assert.That(minimal, Is.LessThan(sizes.V1));
            foreach (var (id, length) in sizes.Formats.Where(entry => entry.Key != MinimalContextContract.Instance.ContractId))
                Assert.That(minimal, Is.LessThan(length), "Formato maior que o minimalista: " + id);
        });
    }

    /// <summary>O custo fixo dos exemplos é a aposta do formato few-shot; se sumir, o formato deixou de ser o que era.</summary>
    [Test]
    public void TheFewShotFormatCarriesItsFixedExamples()
    {
        var context = FewShotContextContract.Instance.Build(ExperimentalContextCorpus.RichSnapshot, new()).Context;
        Assert.Multiple(() =>
        {
            Assert.That(context, Does.StartWith("EXEMPLOS SINTETICOS"));
            Assert.That(context, Does.Contain("FIM DOS EXEMPLOS"));
            Assert.That(context, Does.Contain("CONTEXTO REAL"));
            Assert.That(FewShotContextContract.Examples.Length, Is.GreaterThan(200));
        });
    }

    /// <summary>O cabeçalho JSON precisa ser JSON válido em todo caso do corpus, senão a hipótese do formato não se sustenta.</summary>
    [TestCaseSource(typeof(ExperimentalContextSizeTests), nameof(CaseIds))]
    public void TheJsonFormatAlwaysParses(string caseId)
    {
        var @case = ExperimentalContextCorpus.Cases.Single(entry => entry.Id == caseId);
        var context = JsonContextContract.Instance.Build(@case.Resolve(), @case.Settings).Context;
        Assert.DoesNotThrow(() =>
        {
            using var document = System.Text.Json.JsonDocument.Parse(context);
            Assert.That(document.RootElement.GetProperty("contract").GetString(), Is.EqualTo(JsonContextContract.Instance.ContractId));
        }, "Cabeçalho JSON inválido: " + context);
    }

    public static IEnumerable<string> CaseIds() => ExperimentalContextCorpus.Cases.Select(@case => @case.Id);
}
