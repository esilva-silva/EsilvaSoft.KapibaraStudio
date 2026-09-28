using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Syntax;

/// <summary>
/// Highlighting of a synthetic Console script (same generator as the Phase 1 baseline), measured before and after the
/// MongoLexer extraction. Full: no previous snapshot. Incremental: one character inserted in the middle line.
/// </summary>
[MemoryDiagnoser]
public class SyntaxHighlightBenchmarks
{
    private readonly SyntaxHighlightingService _highlighting = new();
    private readonly SyntaxContext _context = new([new("Dev"), new("Dev", "loja"), new("Dev", "loja", "clientes")], "Dev", "loja", "clientes");
    private string _text = "";
    private string _edited = "";
    private SyntaxSnapshot _previous = null!;

    [Params(1_024, 16_384, 65_536, 1_048_576)]
    public int DocumentCharacters { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = SyntheticWorkload.Script(DocumentCharacters);
        var middle = _text.IndexOf('\n', _text.Length / 2) + 1;
        _edited = _text.Insert(middle, "x");
        _previous = _highlighting.Highlight(_text, SyntaxLanguage.MongoScript, _context);
    }

    [Benchmark(Description = "Highlight completo (sem snapshot anterior)")]
    public int Full() => _highlighting.Highlight(_text, SyntaxLanguage.MongoScript, _context).Tokens.Count;

    [Benchmark(Description = "Highlight incremental (1 caractere no meio)")]
    public int Incremental() => _highlighting.Highlight(_edited, SyntaxLanguage.MongoScript, _context, _previous).Tokens.Count;
}
