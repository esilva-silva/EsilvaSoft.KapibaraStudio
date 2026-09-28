using System.Collections;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.TokenCounting;

/// <summary>Contagem de tokens, não-composicionalidade do BPE, oráculo de fronteira e invariantes do cache de blocos.</summary>
[TestFixture]
public sealed class TokenCounterTests
{
    private const string Ascii = "db.pedidos.find({ status: 'cattle' })";
    private const string Accented = "// informação do endereço já validada";
    private const string Cjk = "// 中文注释";
    private const string Emoji = "// 😀😀 marcador";
    private const string Combining = "// café decomposto";
    private const string ZeroWidth = "// a​b largura zero";

    [TestCase(Ascii)]
    [TestCase(Accented)]
    [TestCase(Cjk)]
    [TestCase(Emoji)]
    [TestCase(Combining)]
    [TestCase(ZeroWidth)]
    public void CountMatchesEncodeCountExactly(string text)
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var counter = new TokenizerTokenCounter(tokenizer);
        var count = counter.Count(text);
        Assert.Multiple(() =>
        {
            Assert.That(count.Tokens, Is.EqualTo(tokenizer.Encode(text).Count));
            Assert.That(count.IsExact, Is.True);
            Assert.That(tokenizer.Decode(tokenizer.Encode(text)), Is.EqualTo(text), "o mini-BPE precisa ser reversível para ser prova de algo");
        });
    }

    [Test]
    public void CountMatchesEncodeCountForTheSimpleFakeTokenizerToo()
    {
        var tokenizer = new CompletionTokenizerFake();
        Assert.That(new TokenizerTokenCounter(tokenizer).Count("db.find({"), Is.EqualTo(TokenCount.Exact(tokenizer.Encode("db.find({").Count)));
    }

    [TestCase("cat", "tle", TestName = "NonCompositional(ASCII)")]
    [TestCase("informaçã", "o do pedido", TestName = "NonCompositional(AcentosPtBr)")]
    [TestCase("中", "文", TestName = "NonCompositional(CJK)")]
    [TestCase("😀", "😀", TestName = "NonCompositional(ParSubstitutoUtf16)")]
    [TestCase("e", "́", TestName = "NonCompositional(MarcaCombinante)")]
    [TestCase("a", "​", TestName = "NonCompositional(LarguraZero)")]
    public void EncodingBlocksSeparatelyDisagreesWithEncodingTheConcatenation(string left, string right)
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var separate = tokenizer.Encode(left).Count + tokenizer.Encode(right).Count;
        var together = tokenizer.Encode(left + right).Count;
        Assert.That(together, Is.Not.EqualTo(separate), "merge deveria atravessar a fronteira entre os blocos");
    }

    [Test]
    public void SplittingASurrogatePairAcrossBlocksCorruptsBothSides()
    {
        // Cortar um bloco no meio de um par substituto produz substitutos solitários, que a codificação UTF-8
        // substitui por U+FFFD: a soma das contagens não descreve mais o texto final.
        var tokenizer = new AdversarialBpeTokenizer();
        const string whole = "a\U0001F600b";
        var left = whole[..2];
        var right = whole[2..];
        Assert.Multiple(() =>
        {
            Assert.That(tokenizer.Encode(left).Count + tokenizer.Encode(right).Count, Is.Not.EqualTo(tokenizer.Encode(whole).Count));
            Assert.That(new TokenizerBoundaryOracle(tokenizer).IsStableBoundary(left, right), Is.False);
        });
    }

    [Test]
    public void StableBoundaryIsApprovedAndKeepsTheCountExact()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var oracle = new TokenizerBoundaryOracle(tokenizer);
        string[] blocks = ["cat ", " tle"];
        var cache = new TokenizedBlockCache(tokenizer, oracle);
        var combined = cache.CountCombined(blocks);
        Assert.Multiple(() =>
        {
            Assert.That(oracle.IsStableBoundary(blocks[0], blocks[1]), Is.True);
            Assert.That(combined.IsExact, Is.True);
            Assert.That(combined.Tokens, Is.EqualTo(cache.CountAuthoritative(string.Concat(blocks)).Tokens));
        });
    }

    [Test]
    public void UnstableBoundaryIsRejectedAndMarksTheCountAsInexact()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var oracle = new TokenizerBoundaryOracle(tokenizer);
        string[] blocks = ["cat", "tle"];
        var cache = new TokenizedBlockCache(tokenizer, oracle);
        var combined = cache.CountCombined(blocks);
        Assert.Multiple(() =>
        {
            Assert.That(oracle.IsStableBoundary(blocks[0], blocks[1]), Is.False);
            Assert.That(combined.IsExact, Is.False);
            Assert.That(combined.Tokens, Is.GreaterThan(cache.CountAuthoritative(string.Concat(blocks)).Tokens));
        });
    }

    [Test]
    public void TheStructuralGateAloneWouldNotBeEnoughSoTheOracleVerifiesAgainstTheTokenizer()
    {
        // "cat " termina em espaço e passa na porta estrutural, mas o merge " "+"t" atravessa a fronteira:
        // só a verificação diferencial contra o tokenizador rejeita este caso.
        var tokenizer = new AdversarialBpeTokenizer();
        Assert.That(new TokenizerBoundaryOracle(tokenizer).IsStableBoundary("cat ", "tle"), Is.False);
    }

    [Test]
    public void EmptyBlocksAreAlwaysAStableBoundary()
    {
        var oracle = new TokenizerBoundaryOracle(new AdversarialBpeTokenizer());
        Assert.Multiple(() =>
        {
            Assert.That(oracle.IsStableBoundary("", "tle"), Is.True);
            Assert.That(oracle.IsStableBoundary("cat", ""), Is.True);
        });
    }

    [Test]
    public void CountingToTrimReusesTheCacheInsteadOfTokenizingTheSameBlockAgain()
    {
        var tokenizer = new CountingTokenizer(new AdversarialBpeTokenizer());
        var cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(new AdversarialBpeTokenizer()));
        string[] header = ["// coleção: pedidos\n"];
        string[] candidate = ["// coleção: pedidos\n", "db.pedidos.find({ status: 'novo' })\n"];
        var first = cache.CountCombined(header);
        var calls = tokenizer.EncodeCalls;
        var second = cache.CountCombined(candidate);
        Assert.Multiple(() =>
        {
            Assert.That(first.Tokens, Is.GreaterThan(0));
            Assert.That(calls, Is.EqualTo(1), "o primeiro bloco é tokenizado uma vez");
            Assert.That(tokenizer.EncodeCalls, Is.EqualTo(2), "apenas o bloco novo é tokenizado na segunda contagem");
            Assert.That(tokenizer.EncodedTexts, Does.Not.Contain(string.Concat(candidate)));
            Assert.That(second.Tokens, Is.GreaterThan(first.Tokens));
            Assert.That(cache.CachedBlocks, Is.EqualTo(2));
        });
    }

    [Test]
    public void TrimmingDiscardsCandidatesWithoutRetokenizingTheBlocksAlreadySeen()
    {
        var tokenizer = new CountingTokenizer(new AdversarialBpeTokenizer());
        var cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(new AdversarialBpeTokenizer()));
        string[] blocks = ["// bloco a\n", "// bloco b\n", "// bloco c\n"];
        var fitsAll = cache.FitsWithinBudget(blocks, 10, out var full);
        var callsAfterFullCandidate = tokenizer.EncodeCalls;
        var fitsTrimmed = cache.FitsWithinBudget(blocks[..2], 10, out var trimmed);
        Assert.Multiple(() =>
        {
            Assert.That(fitsAll, Is.False);
            Assert.That(full.Tokens, Is.GreaterThan(10));
            Assert.That(tokenizer.EncodeCalls, Is.EqualTo(callsAfterFullCandidate), "candidato menor reutiliza tudo do cache");
            Assert.That(fitsTrimmed, Is.EqualTo(trimmed.Tokens <= 10));
        });
    }

    [Test]
    public void TheAuthoritativeCountAlwaysMatchesADirectFullTokenizationEvenAfterUsingTheCache()
    {
        var tokenizer = new AdversarialBpeTokenizer();
        var cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(tokenizer));
        string[] blocks = ["cat", "tle 中", "文 informaçã", "o 😀", "😀 a​", "é"];
        for (var take = 1; take <= blocks.Length; take++) cache.FitsWithinBudget(blocks[..take], 1_000, out _);
        var final = string.Concat(blocks);
        var authoritative = cache.CountAuthoritative(final);
        var combined = cache.CountCombined(blocks);
        Assert.Multiple(() =>
        {
            Assert.That(authoritative, Is.EqualTo(TokenCount.Exact(tokenizer.Encode(final).Count)));
            Assert.That(combined.IsExact, Is.False, "as fronteiras deste candidato não são aprovadas");
            Assert.That(combined.Tokens, Is.Not.EqualTo(authoritative.Tokens), "a soma de blocos não é o limite; só a tokenização final é");
        });
    }

    [Test]
    public void TheCacheNeverExposesAWayToBuildAPromptByConcatenatingTokenIds()
    {
        // Invariante central do lote: o cache guarda números, não identificadores. Se alguém acrescentar um membro
        // que devolva ids (ou o tokenizador de onde tirá-los), este teste falha antes de o prompt virar concatenação.
        var members = typeof(TokenizedBlockCache).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var offenders = members.OfType<MethodInfo>().Select(method => method.ReturnType)
            .Concat(members.OfType<PropertyInfo>().Select(property => property.PropertyType))
            .Concat(members.OfType<FieldInfo>().Select(field => field.FieldType))
            .Where(ExposesTokenIds).Select(type => type.FullName!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty, "nenhum membro público pode devolver identificadores de tokens");
            Assert.That(typeof(TokenizedBlockCache).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
        });
    }

    private static bool ExposesTokenIds(Type type)
    {
        if (type == typeof(ITokenizer)) return true;
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type)) return false;
        var element = type.IsArray ? type.GetElementType() : type.GenericTypeArguments.FirstOrDefault();
        return element == typeof(int);
    }

    [Test]
    public void TokenCountRefusesNegativeValuesAndOnlyStaysExactWhenEveryBoundaryIsApproved()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new TokenCount(-1, true), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(TokenCount.Exact(3).Add(TokenCount.Exact(4), boundaryApproved: true), Is.EqualTo(TokenCount.Exact(7)));
            Assert.That(TokenCount.Exact(3).Add(TokenCount.Exact(4), boundaryApproved: false), Is.EqualTo(TokenCount.Estimated(7)));
            Assert.That(TokenCount.Estimated(3).Add(TokenCount.Exact(4), boundaryApproved: true), Is.EqualTo(TokenCount.Estimated(7)));
        });
    }
}
