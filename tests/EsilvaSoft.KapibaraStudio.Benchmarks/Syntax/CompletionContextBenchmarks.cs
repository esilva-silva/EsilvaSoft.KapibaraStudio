using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Syntax;

/// <summary>Onde o caret fica no documento sintético: início, meio e fim (posição de digitação real).</summary>
public enum CaretPlace { Start, Middle, End }

/// <summary>
/// Custo de construir o contexto de autocompletar sem cache, com o <see cref="TokenCache"/> (caminho ativo) e com a
/// árvore do <see cref="SyntaxTreeCache"/> (reservado a quem precisar de nós).
/// A linha de base é a análise que lexifica o documento inteiro a cada chamada (o caminho anterior ao lote W2a);
/// as demais medem o refiltro com a lista aberta (mesma versão) e a digitação (uma tecla por análise).
/// O custo de produzir o novo snapshot está medido à parte em <see cref="EditOnly"/> para poder ser descontado.
/// </summary>
[MemoryDiagnoser]
public class CompletionContextBenchmarks
{
    private readonly SyntaxTreeCache _trees = new();
    private readonly TokenCache _tokenCache = new();
    private StringTextSnapshot _snapshot = new("");
    private StringTextSnapshot _typing = new("");
    private StringTextSnapshot _plainTyping = new("");
    private List<MongoToken> _tokens = [];
    private string _text = "";
    private int _caret;
    private bool _inserted;
    private bool _typingInserted;
    private bool _plainInserted;

    [Params(1_024, 16_384, 65_536, 1_048_576)]
    public int DocumentCharacters { get; set; }

    [Params(CaretPlace.Start, CaretPlace.Middle, CaretPlace.End)]
    public CaretPlace Caret { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = SyntheticWorkload.Script(DocumentCharacters);
        _snapshot = new StringTextSnapshot(_text);
        _typing = _snapshot;
        _plainTyping = _snapshot;
        _caret = Caret switch { CaretPlace.Start => 0, CaretPlace.Middle => _text.Length / 2, _ => _text.Length };
        _tokens = [];
        MongoLexer.Tokenize(_text.AsSpan(), _tokens);
        _ = _trees.GetOrParse(_snapshot);
        _ = _tokenCache.GetOrLex(_snapshot);
        _inserted = false;
        _typingInserted = false;
        _plainInserted = false;
    }

    [Benchmark(Baseline = true, Description = "Analyze sem árvore (lexifica o documento inteiro por análise)")]
    public int WithoutTree() => (int)CompletionContextEngine.Analyze(Request(_snapshot), trees: null).Role;

    /// <summary>
    /// Linha de base honesta da digitação: a mesma edição de snapshot dos cenários com cache, analisada pelo caminho
    /// anterior ao lote W2a. Comparar com <see cref="WithoutTree"/> mede análise com edição contra análise sem edição.
    /// </summary>
    [Benchmark(Description = "Analyze após uma tecla sem cache (linha de base por tecla)")]
    public int KeystrokeWithoutCache()
    {
        _plainTyping = _plainInserted ? _plainTyping.Remove(_caret, 1) : _plainTyping.Insert(_caret, "a");
        _plainInserted = !_plainInserted;
        return (int)CompletionContextEngine.Analyze(new ContextRequest(_plainTyping, _caret, EditorDialects.Console, Scope)).Role;
    }

    [Benchmark(Description = "Analyze reutilizando os tokens da mesma versão (lista aberta/refiltro)")]
    public int ReusedTokens() => (int)CompletionContextEngine.Analyze(Request(_snapshot), _tokenCache).Role;

    [Benchmark(Description = "Analyze após uma tecla pelo TokenCache (caminho ativo)")]
    public int KeystrokeTokens()
    {
        _typing = _typingInserted ? _typing.Remove(_caret, 1) : _typing.Insert(_caret, "a");
        _typingInserted = !_typingInserted;
        return (int)CompletionContextEngine.Analyze(new ContextRequest(_typing, _caret, EditorDialects.Console, Scope), _tokenCache).Role;
    }

    [Benchmark(Description = "Analyze reutilizando a árvore da mesma versão (lista aberta/refiltro)")]
    public int ReusedTree() => (int)CompletionContextEngine.Analyze(Request(_snapshot), _trees).Role;

    [Benchmark(Description = "Analyze após uma tecla (reparse pelo SyntaxTreeCache)")]
    public int Keystroke()
    {
        _typing = _inserted ? _typing.Remove(_caret, 1) : _typing.Insert(_caret, "a");
        _inserted = !_inserted;
        return (int)CompletionContextEngine.Analyze(new ContextRequest(_typing, _caret, EditorDialects.Console, Scope), _trees).Role;
    }

    [Benchmark(Description = "Só a edição do snapshot, sem análise (para descontar do cenário anterior)")]
    public int EditOnly()
    {
        _typing = _inserted ? _typing.Remove(_caret, 1) : _typing.Insert(_caret, "a");
        _inserted = !_inserted;
        return _typing.Length;
    }

    [Benchmark(Description = "Alvo a partir do texto (lexifica de novo; acima de 64 KiB devolve Unknown)")]
    public int TargetFromText() => (int)NamespaceTargetResolver.Resolve(_text, _caret, null, EditorDialects.Console).Confidence;

    [Benchmark(Description = "Alvo a partir dos tokens já existentes (acima de 64 KiB devolve Unknown)")]
    public int TargetFromTokens() => (int)NamespaceTargetResolver.Resolve(_text, _tokens, _caret, null, EditorDialects.Console).Confidence;

    private ContextRequest Request(ITextSnapshot snapshot) => new(snapshot, _caret, EditorDialects.Console, Scope);

    private static CatalogScope Scope { get; } =
        new(new ConnectionIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"), "localhost", "hash"), "app", "clientes");
}
