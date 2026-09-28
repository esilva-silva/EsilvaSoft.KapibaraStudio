using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks;

/// <summary>
/// Autocomplete work the current editor performs synchronously for one editor event (keystroke or caret move),
/// measured before the Phase 2 pipeline replaces it. Documents are synthetic Console scripts.
/// </summary>
[MemoryDiagnoser]
public class BaselineBenchmarks
{
    private readonly SyntaxHighlightingService _highlighting = new();
    private readonly AutocompleteService _cacheKeyOnly = new();
    private readonly AutocompleteSettings _settings = new();
    private string _prefix = "";
    private string _suffix = "";
    private SyntaxContext _context = SyntaxContext.Empty;
    private string[] _documents = [];
    private AutocompleteContextSnapshot _snapshot = null!;
    private AutocompleteRequest _request = null!;

    [Params(1_024, 16_384, 65_536)]
    public int DocumentCharacters { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var script = SyntheticWorkload.Script(DocumentCharacters);
        var caret = script.Length - 3;
        _prefix = script[..caret];
        _suffix = script[caret..];
        _context = new([new("Dev"), new("Dev", "loja"), new("Dev", "loja", "clientes")], "Dev", "loja", "clientes");
        // The tab infers fields from up to eight result documents of up to 64 KiB each.
        _documents = SyntheticWorkload.Documents(8, DocumentCharacters);
        var names = Enumerable.Range(0, 128).Select(SyntheticWorkload.FieldName).ToArray();
        _snapshot = new(script, caret, "Mongo Console JavaScript", "{\"Status\":\"ativo\"}", names, names, ["db.clientes.find({ Status: \"ativo\" })"]);
        _request = AutocompleteContextBuilder.Build(_snapshot, _settings);
        await _cacheKeyOnly.ConfigureAsync(_settings with { UseDictionary = false });
    }

    [Benchmark(Description = "MongoCompletionTarget.Resolve (re-lex do prefixo)")]
    public object? ResolveTarget() => MongoCompletionTarget.Resolve(_prefix, _context);

    [Benchmark(Description = "Highlight completo do documento")]
    public int HighlightDocument() => _highlighting.Highlight(_prefix + _suffix, SyntaxLanguage.MongoScript, _context).Tokens.Count;

    [Benchmark(Description = "InferFieldPaths (8 documentos)")]
    public int InferResultFields() => MqlAutocompleteService.InferFieldPaths(_documents).Count;

    [Benchmark(Description = "AutocompleteContextBuilder.Build")]
    public int BuildLegacyContext() => AutocompleteContextBuilder.Build(_snapshot, _settings).Context.Length;

    [Benchmark(Description = "BasicAutocompleteProvider (dicionário imediato)")]
    public object? ImmediateDictionary() => BasicAutocompleteProvider.GetCompletion(_request);

    [Benchmark(Description = "CompletionPrivacy no prefixo+sufixo")]
    public bool PrivacyScan() => CompletionPrivacy.ContainsSensitiveText(_prefix + _suffix);

    [Benchmark(Description = "GetCompletionAsync sem dicionário (JSON + SHA-256)")]
    public Task<AutocompleteResult?> CacheKey() => _cacheKeyOnly.GetCompletionAsync(_request);
}
