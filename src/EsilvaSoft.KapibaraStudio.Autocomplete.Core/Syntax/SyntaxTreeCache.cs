using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

/// <summary>Trabalho realizado por esta chamada, inclusive quando seu resultado foi descartado.</summary>
public enum SyntaxTreeParseKind : byte
{
    None,
    Full,
    /// <summary>Reparse incremental da árvore, mantendo nós de statements não afetados.</summary>
    Incremental
}

/// <summary>
/// Árvore da versão solicitada, ou null quando superada. O consumidor ainda precisa conferir seu stamp
/// antes de aplicar na UI: uma versão pode mudar depois que esta chamada retorna.
/// </summary>
public sealed record SyntaxTreeCacheResult(
    TextSnapshotVersion Version, MongoLexerMode Mode, MongoSyntaxTree? Tree, SyntaxTreeParseKind ParseKind)
{
    public bool IsSuperseded => Tree is null;
    public bool IsReused => Tree is not null && ParseKind == SyntaxTreeParseKind.None;
}

/// <summary>
/// Mantém somente a última versão/modo solicitados por documento, com uma análise por vez para a mesma
/// versão e linhagem. DocumentId deve identificar o documento do editor (contrato de ITextSnapshot).
/// Versões e editores diferentes podem analisar em paralelo; resultados tardios não repovoam o cache.
/// Use em worker: a API é síncrona e não agenda trabalho na UI. Libere documentos fechados com
/// <see cref="RemoveDocument"/>. Snapshots e árvores ficam apenas em memória.
/// </summary>
/// <remarks>
/// GetChangesSince vazio comprova equivalência entre wrappers da mesma versão. Igualdade numérica
/// sozinha não basta para ramos distintos de StringTextSnapshot; história desconhecida exige análise
/// completa. Com alterações conhecidas, o parser preserva statements fora da região editada, mas a
/// lexificação ainda percorre o documento inteiro; checkpoints lexicais e métricas de desempenho ficam
/// para a próxima etapa.
/// </remarks>
public sealed class SyntaxTreeCache
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Entry> _documents = [];
    private readonly TolerantParser _parser;

    public SyntaxTreeCache(TolerantParser? parser = null) => _parser = parser ?? new TolerantParser();

    public SyntaxTreeCacheResult GetOrParse(ITextSnapshot snapshot, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode is not (MongoLexerMode.Script or MongoLexerMode.Json))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo léxico inválido.");
        cancellationToken.ThrowIfCancellationRequested();
        var version = snapshot.Version;
        Entry entry;
        lock (_gate)
        {
            if (_documents.TryGetValue(version.DocumentId, out var previous))
            {
                if (previous.Snapshot.Version.IsNewerThan(version))
                    return new(version, mode, null, SyntaxTreeParseKind.None);

                if (previous.Mode == mode && IsSameSnapshot(snapshot, previous.Snapshot))
                    entry = previous;
                else
                {
                    var changes = previous.Mode == mode && previous.Tree is not null ? snapshot.GetChangesSince(previous.Snapshot) : null;
                    _documents[version.DocumentId] = entry = new(snapshot, mode, previous.Tree, previous.Snapshot, changes);
                }
            }
            else
                _documents.Add(version.DocumentId, entry = new(snapshot, mode));
        }

        // O gate pertence à versão/linhagem, não ao documento: uma versão nova não espera a antiga.
        // Esperas curtas permitem cancelar só este consumidor, sem CTS compartilhado.
        while (!Monitor.TryEnter(entry.ParseGate, millisecondsTimeout: 25))
            cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!IsCurrent(entry)) return new(version, mode, null, SyntaxTreeParseKind.None);
                if (entry.Tree is { } cached) return new(version, mode, cached, SyntaxTreeParseKind.None);
            }

            var previousTree = entry.PreviousTree;
            var previousSnapshot = entry.PreviousSnapshot;
            var changes = entry.Changes;
            // O mapeamento atual de coordenadas é seguro para uma edição por linhagem. Históricos
            // acumulados aguardam o algoritmo peça a peça; até lá, preservamos a correção com full parse.
            var isIncremental = previousTree is not null && previousSnapshot is not null && changes is { Count: 1 };
            MongoSyntaxTree tree;
            if (isIncremental)
                tree = _parser.ParseIncremental(previousSnapshot!, previousTree!, snapshot, changes!, mode, cancellationToken);
            else
                tree = _parser.Parse(snapshot, mode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!IsCurrent(entry)) return new(version, mode, null, SyntaxTreeParseKind.Full);
                entry.Tree = tree;
                return new(version, mode, tree, isIncremental ? SyntaxTreeParseKind.Incremental : SyntaxTreeParseKind.Full);
            }
        }
        finally
        {
            Monitor.Exit(entry.ParseGate);
        }
    }

    /// <summary>
    /// Libera a árvore/snapshot do documento e invalida análises em andamento. O proprietário deve
    /// parar de enviar pedidos do documento fechado; uma chamada futura inicia uma nova entrada.
    /// </summary>
    public bool RemoveDocument(long documentId)
    {
        lock (_gate) return _documents.Remove(documentId);
    }

    private bool IsCurrent(Entry entry) =>
        _documents.TryGetValue(entry.Snapshot.Version.DocumentId, out var current) && ReferenceEquals(current, entry);

    private static bool IsSameSnapshot(ITextSnapshot snapshot, ITextSnapshot previous) =>
        snapshot.Version == previous.Version && snapshot.Length == previous.Length &&
        (ReferenceEquals(snapshot, previous) || snapshot.GetChangesSince(previous) is { Count: 0 });

    private sealed class Entry(ITextSnapshot snapshot, MongoLexerMode mode, MongoSyntaxTree? previousTree = null,
        ITextSnapshot? previousSnapshot = null, IReadOnlyList<TextChange>? changes = null)
    {
        public object ParseGate { get; } = new();
        public ITextSnapshot Snapshot { get; } = snapshot;
        public MongoLexerMode Mode { get; } = mode;
        public MongoSyntaxTree? Tree { get; set; }
        public MongoSyntaxTree? PreviousTree { get; } = previousTree;
        public ITextSnapshot? PreviousSnapshot { get; } = previousSnapshot;
        public IReadOnlyList<TextChange>? Changes { get; } = changes;
    }
}
