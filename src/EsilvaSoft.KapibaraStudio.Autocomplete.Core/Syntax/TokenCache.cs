using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

/// <summary>Trabalho realizado por esta chamada de <see cref="TokenCache.GetOrLex"/>.</summary>
public enum TokenCacheLexKind : byte
{
    /// <summary>Os tokens da versão pedida já estavam em cache; nada foi lexificado nem lido do documento.</summary>
    Reused,
    /// <summary>O documento foi lido e lexificado, e o resultado ficou em cache como versão corrente.</summary>
    Lexed,
    /// <summary>
    /// O documento foi lido e lexificado só para este consumidor: a versão pedida já foi superada, então o
    /// resultado é correto para ela mas não substitui o cache da versão mais nova.
    /// </summary>
    Superseded
}

/// <summary>
/// Texto e tokens de uma versão de documento. <see cref="Text"/> é exatamente o texto de onde
/// <see cref="Tokens"/> saíram, para que índices de token e substrings nunca venham de versões diferentes.
/// </summary>
public sealed record DocumentTokens(
    TextSnapshotVersion Version, MongoLexerMode Mode, string Text, IReadOnlyList<MongoToken> Tokens, TokenCacheLexKind LexKind);

/// <summary>
/// Mantém a lexificação da última versão/modo pedidos por documento. É o cache do caminho por tecla: uma tecla
/// custa uma lexificação do documento (o mesmo custo de não ter cache), e toda análise repetida da mesma versão
/// — refiltro com a lista aberta, um segundo provedor, ghost text — custa zero, inclusive a materialização do texto.
/// Nenhum nó de árvore é construído aqui; quem precisar de nós usa <see cref="SyntaxTreeCache"/> sob demanda.
/// </summary>
/// <remarks>
/// Entradas são indexadas por DocumentId, então uma aba jamais recebe tokens de outra, mesmo com números de versão
/// iguais; a igualdade numérica sozinha não basta para ramos distintos, por isso a reutilização exige GetChangesSince
/// vazio. Uma versão já superada é lexificada para o consumidor sem repovoar o cache. Libere abas fechadas com
/// <see cref="RemoveDocument"/>: enquanto a entrada existir, o texto da versão corrente fica em memória.
/// </remarks>
public sealed class TokenCache
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Entry> _documents = [];

    public DocumentTokens GetOrLex(ITextSnapshot snapshot, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode is not (MongoLexerMode.Script or MongoLexerMode.Json))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo léxico inválido.");
        cancellationToken.ThrowIfCancellationRequested();
        var version = snapshot.Version;
        Entry? entry;
        // Nada é lexificado com o gate do cache na mão: uma aba não pode fazer as outras esperarem.
        lock (_gate)
        {
            if (_documents.TryGetValue(version.DocumentId, out var previous))
            {
                if (previous.Snapshot.Version.IsNewerThan(version)) entry = null;
                else if (previous.Mode == mode && IsSameSnapshot(snapshot, previous.Snapshot)) entry = previous;
                // A versão anterior do mesmo documento é a melhor estimativa de quantos tokens virão: com ela a
                // lexificação por tecla aloca um vetor do tamanho certo em vez de crescer por dobras sucessivas.
                else _documents[version.DocumentId] = entry = new(snapshot, mode, previous.Tokens?.Count ?? 0);
            }
            else _documents.Add(version.DocumentId, entry = new(snapshot, mode));
        }
        if (entry is null) return Lex(snapshot, mode, TokenCacheLexKind.Superseded, cancellationToken);

        // O gate pertence à versão, não ao documento: uma versão nova não espera a antiga terminar.
        // Esperas curtas deixam este consumidor cancelar sozinho, sem CTS compartilhado entre abas.
        while (!Monitor.TryEnter(entry.LexGate, millisecondsTimeout: 25))
            cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentTokens? reused = null;
            bool current;
            lock (_gate)
            {
                current = IsCurrent(entry);
                if (current && entry.Tokens is { } cached) reused = new(version, mode, entry.Text!, cached, TokenCacheLexKind.Reused);
            }
            if (reused is not null) return reused;
            if (!current) return Lex(snapshot, mode, TokenCacheLexKind.Superseded, cancellationToken);

            var lexed = Lex(snapshot, mode, TokenCacheLexKind.Lexed, cancellationToken, entry.ExpectedTokens);
            lock (_gate)
            {
                if (!IsCurrent(entry)) return lexed with { LexKind = TokenCacheLexKind.Superseded };
                entry.Text = lexed.Text;
                entry.Tokens = lexed.Tokens;
                return lexed;
            }
        }
        finally
        {
            Monitor.Exit(entry.LexGate);
        }
    }

    /// <summary>
    /// Libera texto e tokens do documento e invalida lexificações em andamento dele. Uma chamada posterior
    /// recomeça fria, como uma aba recém-aberta.
    /// </summary>
    public bool RemoveDocument(long documentId)
    {
        lock (_gate) return _documents.Remove(documentId);
    }

    private static DocumentTokens Lex(ITextSnapshot snapshot, MongoLexerMode mode, TokenCacheLexKind kind,
        CancellationToken cancellationToken, int expectedTokens = 0)
    {
        var text = snapshot.GetText(0, snapshot.Length);
        // Uma tecla muda pouquíssimos tokens: a contagem anterior mais uma folga evita realocar durante a leitura.
        var capacity = expectedTokens > 0
            ? Math.Min(expectedTokens + expectedTokens / 16 + 16, text.Length + 1)
            : Math.Min(text.Length / 3 + 8, 8192);
        var tokens = new List<MongoToken>(capacity);
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: mode, cancellationToken: cancellationToken);
        return new(snapshot.Version, mode, text, tokens, kind);
    }

    private bool IsCurrent(Entry entry) =>
        _documents.TryGetValue(entry.Snapshot.Version.DocumentId, out var current) && ReferenceEquals(current, entry);

    private static bool IsSameSnapshot(ITextSnapshot snapshot, ITextSnapshot previous) =>
        snapshot.Version == previous.Version && snapshot.Length == previous.Length &&
        (ReferenceEquals(snapshot, previous) || snapshot.GetChangesSince(previous) is { Count: 0 });

    private sealed class Entry(ITextSnapshot snapshot, MongoLexerMode mode, int expectedTokens = 0)
    {
        public object LexGate { get; } = new();
        /// <summary>Contagem de tokens da versão anterior do mesmo documento, usada só como capacidade inicial.</summary>
        public int ExpectedTokens { get; } = expectedTokens;
        public ITextSnapshot Snapshot { get; } = snapshot;
        public MongoLexerMode Mode { get; } = mode;
        public string? Text { get; set; }
        public IReadOnlyList<MongoToken>? Tokens { get; set; }
    }
}
