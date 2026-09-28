using System.Text;
using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Custo de transformar tokens em texto durante a geração, por número de tokens gerados (R42, incremento 4.2).
/// </summary>
/// <remarks>
/// <para><see cref="WholeSequencePerToken"/> reproduz o que o runtime fazia antes: a cada token, decodificar a
/// sequência inteira desde o início da geração e procurar a parada no texto todo — custo agregado quadrático.
/// <see cref="IncrementalPerToken"/> é o caminho atual: cada token é decodificado uma vez e só a cauda é examinada.</para>
/// <para>Mede a decodificação isolada, não a inferência: o laço nativo do ONNX domina o relógio e esconderia a
/// diferença. A equivalência com o runtime real é garantida pelos testes <c>Explicit</c> de modelo de verdade.</para>
/// <para>Rodar com <c>dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- --filter "*IncrementalDecode*"</c>.</para>
/// </remarks>
[MemoryDiagnoser]
public class IncrementalDecodeBenchmarks
{
    private const string Suffix = "});";
    private readonly ByteLevelTokenizer _tokenizer = new();
    private int[] _tokens = [];

    /// <summary>Tamanhos típicos de uma continuação inline, de uma linha até um bloco inteiro.</summary>
    [Params(10, 50, 200, 500)]
    public int TokenCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Texto com acentos e emoji: é onde a decodificação incremental precisa segurar bytes entre tokens.
        const string sample = "{ \"situação\": \"ativo\", \"país\": \"BR\", \"nota\": 9.5, \"marca\": \"😀\" }, ";
        var bytes = Encoding.UTF8.GetBytes(sample);
        _tokens = Enumerable.Range(0, TokenCount).Select(index => (int)bytes[index % bytes.Length]).ToArray();
    }

    /// <summary>Linha de base histórica: decodificação completa da sequência a cada token.</summary>
    [Benchmark(Baseline = true)]
    public int WholeSequencePerToken()
    {
        var length = 0;
        for (var count = 1; count <= _tokens.Length; count++)
        {
            var text = _tokenizer.Decode(_tokens.Take(count));
            if (text.Contains(Suffix, StringComparison.Ordinal)) break;
            length = text.Length;
        }
        return length;
    }

    /// <summary>Caminho atual: um token por vez, com verificação de parada só na cauda.</summary>
    [Benchmark]
    public int IncrementalPerToken()
    {
        using var decoder = ((ITokenizer)_tokenizer).CreateIncrementalDecoder();
        var length = 0;
        var tail = "";
        foreach (var token in _tokens)
        {
            var piece = decoder.Append(token);
            length += piece.Length;
            tail += piece;
            if (tail.Contains(Suffix, StringComparison.Ordinal)) break;
            if (tail.Length > Suffix.Length - 1) tail = tail[^(Suffix.Length - 1)..];
        }
        length += decoder.Flush().Length;
        return length;
    }

    /// <summary>Tokenizador byte-level mínimo: identificador 0–255 é o byte cru, como na base de um BPE real.</summary>
    private sealed class ByteLevelTokenizer : ITokenizer
    {
        public IReadOnlyList<int> Encode(string text) => Encoding.UTF8.GetBytes(text).Select(value => (int)value).ToArray();
        public string Decode(IEnumerable<int> tokens) => Encoding.UTF8.GetString(tokens.Select(token => (byte)token).ToArray());
        public IIncrementalDecoder CreateIncrementalDecoder() => new Decoder();

        private sealed class Decoder : IIncrementalDecoder
        {
            private readonly Utf8IncrementalBuffer _buffer = new();
            public string Append(int token) => _buffer.Append([(byte)token]);
            public string Flush() => _buffer.Flush();
            public void Dispose() { }
        }
    }
}
