using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed class CompletionRanker
{
    private readonly RankingProfile _profile;
    private readonly CompletionUsageTracker? _usage;

    /// <summary>
    /// O rastreador de uso é opcional: sem ele o ranking permanece puramente determinístico pelo texto, que é a base
    /// exigida do produto. Quando informado, o termo de uso entra apenas nas consultas que trazem contexto.
    /// </summary>
    public CompletionRanker(RankingProfile? profile = null, CompletionUsageTracker? usage = null)
    {
        _profile = profile ?? new();
        _usage = usage;
    }

    /// <summary>
    /// Ordena sem nenhum sinal de sessão: esta sobrecarga não tem contexto, então não aplica uso nem tipo.
    /// </summary>
    public IReadOnlyList<CompletionItem> Rank(IEnumerable<CompletionItem> items, string prefix, int maximum,
        CancellationToken cancellationToken = default) => Rank(items, prefix, maximum, usage: null, valueTypes: null, cancellationToken);

    /// <summary>
    /// Ordena aplicando também o sinal de uso recente da sessão, quando o contexto identifica conexão, banco, coleção
    /// e forma. Se qualquer componente dessa identidade faltar, o termo de uso é simplesmente omitido.
    /// </summary>
    public IReadOnlyList<CompletionItem> Rank(IEnumerable<CompletionItem> items, CompletionContext context, int maximum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Rank(items, context.Prefix, maximum, UsageScope.TryCreate(_usage, context), context.ValueTypes, cancellationToken);
    }

    private CompletionItem[] Rank(IEnumerable<CompletionItem> items, string prefix, int maximum,
        UsageScope? usage, IReadOnlySet<string>? valueTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        // Do not preallocate maximum: a lazy sequence can have a very large requested cap.
        // The list grows only to the number of retained candidates.
        var heap = new List<Candidate>(Math.Min(maximum, 16));
        var ordinal = 0L;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryMatch(item.FilterText, prefix, out var match, out var matchScore))
            {
                ordinal++;
                continue;
            }

            var score = matchScore + SourceScore(item.Source);
            if (item.Tags.HasFlag(CompletionItemTags.Deprecated)) score -= _profile.DeprecatedPenalty;
            if (item.Tags.HasFlag(CompletionItemTags.Stale)) score -= _profile.StalePenalty;
            if (item.Tags.HasFlag(CompletionItemTags.Write)) score -= _profile.WritePenalty;
            score += ContextualPriority(item, valueTypes);
            if (HasTypeMismatch(item.ApplicableTypes, valueTypes)) score -= _profile.TypeMismatchPenalty;

            // O termo de uso soma depois das penalidades e antes do heap: ele muda a posição, nunca a sobrevivência
            // do candidato, e o desempate continua sendo (match, rótulo, símbolo, ordem de entrada).
            if (usage is { } scope) score += _profile.UsageWeight * scope.UsageOf(item.SymbolId);

            // Highlights are not materialized here: only (score, match, label, symbolId, ordinal) drive the
            // heap, so the array allocation is deferred until the final top-K survivors are known below.
            var candidate = new Candidate(item, match, score, ordinal++);
            if (heap.Count < maximum)
            {
                heap.Add(candidate);
                SiftUpWorstFirst(heap, heap.Count - 1);
            }
            else if (CompareForOutput(candidate, heap[0]) < 0)
            {
                heap[0] = candidate;
                SiftDownWorstFirst(heap, 0);
            }
        }

        heap.Sort(CompareForOutput);
        var result = new CompletionItem[heap.Count];
        for (var index = 0; index < heap.Count; index++)
        {
            var candidate = heap[index];
            var highlights = BuildHighlights(candidate.Item.FilterText, prefix, candidate.Match);
            result[index] = candidate.Item with { Score = candidate.Score, Highlights = highlights };
        }
        return result;
    }

    private double SourceScore(CompletionSource source) => source switch
    {
        CompletionSource.Catalog => _profile.SourceCatalog,
        CompletionSource.Schema => _profile.SourceSchema,
        CompletionSource.Snippet => _profile.SourceSnippet,
        _ => 0
    };

    private static bool HasTypeMismatch(IReadOnlyList<string> applicableTypes, IReadOnlySet<string>? valueTypes) =>
        applicableTypes.Count > 0 && valueTypes is { Count: > 0 } &&
        !applicableTypes.Any(valueTypes.Contains);

    private static double ContextualPriority(CompletionItem item, IReadOnlySet<string>? valueTypes)
    {
        if (valueTypes is { Count: > 0 } && item.CatalogKind == SymbolKind.BsonConstructor
            && ConstructorTypes(item.Label).Any(valueTypes.Contains)) return 240;
        if (item.CatalogKind != SymbolKind.CollectionMethod) return 0;
        return item.Label switch
        {
            "find" => 30,
            "findOne" => 25,
            "countDocuments" => 20,
            "distinct" => 15,
            "aggregate" => 10,
            _ => 0
        };
    }

    private static IReadOnlyList<string> ConstructorTypes(string name) => name switch
    {
        "ObjectId" => ["objectId"],
        "UUID" or "GUUID" or "CGUUID" or "JUUID" => ["uuid"],
        "ISODate" => ["date"],
        "Decimal128" or "NumberDecimal" => ["decimal"],
        "Long" or "NumberLong" => ["long"],
        "Int32" or "NumberInt" => ["int"],
        "Double" => ["double"],
        "BinData" => ["binData"],
        _ => []
    };

    /// <summary>Compares candidates in their final, best-to-worst output order.</summary>
    private static int CompareForOutput(Candidate left, Candidate right)
    {
        var comparison = right.Score.CompareTo(left.Score);
        if (comparison != 0) return comparison;

        comparison = MatchOrder(left.Match).CompareTo(MatchOrder(right.Match));
        if (comparison != 0) return comparison;

        comparison = left.Item.Label.Length.CompareTo(right.Item.Label.Length);
        if (comparison != 0) return comparison;

        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Item.Label, right.Item.Label);
        if (comparison != 0) return comparison;

        comparison = StringComparer.Ordinal.Compare(left.Item.SymbolId, right.Item.SymbolId);
        return comparison != 0 ? comparison : left.Ordinal.CompareTo(right.Ordinal);
    }

    private static void SiftUpWorstFirst(List<Candidate> heap, int child)
    {
        while (child > 0)
        {
            var parent = (child - 1) / 2;
            if (!IsWorse(heap[child], heap[parent])) return;
            (heap[child], heap[parent]) = (heap[parent], heap[child]);
            child = parent;
        }
    }

    private static void SiftDownWorstFirst(List<Candidate> heap, int parent)
    {
        while (true)
        {
            var left = (parent * 2) + 1;
            if (left >= heap.Count) return;

            var worstChild = left;
            var right = left + 1;
            if (right < heap.Count && IsWorse(heap[right], heap[left])) worstChild = right;
            if (!IsWorse(heap[worstChild], heap[parent])) return;

            (heap[parent], heap[worstChild]) = (heap[worstChild], heap[parent]);
            parent = worstChild;
        }
    }

    private static bool IsWorse(Candidate left, Candidate right) => CompareForOutput(left, right) > 0;

    private static int MatchOrder(CatalogMatch match) => match switch
    {
        CatalogMatch.Any => 0,
        CatalogMatch.Prefix => 1,
        CatalogMatch.Humps => 2,
        _ => 3
    };

    /// <summary>
    /// Só classifica o tipo de match e a pontuação. Não constrói realces: eles são caros (uma alocação por
    /// candidato que casa) e a maioria dos candidatos analisados nunca chega ao top-K exibido.
    /// </summary>
    private bool TryMatch(string value, string prefix, out CatalogMatch match, out double score)
    {
        match = CatalogMatch.Any;
        score = _profile.ExactMatch;
        if (prefix.Length == 0) return true;
        if (value.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            match = CatalogMatch.Prefix; score = _profile.PrefixMatch + prefix.Length;
            return true;
        }
        if (TryCamelHumps(value, prefix, highlights: null))
        {
            match = CatalogMatch.Humps; score = _profile.CamelHumpMatch + prefix.Length;
            return true;
        }
        if (value.Contains(prefix, StringComparison.OrdinalIgnoreCase))
        {
            match = CatalogMatch.Substring; score = _profile.SubstringMatch + prefix.Length;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reconstrói os realces de um candidato sobrevivente a partir de (valor, prefixo, tipo de match), sem
    /// depender de estado guardado durante a fase de match: o resultado é determinístico para os mesmos
    /// argumentos, então recalcular é mais barato do que carregar arrays não usados pelos descartados do heap.
    /// </summary>
    private static TextSpan[] BuildHighlights(string value, string prefix, CatalogMatch match) => match switch
    {
        // CatalogMatch.Any cobre tanto "prefixo vazio" (sem realce) quanto "igualdade exata" (realce total);
        // TryMatch só chega em Any com prefixo não vazio quando value == prefix.
        CatalogMatch.Any => prefix.Length == 0 ? [] : OneHighlight(0, value.Length),
        CatalogMatch.Prefix => OneHighlight(0, prefix.Length),
        CatalogMatch.Humps => CamelHighlights(value, prefix),
        CatalogMatch.Substring => OneHighlight(value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase), prefix.Length),
        _ => []
    };

    private static TextSpan[] OneHighlight(int start, int length) => [new(start, length)];

    private static TextSpan[] CamelHighlights(string value, string prefix)
    {
        var highlights = new TextSpan[prefix.Length];
        TryCamelHumps(value, prefix, highlights);
        return highlights;
    }

    /// <summary>
    /// Varredura única de camel humps: quando <paramref name="highlights"/> é nulo, só confirma o match (sem
    /// alocar); quando informado, preenche as posições casadas no mesmo laço. Antes havia duas varreduras
    /// separadas (uma para detectar, outra para extrair os realces) sempre que o candidato casava.
    /// </summary>
    private static bool TryCamelHumps(string value, string prefix, TextSpan[]? highlights)
    {
        var next = 0;
        for (var index = 0; index < value.Length && next < prefix.Length; index++)
        {
            if (IsCamelBoundary(value, index) && char.ToUpperInvariant(value[index]) == char.ToUpperInvariant(prefix[next]))
            {
                if (highlights is not null) highlights[next] = new TextSpan(index, 1);
                next++;
            }
        }
        return next == prefix.Length;
    }

    private static bool IsCamelBoundary(string value, int index) =>
        index == 0 || value[index - 1] is '_' or '-' or '.' || char.IsUpper(value[index]);

    private readonly record struct Candidate(CompletionItem Item, CatalogMatch Match, double Score, long Ordinal);

    /// <summary>
    /// Identidade de uso de um símbolo neste contexto, ou nulo quando o contexto não a define por completo. O
    /// registro de aceite deve usar esta mesma construção: uma chave divergente nunca seria lida de volta no ranking.
    /// </summary>
    public static bool TryCreateUsageKey(CompletionContext context, string symbolId,
        [NotNullWhen(true)] out CompletionUsageKey? key)
    {
        ArgumentNullException.ThrowIfNull(context);
        key = !string.IsNullOrWhiteSpace(symbolId) && UsageIdentity.TryCreate(context) is { } identity
            ? identity.KeyFor(symbolId)
            : null;
        return key is not null;
    }

    /// <summary>
    /// Identidade validada uma única vez por consulta. Construir <see cref="CompletionUsageKey"/> com qualquer
    /// componente vazio ou em branco lança, então a validação acontece fora do laço de candidatos: uma aba sem coleção
    /// capturada ou sem forma conhecida simplesmente não recebe o termo de uso, sem chave sintética e sem exceção.
    /// </summary>
    private readonly record struct UsageIdentity(string ConnectionId, string Database, string Collection, string Shape)
    {
        public static UsageIdentity? TryCreate(CompletionContext context)
        {
            if (context.Scope is not { } scope || scope.Connection.ProfileId == Guid.Empty) return null;
            if (string.IsNullOrWhiteSpace(scope.Database) || string.IsNullOrWhiteSpace(scope.Collection)) return null;
            if (string.IsNullOrWhiteSpace(context.ShapeId)) return null;
            // A digital da conexão entra quando existe: editar o perfil ou apontar para outra instância não deve
            // reaproveitar o uso acumulado da configuração anterior.
            var profile = scope.Connection.ProfileId.ToString("N", CultureInfo.InvariantCulture);
            var connectionId = string.IsNullOrWhiteSpace(scope.Connection.Fingerprint)
                ? profile
                : profile + ":" + scope.Connection.Fingerprint;
            return new(connectionId, scope.Database, scope.Collection, context.ShapeId);
        }

        public CompletionUsageKey KeyFor(string symbolId) => new(ConnectionId, Database, Collection, Shape, symbolId);
    }

    /// <summary>Rastreador mais identidade já validada: o laço de candidatos só precisa do símbolo.</summary>
    private readonly record struct UsageScope(CompletionUsageTracker Tracker, UsageIdentity Identity)
    {
        public static UsageScope? TryCreate(CompletionUsageTracker? tracker, CompletionContext context) =>
            tracker is not null && UsageIdentity.TryCreate(context) is { } identity ? new(tracker, identity) : null;

        public double UsageOf(string symbolId) => string.IsNullOrWhiteSpace(symbolId)
            ? 0
            : Tracker.GetUsage(Identity.KeyFor(symbolId));
    }
}
