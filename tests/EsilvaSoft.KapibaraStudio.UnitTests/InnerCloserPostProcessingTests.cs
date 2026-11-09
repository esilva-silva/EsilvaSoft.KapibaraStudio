using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Fechamento de delimitadores internos do contrato <c>editor-context-v2</c>. Os casos são os mesmos de
/// <c>test_close_inner_adds_missing_inner_closers</c>, <c>test_close_inner_leaves_other_cases_alone</c> e
/// <c>test_postprocess_close_inner_is_opt_in</c> (<c>scripts/eval/tests/test_cursor_suite.py</c> no repositório de
/// modelos), que validaram a regra na suíte cursor-in-call: os dois lados precisam dar a mesma resposta.
/// </summary>
[TestFixture]
public sealed class InnerCloserPostProcessingTests
{
    [TestCase("db.Courses.deleteOne({ Title: { $ty", "pe: \"string\"", " })", "pe: \"string\" }")]   // operador parcial
    [TestCase("db.o.aggregate([{ $match: { b: { $ty", "pe: \"string\"", " } }])", "pe: \"string\" }")] // dentro do pipeline
    [TestCase("db.o.find({ a: { $in: [", "\"x\", \"y\"", " })", "\"x\", \"y\" ] }")]                     // array + objeto
    [TestCase("db.o.find({ a: { $gt", "e: 5 ", " })", "e: 5 }")]                                         // já termina em espaço
    public void MissingInnerClosersAreAppended(string prefix, string prediction, string suffix, string expected)
        => Assert.That(CompletionOutputProcessor.CloseInnerDelimiters(prefix, prediction, suffix), Is.EqualTo(expected));

    [TestCase("db.c.find({ ", "Status: \"a\"", " })")]                         // já consistente
    [TestCase("db.c.find({ a: { $ex", "ists: true", " }, { $set: { b: 1 } })")] // o sufixo continua o statement: ambíguo
    [TestCase("db.c.find({ a: { $ty", "", " })")]                               // sugestão vazia
    [TestCase("db.c.find({ a: 'abc", "def", "' })")]                            // termina dentro de string
    [TestCase("db.c.find({ a: 1", " }", " })")]                                 // fecha demais: não é caso desta regra
    [TestCase("db.c.find({ a: { $gt: 1 ", "", "")]                              // sem sufixo
    public void OtherCasesAreLeftAlone(string prefix, string prediction, string suffix)
        => Assert.That(CompletionOutputProcessor.CloseInnerDelimiters(prefix, prediction, suffix), Is.EqualTo(prediction));

    /// <summary>
    /// Bordas da regra que a suíte Python não lista: término por <c>;</c>, continuação em outra linha, teto de quatro
    /// internos e delimitadores dentro de strings, que não contam.
    /// </summary>
    [TestCase("db.c.find({ a: { $ty", "pe: 2", " });", "pe: 2 }")]
    [TestCase("db.c.find({ a: { $ty", "pe: 2", " })\ndb.c.drop()", "pe: 2")]
    [TestCase("db.c.find({ a: { b: { c: { d: { e: { $ty", "pe: 2", " })", "pe: 2")]
    [TestCase("db.c.find({ a: { b: { c: { d: { $ty", "pe: 2", " })", "pe: 2 } } } }")]
    [TestCase("db.c.find({ a: { $eq: \"{[(\"", ", x: 1", " })", ", x: 1 }")]
    public void EdgesOfTheRule(string prefix, string prediction, string suffix, string expected)
        => Assert.That(CompletionOutputProcessor.CloseInnerDelimiters(prefix, prediction, suffix), Is.EqualTo(expected));

    /// <summary>
    /// Paridade com <c>test_postprocess_close_inner_is_opt_in</c>: sem o documento (v1) nada muda; com ele (v2) o
    /// eco do sufixo é cortado antes e o fechamento vem depois.
    /// </summary>
    [Test]
    public void CleanStructuredClosesOnlyWhenAskedAndAfterTheSuffixEcho()
    {
        const string prefix = "db.c.find({ a: { $ty";
        const string suffix = " })";
        var document = new InnerCloserContext(prefix, suffix);
        Assert.Multiple(() =>
        {
            Assert.That(CompletionOutputProcessor.CleanStructured("pe: \"string\"", suffix, out _), Is.EqualTo("pe: \"string\""));
            Assert.That(CompletionOutputProcessor.CleanStructured("pe: \"string\"", suffix, document, out _), Is.EqualTo("pe: \"string\" }"));
            Assert.That(CompletionOutputProcessor.CleanStructured("pe: \"string\" })", suffix, document, out _), Is.EqualTo("pe: \"string\" }"));
        });
    }

    /// <summary>
    /// Ponta a ponta no pipeline de geração: o mesmo texto gerado sai fechado só para um pacote que declara
    /// <c>editor-context-v2</c>; o v1 (declarado ou ausente) continua byte a byte.
    /// </summary>
    [TestCase(null, "pe: \"string\"")]
    [TestCase(LocalModelContextContracts.EditorContextV1, "pe: \"string\"")]
    [TestCase(LocalModelContextContracts.EditorContextV2, "pe: \"string\" }")]
    public async Task ThePipelineClosesInnerDelimitersOnlyForTheV2Contract(string? contract, string expected)
    {
        const string document = "db.Courses.deleteOne({ Title: { $ty })";
        var service = new StreamingModelServiceFake { Chunks = ["pe: ", "\"string\""] };
        service.Definition = service.Definition with { Metadata = new LocalModelMetadata { ContextContract = contract } };
        var provider = new AiCompletionProvider(new AiGenerationPipeline(service, _ => new CompletionTokenizerFake(), new QwenFimPromptBuilder(), null));
        var request = new AiGenerationRequest(new AutocompleteContextSnapshot(document, document.Length - 3, "Mongo Console JavaScript"),
            new AutocompleteSettings { ContextTokens = 4096 });

        AiCompletionUpdate? last = null;
        await foreach (var update in provider.RequestAsync(request)) last = update;

        Assert.That(last?.Candidate?.Text, Is.EqualTo(expected));
    }

    /// <summary>O opt-out do contexto do editor vale também aqui: o modelo não viu o sufixo, a regra não age.</summary>
    [Test]
    public async Task ThePipelineDoesNotCloseWhenTheEditorContextIsOff()
    {
        const string document = "db.Courses.deleteOne({ Title: { $ty })";
        var service = new StreamingModelServiceFake { Chunks = ["pe: \"string\""] };
        service.Definition = service.Definition with
        {
            Metadata = new LocalModelMetadata { ContextContract = LocalModelContextContracts.EditorContextV2 }
        };
        var provider = new AiCompletionProvider(new AiGenerationPipeline(service, _ => new CompletionTokenizerFake(), new QwenFimPromptBuilder(), null));
        var request = new AiGenerationRequest(new AutocompleteContextSnapshot(document, document.Length - 3, "Mongo Console JavaScript"),
            new AutocompleteSettings { ContextTokens = 4096, UseEditorContext = false });

        AiCompletionUpdate? last = null;
        await foreach (var update in provider.RequestAsync(request)) last = update;

        Assert.That(last?.Candidate?.Text, Is.EqualTo("pe: \"string\""));
    }
}
