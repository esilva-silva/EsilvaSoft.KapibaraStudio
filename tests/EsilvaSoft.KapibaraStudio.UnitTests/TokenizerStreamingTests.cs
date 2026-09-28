using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.UnitTests.TokenCounting;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Decodificação incremental (R42): o texto sai conforme os tokens chegam, sem redecodificar a sequência, e sem
/// nunca entregar metade de um caractere.
/// </summary>
/// <remarks>
/// A equivalência com a decodificação em lote é o contrato que sustenta o critério "streaming idêntico ao não
/// streaming": se a concatenação dos pedaços divergisse do lote, a prévia inline mostraria um texto e a aplicação
/// inseriria outro.
/// </remarks>
[TestFixture]
public sealed class TokenizerStreamingTests
{
    private static readonly string[] Texts =
    [
        "db.getCollection(\"clientes\").find({})",
        "informação de endereço não encontrada",
        "中文注释与分词",
        "status: 😀 concluído 🎉",
        "é decomposto e a​ de largura zero"
    ];

    /// <summary>Byte a byte é o pior caso possível: todo caractere multibyte nasce partido.</summary>
    [Test]
    public void ByteAtATimeNeverEmitsHalfOfACharacter([ValueSource(nameof(Texts))] string text)
    {
        var buffer = new Utf8IncrementalBuffer();
        var assembled = new StringBuilder();
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            var piece = buffer.Append([value]);
            Assert.That(piece, Does.Not.Contain("�"), "Nenhum pedaço intermediário pode conter bytes inválidos.");
            if (piece.Length > 0) Assert.That(char.IsHighSurrogate(piece[^1]), Is.False, "Um par substituto nunca sai partido entre pedaços.");
            assembled.Append(piece);
        }
        assembled.Append(buffer.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(text));
        Assert.That(buffer.PendingBytes, Is.Zero);
    }

    /// <summary>Bytes inválidos precisam produzir exatamente os mesmos <c>U+FFFD</c> que a decodificação em lote.</summary>
    [TestCase(new byte[] { 0x61, 0xE0, 0x41, 0x62 }, TestName = "SequenciaTruncadaPorAsciiSeguinte")]
    [TestCase(new byte[] { 0x80, 0x61 }, TestName = "ContinuacaoSolta")]
    [TestCase(new byte[] { 0xC0, 0x80 }, TestName = "SobrecargaProibida")]
    [TestCase(new byte[] { 0xF5, 0x90, 0x80, 0x80 }, TestName = "AcimaDoPlanoMaximo")]
    [TestCase(new byte[] { 0xED, 0xA0, 0x80 }, TestName = "SubstitutoCodificado")]
    [TestCase(new byte[] { 0x61, 0xF0, 0x9F }, TestName = "EmojiTruncadoNoFim")]
    public void InvalidBytesDecodeExactlyLikeTheBatchDecoder(byte[] bytes)
    {
        var buffer = new Utf8IncrementalBuffer();
        var assembled = new StringBuilder();
        foreach (var value in bytes) assembled.Append(buffer.Append([value]));
        assembled.Append(buffer.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(Encoding.UTF8.GetString(bytes)));
    }

    /// <summary>O decodificador padrão (sem streaming nativo) continua correto para tokenizers antigos e falsos.</summary>
    [Test]
    public void TheBatchFallbackDecoderMatchesTheBatchDecoding([ValueSource(nameof(Texts))] string text)
    {
        ITokenizer tokenizer = new AdversarialBpeTokenizer();
        var tokens = tokenizer.Encode(text);
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var assembled = new StringBuilder();
        foreach (var token in tokens)
        {
            var piece = decoder.Append(token);
            if (piece.Length > 0) Assert.That(char.IsHighSurrogate(piece[^1]), Is.False);
            assembled.Append(piece);
        }
        assembled.Append(decoder.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(tokenizer.Decode(tokens)));
    }

    /// <summary>O DeepSeek é BPE em .NET puro: a decodificação incremental é nossa, e precisa bater com o lote.</summary>
    [Test]
    public void TheDeepSeekDecoderStreamsFragmentedCharacters([ValueSource(nameof(Texts))] string text)
    {
        ITokenizer tokenizer = new DeepSeekModelTokenizer(FabricatedByteLevelTokenizer());
        var tokens = tokenizer.Encode(text);
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var assembled = new StringBuilder();
        foreach (var token in tokens)
        {
            var piece = decoder.Append(token);
            Assert.That(piece, Does.Not.Contain("�"));
            assembled.Append(piece);
        }
        assembled.Append(decoder.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(tokenizer.Decode(tokens)));
        Assert.That(assembled.ToString(), Is.EqualTo(text));
    }

    /// <summary>Marcadores adicionados não são bytes: entram inteiros no fluxo, sem estragar o caractere seguinte.</summary>
    [Test]
    public void TheDeepSeekDecoderKeepsAddedMarkersWholeBetweenFragmentedCharacters()
    {
        ITokenizer tokenizer = new DeepSeekModelTokenizer(FabricatedByteLevelTokenizer());
        // "ç" partido ao meio pelo marcador não existe em geração real; o que existe é marcador entre caracteres.
        var tokens = tokenizer.Encode("ção").Concat([EotId]).Concat(tokenizer.Encode("中")).ToArray();
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var assembled = new StringBuilder();
        foreach (var token in tokens) assembled.Append(decoder.Append(token));
        assembled.Append(decoder.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(tokenizer.Decode(tokens)));
        Assert.That(assembled.ToString(), Is.EqualTo("ção<|EOT|>中"));
    }

    /// <summary>
    /// A conta que motiva o lote: redecodificar tudo a cada token cresce com o quadrado da geração; incremental, não.
    /// A medida é determinística (peças visitadas), não tempo de parede.
    /// </summary>
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(200)]
    [TestCase(500)]
    public void IncrementalDecodingVisitsEachTokenOnce(int count)
    {
        var tokenizer = new CountingDecodeTokenizer(new AdversarialBpeTokenizer());
        var tokens = Enumerable.Range(0, count).Select(index => (int)"abcdefghij"[index % 10]).ToArray();

        using (var decoder = new BatchFallbackIncrementalDecoder(tokenizer))
        {
            foreach (var token in tokens) decoder.Append(token);
            decoder.Flush();
        }
        // n(n+1)/2 + n da descarga: é exatamente o custo que o runtime pagava por token antes de R42.
        Assert.That(tokenizer.DecodedTokens, Is.EqualTo(count * (count + 1) / 2 + count));

        tokenizer.Reset();
        ITokenizer streaming = new StreamingDecodeTokenizer(tokenizer);
        using (var decoder = streaming.CreateIncrementalDecoder())
        {
            foreach (var token in tokens) decoder.Append(token);
            decoder.Flush();
        }
        Assert.That(tokenizer.DecodedTokens, Is.EqualTo(count), "Cada token é decodificado uma única vez.");
    }

    /// <summary>Id do único token adicionado do tokenizer fabricado.</summary>
    private const int EotId = 256;

    /// <summary>
    /// <c>tokenizer.json</c> mínimo aceito pelo <see cref="DeepSeekModelTokenizer"/>: ByteLevel sem merges, com um
    /// marcador adicionado. Evita depender do pacote externo para exercitar a decodificação incremental.
    /// </summary>
    private static string FabricatedByteLevelTokenizer()
    {
        var characters = ByteLevelCharacters();
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var value = 0; value < 256; value++) vocabulary[characters[value].ToString()] = value;
        var document = new
        {
            normalizer = (object?)null,
            pre_tokenizer = new { pretokenizers = new[] { new { type = "ByteLevel", add_prefix_space = false, use_regex = false } } },
            added_tokens = new[] { new { content = "<|EOT|>", id = EotId } },
            model = new { type = "BPE", byte_fallback = false, unk_token = (object?)null, vocab = vocabulary, merges = Array.Empty<string>() }
        };
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, $"deepseek-fabricado-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return path;
    }

    /// <summary>Mesma tabela ByteLevel do GPT-2 usada pelo tokenizer real.</summary>
    private static char[] ByteLevelCharacters()
    {
        var result = new char[256];
        var next = 256;
        for (var value = 0; value < result.Length; value++)
            result[value] = (char)(value is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 and <= 255 ? value : next++);
        return result;
    }

    /// <summary>Conta quantos tokens passaram por <see cref="ITokenizer.Decode"/>, somando todas as chamadas.</summary>
    private sealed class CountingDecodeTokenizer(ITokenizer inner) : ITokenizer
    {
        public int DecodedTokens { get; private set; }
        public void Reset() => DecodedTokens = 0;
        public IReadOnlyList<int> Encode(string text) => inner.Encode(text);
        public string Decode(IEnumerable<int> tokens)
        {
            var materialized = tokens.ToArray();
            DecodedTokens += materialized.Length;
            return inner.Decode(materialized);
        }
    }

    /// <summary>
    /// Tokenizer cujo decodificador incremental decodifica um token por vez. É o mínimo necessário para a contagem
    /// deste teste (ids ASCII); os tokenizers do produto decodificam peças em bytes, sem passar por <c>Decode</c>.
    /// </summary>
    private sealed class StreamingDecodeTokenizer(ITokenizer inner) : ITokenizer
    {
        public IReadOnlyList<int> Encode(string text) => inner.Encode(text);
        public string Decode(IEnumerable<int> tokens) => inner.Decode(tokens);
        public IIncrementalDecoder CreateIncrementalDecoder() => new Decoder(inner);

        private sealed class Decoder(ITokenizer tokenizer) : IIncrementalDecoder
        {
            private readonly Utf8IncrementalBuffer _buffer = new();
            private readonly int[] _single = new int[1];
            public string Append(int token)
            {
                _single[0] = token;
                return _buffer.Append(Encoding.UTF8.GetBytes(tokenizer.Decode(_single)));
            }
            public string Flush() => _buffer.Flush();
            public void Dispose() { }
        }
    }
}
