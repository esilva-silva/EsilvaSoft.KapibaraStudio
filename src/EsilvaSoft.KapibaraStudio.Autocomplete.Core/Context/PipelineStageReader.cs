using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

internal readonly record struct SiblingCollectionContext(string Collection, string Property, int PipelineOpen);

/// <summary>
/// Produz <see cref="PipelineStage"/> a partir dos tokens do editor. É deliberadamente não avaliativo: nada é
/// executado, nenhuma expressão é interpretada e qualquer trecho não reconhecido vira <see cref="PipelineValueKind.Unknown"/>,
/// o que leva a inferência de campos a um estado seguro em vez de a um palpite.
/// </summary>
public static class PipelineStageReader
{
    /// <summary>Acumuladores cuja saída é sempre numérica.</summary>
    private static readonly string[] NumericAccumulators = ["$sum", "$avg", "$count", "$stdDevPop", "$stdDevSamp", "$median"];

    /// <summary>Índice do token '[' que abre o pipeline do caret, ou -1.</summary>
    /// <param name="tokens">Tokens do documento, em ordem de origem.</param>
    /// <param name="text">Texto completo correspondente aos tokens.</param>
    /// <param name="caret">Posição do cursor em UTF-16.</param>
    /// <param name="dialect">Dialeto do editor: define se o pipeline é o documento inteiro ou o argumento de <c>aggregate</c>.</param>
    public static int FindPipelineRoot(IReadOnlyList<MongoToken> tokens, string text, int caret, EditorDialects dialect)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        return dialect == EditorDialects.AggregationJson ? OutermostOpenArray(tokens, text, caret) : AggregateArgument(tokens, text, caret);
    }

    /// <summary>
    /// Localiza um valor cujo shape declara <c>SiblingCollection:...</c> dentro do <c>$lookup</c> que contém o caret.
    /// A coleção irmã precisa ser um literal estático; expressões dinâmicas mantêm o contexto conservador do chamador.
    /// </summary>
    internal static SiblingCollectionContext? FindSiblingCollectionContext(IReadOnlyList<MongoToken> tokens, string text,
        int caret, LanguageDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        definition ??= LanguageDefinition.Default;
        if (!definition.Shapes.TryGetValue("LookupBody", out var lookupShape)) return null;

        SiblingCollectionContext? result = null;
        for (var lookupKey = 0; lookupKey + 2 < tokens.Count; lookupKey++)
        {
            if (Name(tokens, text, lookupKey) != "$lookup" || Text(tokens, text, lookupKey + 1) != ":"
                || Text(tokens, text, lookupKey + 2) != "{") continue;

            var bodyOpen = lookupKey + 2;
            var bodyClose = MatchingClose(tokens, text, bodyOpen);
            var limit = bodyClose >= 0 ? bodyClose : tokens.Count;
            var bodyEnd = bodyClose >= 0 ? tokens[bodyClose].End : text.Length;
            if (caret < tokens[bodyOpen].Start || caret > bodyEnd) continue;

            var properties = Keys(tokens, text, bodyOpen, limit).ToArray();
            foreach (var propertyKey in properties)
            {
                var valueIndex = propertyKey + 2;
                if (!ValueContainsCaret(tokens, text, valueIndex, limit, caret)) continue;
                var property = Name(tokens, text, propertyKey);
                var rule = lookupShape.Keys.FirstOrDefault(candidate => candidate.Rule == "Fixed"
                    && candidate.Names.Contains(property, StringComparer.Ordinal));
                if (rule?.Scope is not { } scope || !scope.StartsWith("SiblingCollection:", StringComparison.Ordinal)) continue;

                var sourceProperty = scope["SiblingCollection:".Length..];
                var sourceKey = properties.FirstOrDefault(candidate =>
                    string.Equals(Name(tokens, text, candidate), sourceProperty, StringComparison.Ordinal));
                if (sourceKey <= 0 || StaticLiteral(tokens, text, sourceKey + 2) is not { Length: > 0 } collection) continue;
                var pipelineOpen = property == "pipeline" && Text(tokens, text, valueIndex) == "[" ? valueIndex : -1;
                result = new(collection, property, pipelineOpen);
                break;
            }
        }
        return result;
    }

    /// <summary>Estágios encerrados antes do caret, em ordem de origem.</summary>
    /// <param name="tokens">Tokens do documento, em ordem de origem.</param>
    /// <param name="text">Texto completo correspondente aos tokens.</param>
    /// <param name="open">Índice do token '[' que abre o pipeline, como devolvido por <see cref="FindPipelineRoot"/>.</param>
    /// <param name="caret">Posição do cursor em UTF-16; o estágio que contém o caret nunca é devolvido.</param>
    /// <param name="definition">Definição de linguagem usada para reconhecer acumuladores; a padrão é usada quando nula.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo.</param>
    public static IReadOnlyList<PipelineStage> ReadStagesBefore(IReadOnlyList<MongoToken> tokens, string text, int open,
        int caret, LanguageDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        cancellationToken.ThrowIfCancellationRequested();
        if (open < 0 || open >= tokens.Count) return [];
        definition ??= LanguageDefinition.Default;
        var stages = new List<PipelineStage>();
        for (var i = open + 1; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = Text(tokens, text, i);
            if (value == "]") break;
            if (value != "{") continue;
            var close = MatchingClose(tokens, text, i);
            // Um estágio aberto, inacabado ou que contém o caret não descreve nada de concluído: a leitura para ali.
            if (close < 0 || tokens[close].End > caret) break;
            stages.Add(ReadStage(tokens, text, i, close, definition, cancellationToken));
            i = close;
        }
        return stages;
    }

    // Em AggregationJson o documento é o próprio pipeline: vale o '[' mais externo ainda aberto no caret.
    private static int OutermostOpenArray(IReadOnlyList<MongoToken> tokens, string text, int caret)
    {
        var openArrays = new List<int>();
        for (var i = 0; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Text(tokens, text, i);
            if (value == "[") openArrays.Add(i);
            else if (value == "]" && openArrays.Count > 0) openArrays.RemoveAt(openArrays.Count - 1);
        }
        return openArrays.Count > 0 ? openArrays[0] : -1;
    }

    // Em scripts, o pipeline é o primeiro array do argumento de aggregate; qualquer outra chamada envolvente não tem pipeline.
    private static int AggregateArgument(IReadOnlyList<MongoToken> tokens, string text, int caret)
    {
        var call = ShapeWalker.FindCall(tokens, text, caret);
        var openParenthesis = call.Method == "aggregate" ? call.Open : OuterAggregateCall(tokens, text, caret);
        if (openParenthesis < 0) return -1;
        for (var i = openParenthesis + 1; i < tokens.Count && tokens[i].Start < caret; i++)
            if (Text(tokens, text, i) == "[") return i;
        return -1;
    }

    // A chamada mais interna pode ser outra (por exemplo um construtor BSON): a pilha é percorrida para fora.
    private static int OuterAggregateCall(IReadOnlyList<MongoToken> tokens, string text, int caret)
    {
        var openCalls = new List<int>();
        for (var i = 0; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Text(tokens, text, i);
            if (value == "(") openCalls.Add(i);
            else if (value == ")" && openCalls.Count > 0) openCalls.RemoveAt(openCalls.Count - 1);
        }
        for (var index = openCalls.Count - 1; index >= 0; index--)
        {
            var open = openCalls[index];
            if (open > 0 && tokens[open - 1].Kind == MongoTokenKind.Identifier && Text(tokens, text, open - 1) == "aggregate") return open;
        }
        return -1;
    }

    private static PipelineStage ReadStage(IReadOnlyList<MongoToken> tokens, string text, int open, int close,
        LanguageDefinition definition, CancellationToken cancellationToken)
    {
        var key = -1;
        foreach (var candidate in Keys(tokens, text, open, close))
        {
            if (!Name(tokens, text, candidate).StartsWith('$')) continue;
            key = candidate;
            break;
        }
        // Um grupo sem chave de operador não é um estágio conhecido: o nome reservado leva PipelineInfo ao estado desconhecido.
        if (key < 0) return new PipelineStage("$unrecognizedStage");
        var name = Name(tokens, text, key);
        var valueIndex = key + 2;
        if (valueIndex > close - 1) return new PipelineStage(name, new PipelineStageProperty(name, PipelineStageValue.Unknown));
        if (Text(tokens, text, valueIndex) != "{")
            return new PipelineStage(name, new PipelineStageProperty(name, Classify(tokens, text, valueIndex, close, definition, cancellationToken)));

        var body = MatchingClose(tokens, text, valueIndex);
        if (body < 0 || body > close) return new PipelineStage(name, new PipelineStageProperty(name, PipelineStageValue.Unknown));
        var properties = new List<PipelineStageProperty>();
        foreach (var property in Keys(tokens, text, valueIndex, body))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var propertyName = Name(tokens, text, property);
            if (propertyName.Length == 0) continue;
            properties.Add(new(propertyName, Classify(tokens, text, property + 2, body, definition, cancellationToken)));
        }
        return new PipelineStage(name, properties.ToArray());
    }

    /// <summary>Índices dos tokens de chave imediatos de um grupo, isto é, seguidos de ':' na profundidade do grupo.</summary>
    private static IEnumerable<int> Keys(IReadOnlyList<MongoToken> tokens, string text, int open, int close)
    {
        var depth = 0;
        for (var i = open + 1; i < close; i++)
        {
            var value = Text(tokens, text, i);
            if (value is "{" or "[" or "(") { depth++; continue; }
            if (value is "}" or "]" or ")") { depth--; continue; }
            if (depth != 0 || tokens[i].Kind is not (MongoTokenKind.Identifier or MongoTokenKind.String)) continue;
            if (i + 1 < close && Text(tokens, text, i + 1) == ":") yield return i;
        }
    }

    private static PipelineStageValue Classify(IReadOnlyList<MongoToken> tokens, string text, int index, int limit,
        LanguageDefinition definition, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= tokens.Count || index >= limit) return PipelineStageValue.Unknown;
        var token = tokens[index];
        var value = Text(tokens, text, index);
        switch (token.Kind)
        {
            case MongoTokenKind.Number:
                return value switch { "1" => PipelineStageValue.Include, "0" => PipelineStageValue.Exclude, _ => PipelineStageValue.Expression };
            case MongoTokenKind.Identifier:
                return value switch { "true" => PipelineStageValue.Include, "false" => PipelineStageValue.Exclude, _ => PipelineStageValue.Unknown };
            case MongoTokenKind.String when token.IsTerminated:
                var content = Unquote(value);
                if (content.StartsWith("$$", StringComparison.Ordinal)) return PipelineStageValue.Expression;
                if (content.Length <= 1) return content.Length == 0 ? PipelineStageValue.Unknown : PipelineStageValue.Literal(content);
                return content[0] == '$' ? PipelineStageValue.FieldReference(content[1..]) : PipelineStageValue.Literal(content);
            case MongoTokenKind.Punctuation when value == "[":
                return IsPipelineArray(tokens, text, index, limit)
                    ? PipelineStageValue.Pipeline(ReadPipelineArray(tokens, text, index, limit, definition, cancellationToken))
                    : ClassifyArray(tokens, text, index, limit);
            case MongoTokenKind.Punctuation when value == "{":
                return ClassifyObject(tokens, text, index, limit, definition, cancellationToken);
            default:
                return PipelineStageValue.Unknown;
        }
    }

    private static PipelineStageValue ClassifyArray(IReadOnlyList<MongoToken> tokens, string text, int open, int limit)
    {
        var close = MatchingClose(tokens, text, open);
        if (close < 0 || close > limit) return PipelineStageValue.Unknown;
        var names = new List<string>();
        for (var i = open + 1; i < close; i++)
        {
            var value = Text(tokens, text, i);
            if (value == ",") continue;
            if (tokens[i].Kind != MongoTokenKind.String || !tokens[i].IsTerminated) return PipelineStageValue.Expression;
            var content = Unquote(value);
            if (content.Length == 0) return PipelineStageValue.Expression;
            names.Add(content);
        }
        return names.Count > 0 ? PipelineStageValue.LiteralNames(names.ToArray()) : PipelineStageValue.Expression;
    }

    private static PipelineStageValue ClassifyObject(IReadOnlyList<MongoToken> tokens, string text, int open, int limit,
        LanguageDefinition definition, CancellationToken cancellationToken)
    {
        var close = MatchingClose(tokens, text, open);
        if (close < 0 || close > limit) return PipelineStageValue.Unknown;
        // $replaceRoot/$replaceWith carrega uma referência simples ao documento. Preservá-la
        // permite que a inferência troque a raiz pelo subdocumento sem avaliar a expressão.
        var properties = Keys(tokens, text, open, close).ToArray();
        if (properties.Length > 0 && properties.All(property =>
            Text(tokens, text, property + 2) == "[" && IsPipelineArray(tokens, text, property + 2, close)))
        {
            var branches = new Dictionary<string, IReadOnlyList<PipelineStage>>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                branches[Name(tokens, text, property)] = ReadPipelineArray(tokens, text, property + 2, close, definition, cancellationToken);
            }
            return PipelineStageValue.Facet(branches);
        }
        if (properties.Length == 1 && Name(tokens, text, properties[0]) is "newRoot" or "replacement")
        {
            var valueIndex = properties[0] + 2;
            if (valueIndex < close && tokens[valueIndex].Kind == MongoTokenKind.String && tokens[valueIndex].IsTerminated)
            {
                var content = Unquote(Text(tokens, text, valueIndex));
                if (content.StartsWith('$') && !content.StartsWith("$$", StringComparison.Ordinal))
                    return PipelineStageValue.FieldReference(content[1..]);
            }
        }
        foreach (var key in Keys(tokens, text, open, close))
        {
            var name = Name(tokens, text, key);
            if (!IsAccumulator(definition, name)) break;
            return PipelineStageValue.Accumulator(name, NumericAccumulators.Contains(name, StringComparer.Ordinal));
        }
        return PipelineStageValue.Expression;
    }

    private static bool IsPipelineArray(IReadOnlyList<MongoToken> tokens, string text, int open, int limit)
    {
        var close = MatchingClose(tokens, text, open);
        if (close < 0 || close > limit) return false;
        for (var index = open + 1; index < close; index++)
        {
            var value = Text(tokens, text, index);
            if (value == ",") continue;
            return value == "{";
        }
        return true;
    }

    private static List<PipelineStage> ReadPipelineArray(IReadOnlyList<MongoToken> tokens, string text, int open,
        int limit, LanguageDefinition definition, CancellationToken cancellationToken)
    {
        var close = MatchingClose(tokens, text, open);
        if (close < 0 || close > limit) return [];
        var stages = new List<PipelineStage>();
        for (var index = open + 1; index < close; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Text(tokens, text, index) == ",") continue;
            if (Text(tokens, text, index) != "{") continue;
            var stageClose = MatchingClose(tokens, text, index);
            if (stageClose < 0 || stageClose > close) break;
            stages.Add(ReadStage(tokens, text, index, stageClose, definition, cancellationToken));
            index = stageClose;
        }
        return stages;
    }

    private static bool IsAccumulator(LanguageDefinition definition, string name) =>
        definition.Symbols.Any(symbol => symbol.Kind == SymbolKind.Accumulator && string.Equals(symbol.Name, name, StringComparison.Ordinal));

    private static bool ValueContainsCaret(IReadOnlyList<MongoToken> tokens, string text, int valueIndex, int limit, int caret)
    {
        if (valueIndex < 0 || valueIndex >= tokens.Count || valueIndex >= limit || caret < tokens[valueIndex].Start) return false;
        var value = Text(tokens, text, valueIndex);
        if (value is not ("{" or "[")) return caret <= tokens[valueIndex].End;
        var close = MatchingClose(tokens, text, valueIndex);
        return close < 0 || close <= limit && caret <= tokens[close].End;
    }

    private static string? StaticLiteral(IReadOnlyList<MongoToken> tokens, string text, int index)
    {
        if (index < 0 || index >= tokens.Count || tokens[index].Kind != MongoTokenKind.String || !tokens[index].IsTerminated) return null;
        return Unquote(Text(tokens, text, index));
    }

    /// <summary>Índice do token que fecha o grupo aberto em <paramref name="open"/>, ou -1 quando não há fechamento.</summary>
    private static int MatchingClose(IReadOnlyList<MongoToken> tokens, string text, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            var value = Text(tokens, text, i);
            if (value is "{" or "[") depth++;
            else if (value is "}" or "]") { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static string Name(IReadOnlyList<MongoToken> tokens, string text, int index) => Unquote(Text(tokens, text, index));

    private static string Text(IReadOnlyList<MongoToken> tokens, string text, int index) =>
        index >= 0 && index < tokens.Count ? text.Substring(tokens[index].Start, tokens[index].Length) : "";

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] is '\'' or '"' or '`' && value[^1] == value[0] ? value[1..^1] : value;
}
