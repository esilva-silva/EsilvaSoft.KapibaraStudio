using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Syntax;

/// <summary>Shared MongoLexer isolated from highlighting classification, over the Phase 1 synthetic Console script.</summary>
[MemoryDiagnoser]
public class MongoLexerBenchmarks
{
    private readonly List<MongoToken> _tokens = [];
    private string _text = "";

    [Params(1_024, 16_384, 65_536, 1_048_576)]
    public int DocumentCharacters { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = SyntheticWorkload.Script(DocumentCharacters);
        MongoLexer.Tokenize(_text, _tokens);
    }

    [Benchmark(Description = "MongoLexer.Tokenize (documento inteiro, lista reutilizada)")]
    public int Tokenize()
    {
        _tokens.Clear();
        MongoLexer.Tokenize(_text, _tokens);
        return _tokens.Count;
    }

    [Benchmark(Description = "MongoLexer por linha (sem materializar tokens)")]
    public int CountByLine()
    {
        var text = _text.AsSpan();
        var state = default(MongoLexerState);
        var count = 0;
        for (var start = 0; start < text.Length;)
        {
            var length = MongoLexer.LineLength(text, start);
            var lexer = new MongoLexer(text.Slice(start, length), state, offset: start);
            while (lexer.TryRead(out _)) count++;
            state = lexer.State;
            start += length;
        }
        return count;
    }
}
