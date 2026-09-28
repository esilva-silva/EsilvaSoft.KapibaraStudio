using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// A limpeza de saída compartilhada pelas modalidades de IA. <see cref="CompletionOutputProcessor.Clean"/> é o
/// comportamento congelado do ghost automático; <see cref="CompletionOutputProcessor.CleanStructured"/> acrescenta a
/// parada estrutural do caminho explícito.
/// </summary>
[TestFixture]
public sealed class CompletionOutputProcessorTests
{
    [TestCase("find({})", "", "find({})")]
    [TestCase("name: 1 })\nfunction outra() {}", "})", "name: 1 ")]
    [TestCase("   ", "", null)]
    [TestCase("```js\nfind()\n```", "", null)]
    [TestCase("texto\0nulo", "", null)]
    [TestCase("<|fim_middle|>find()", "", null)]
    [TestCase("const password = 'x';", "", null)]
    public void CleanKeepsTheFrozenAutomaticBehaviour(string text, string suffix, string? expected)
        => Assert.That(CompletionOutputProcessor.Clean(text, suffix), Is.EqualTo(expected));

    /// <summary>Um sufixo de um caractere não é sinal de eco confiável e não corta nada.</summary>
    [Test]
    public void ASingleCharacterSuffixIsNotTreatedAsAnEcho()
        => Assert.That(CompletionOutputProcessor.Clean("a}b", "}"), Is.EqualTo("a}b"));

    [TestCase("find({})")]
    [TestCase("name: 1 })")]
    [TestCase("[1, 2, 3]")]
    [TestCase("x: 'a } b'")]
    [TestCase("x: 1 // comentário sem fim")]
    [TestCase("/* bloco */ x: 1")]
    [TestCase("find({});")]
    public void StructurallySoundTextIsKeptWhole(string text)
        => Assert.That(StructuralStopDetector.Truncate(text), Is.EqualTo(text));

    [TestCase("name: 1 }, { outro: ", "name: 1 },")]
    [TestCase("find({});\ndb.outra.drop();", "find({});")]
    [TestCase("x: 'aberta", "x:")]
    [TestCase("x: 1 /* aberto", "x: 1")]
    [TestCase("f({ a: 1 )", "f")]
    [TestCase("f(", "f")]
    public void StructuralStopCutsAtTheBrokenBoundary(string text, string expected)
        => Assert.That(StructuralStopDetector.Truncate(text), Is.EqualTo(expected));

    /// <summary>Fechar o que o documento abriu antes do cursor é o caso normal e nunca é corte.</summary>
    [Test]
    public void ClosingWhatTheDocumentOpenedIsNotAStructuralBreak()
        => Assert.That(StructuralStopDetector.Truncate("status: 'A' }).toArray()"), Is.EqualTo("status: 'A' }).toArray()"));

    /// <summary>A reprovação acontece sobre o texto inteiro: o corte não pode absolver um bloco cercado no fim.</summary>
    [Test]
    public void StructuredCleanStillRejectsWhatTheWholeTextForbids()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompletionOutputProcessor.CleanStructured("x: 1;\n```", "", out _), Is.Null);
            Assert.That(CompletionOutputProcessor.CleanStructured("x: 1;\nconst password = 'y';", "", out _), Is.Null);
        });
    }

    [Test]
    public void StructuredCleanReportsWhetherItTruncated()
    {
        Assert.That(CompletionOutputProcessor.CleanStructured("x: 1 }, { y: ", "", out var truncated), Is.EqualTo("x: 1 },"));
        Assert.That(truncated, Is.True);
        Assert.That(CompletionOutputProcessor.CleanStructured("x: 1", "", out var untouched), Is.EqualTo("x: 1"));
        Assert.That(untouched, Is.False);
    }
}
