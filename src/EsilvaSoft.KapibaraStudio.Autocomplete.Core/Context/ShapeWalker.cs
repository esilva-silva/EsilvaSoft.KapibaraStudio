using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Locates the innermost catalogued MongoDB call and applies its parameter shape. This is deliberately non-evaluating:
/// malformed/dynamic expressions return <see cref="ShapeWalkResult.Unknown"/> rather than borrowing a neighbouring call.
/// </summary>
public static class ShapeWalker
{
    /// <summary>
    /// Analisa a forma no caret reaproveitando os tokens já produzidos pelo chamador. Não existe parâmetro de modo
    /// léxico: o modo do dialeto já está embutido na lista de tokens, o que evita uma segunda lexagem divergente.
    /// </summary>
    /// <param name="text">Texto completo do documento correspondente aos tokens.</param>
    /// <param name="tokens">Tokens do documento, em ordem de origem.</param>
    /// <param name="caret">Posição do cursor em UTF-16.</param>
    /// <param name="role">Papel estrutural já classificado para o caret.</param>
    /// <param name="definition">Definição de linguagem; a padrão é usada quando nula.</param>
    /// <param name="rootShape">Forma raiz usada apenas como fallback quando não há chamada catalogada envolvente.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo.</param>
    public static ShapeWalkResult Walk(string text, IReadOnlyList<MongoToken> tokens, int caret, CompletionCursorRole role,
        LanguageDefinition? definition = null, string? rootShape = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, text.Length);
        cancellationToken.ThrowIfCancellationRequested();
        definition ??= LanguageDefinition.Default;
        var call = FindCall(tokens, text, caret);
        if (call.Method is null) return Fallback();
        var method = definition.Symbols.FirstOrDefault(symbol => symbol.Name == call.Method && symbol.Parameters.Count > call.Argument);
        if (method is null) return Fallback();
        var shape = method.Parameters[call.Argument];
        return Descend(shape, tokens, text, call.Open, caret, role, definition);

        // A semente da raiz só vale depois de a busca pela chamada falhar: nunca antes, para não roubar o contexto de uma chamada real.
        ShapeWalkResult Fallback() => rootShape is null
            ? ShapeWalkResult.Unknown
            : Descend(rootShape, tokens, text, callOpen: -1, caret, role, definition);
    }

    /// <summary>Sobrecarga de conveniência que lexa o texto em modo <c>Script</c> por conta própria.</summary>
    public static ShapeWalkResult Walk(string text, int caret, CompletionCursorRole role, LanguageDefinition? definition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, text.Length);
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(text.AsSpan(), tokens, cancellationToken: cancellationToken);
        return Walk(text, tokens, caret, role, definition, rootShape: null, cancellationToken);
    }

    internal static (string? Method, int Open, int Argument) FindCall(IReadOnlyList<MongoToken> tokens, string text, int caret)
    {
        var stack = new Stack<int>();
        var best = -1;
        for (var i = 0; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value == "(") stack.Push(i);
            else if (value == ")" && stack.Count > 0) stack.Pop();
        }
        if (stack.Count > 0) best = stack.Peek();
        if (best < 0)
        {
            // Recovery path for a stray closing parenthesis before the caret: preserve the last recognizable method
            // call so a malformed argument does not erase the enclosing filter context.
            for (var i = 1; i < tokens.Count && tokens[i].Start < caret; i++)
                if (Value(tokens, text, i) == "(" && tokens[i - 1].Kind == MongoTokenKind.Identifier) best = i;
        }
        if (best <= 0 || tokens[best - 1].Kind != MongoTokenKind.Identifier) return (null, -1, 0);
        var argument = 0;
        var delimiters = new Stack<char>();
        for (var i = best + 1; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value is "{" or "[" or "(") delimiters.Push(value[0]);
            else if (value == ")")
            {
                if (delimiters.Count == 0) break;
                if (delimiters.Peek() == '(') delimiters.Pop();
                // A premature ')' inside an object is a skipped recovery token, not the end of the method call.
            }
            else if (value == "}" && delimiters.Count > 0 && delimiters.Peek() == '{') delimiters.Pop();
            else if (value == "]" && delimiters.Count > 0 && delimiters.Peek() == '[') delimiters.Pop();
            else if (value == "," && delimiters.Count == 0) argument++;
        }
        return (Value(tokens, text, best - 1), best, argument);
    }

    private static ShapeWalkResult Descend(string initial, IReadOnlyList<MongoToken> tokens, string text, int callOpen, int caret,
        CompletionCursorRole role, LanguageDefinition definition)
    {
        var shape = initial;
        var stack = new Stack<(string Shape, string? Key)>();
        stack.Push((shape, null));
        for (var i = callOpen + 1; i < tokens.Count && tokens[i].Start < caret; i++)
        {
            var value = Value(tokens, text, i);
            if (value == "[" && definition.Shapes.TryGetValue(shape, out var array))
            {
                var element = array.Element ?? array.Values.FirstOrDefault(candidate =>
                    definition.Shapes.TryGetValue(candidate, out var valueShape) && valueShape.Element is not null);
                while (element is not null && definition.Shapes.TryGetValue(element, out var nestedArray)
                    && nestedArray.Element is { } nestedElement)
                    element = nestedElement;
                if (element is not null)
                { shape = element; stack.Push((shape, null)); continue; }
            }
            if (value == "{")
            {
                if (shape == "FieldValue" && stack.Any(frame => frame.Key is "$push" or "$addToSet"))
                    shape = "UpdateModifierObject";
                if (definition.Shapes.TryGetValue(shape, out var valueShape))
                {
                    var objectValue = valueShape.Values.FirstOrDefault(definition.Shapes.ContainsKey);
                    if (objectValue is not null) shape = objectValue;
                }
                stack.Push((shape, null));
                continue;
            }
            if (value is "}" or "]")
            {
                if (stack.Count > 1)
                {
                    // A key frame may represent a scalar value without its own opening delimiter. When its enclosing
                    // object closes, discard both the value frame and the object frame; nested objects already have a
                    // delimiter frame on top and are removed on their own closing token.
                    var topHasKey = stack.Peek().Key is not null;
                    stack.Pop();
                    if (stack.Count > 1 && (topHasKey || stack.Peek().Key is not null)) stack.Pop();
                    shape = stack.Peek().Shape;
                }
                continue;
            }
            // A vírgula encerra o valor da propriedade corrente: sem devolver o quadro da chave, a próxima chave seria
            // resolvida contra a forma do valor anterior, e uma forma sem chaves descartaria todo o estreitamento.
            if (value == "," && stack.Count > 1 && stack.Peek().Key is not null) { stack.Pop(); shape = stack.Peek().Shape; continue; }
            if ((tokens[i].Kind is MongoTokenKind.Identifier or MongoTokenKind.String) && i + 1 < tokens.Count
                && Value(tokens, text, i + 1) == ":" && tokens[i + 1].End <= caret)
            {
                var key = Unquote(value);
                if (definition.Shapes.TryGetValue(shape, out var objectShape))
                {
                    var rule = Match(objectShape, key, definition);
                    var symbolShape = SymbolValueShape(definition, key);
                    var next = symbolShape ?? rule?.Value;
                    if (next is not null) { shape = next; stack.Push((shape, key)); }
                }
            }
        }
        if (role is CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString
            && stack.Count > 1 && stack.Peek().Key is not null)
        {
            stack.Pop();
            shape = stack.Peek().Shape;
        }
        // Uma forma que não é objeto é ela mesma o tipo de valor aceito na posição.
        if (!definition.Shapes.TryGetValue(shape, out var current)) return new(shape, ExpectedForPrimitive(shape)) { ValueShape = shape };
        var expected = role switch
        {
            CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString => KeyKinds(current, definition),
            CompletionCursorRole.ArrayElement => current.Element is { } element ? ExpectedForShape(element, definition) : KeyKinds(current, definition),
            CompletionCursorRole.PropertyValue => ValueKinds(current, definition),
            _ => ExpectedForShape(shape, definition)
        };
        var parent = stack.Reverse().Select(frame => frame.Key).LastOrDefault(key => !string.IsNullOrEmpty(key)) ?? "";
        // Search compound has a narrower key set than an operator body, but it remains the published context id so
        // existing consumers continue to recognize the catalogued SearchOperatorBody shape.
        var reportedShape = shape == "SearchCompoundBody" ? "SearchOperatorBody" : shape;
        return new(reportedShape, expected, parent) { ValueShape = ValueShapeOf(current) };
    }

    // Entre os valores declarados, o primeiro tipo primitivo descreve o valor esperado; formas de objeto não são um tipo.
    private static string? ValueShapeOf(ShapeDefinition shape) =>
        shape.Values.FirstOrDefault(LanguageDefinition.PrimitiveValues.Contains) ?? (shape.Values.Count > 0 ? shape.Values[0] : null);

    private static ShapeKeyRule? Match(ShapeDefinition shape, string key, LanguageDefinition definition) => shape.Keys.FirstOrDefault(rule =>
        rule.Rule == "Fixed" && rule.Names.Contains(key, StringComparer.Ordinal)) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule == "Operator" && rule.Names.Contains(key, StringComparer.Ordinal)) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule == "Operator" && (key.StartsWith('$') ||
            (rule.Kinds.Count > 0 && definition.Symbols.Any(symbol => string.Equals(symbol.Name, key, StringComparison.Ordinal)
                && rule.Kinds.Contains(symbol.Kind))))) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule == "Exclusive" && (rule.Names.Count == 0 || rule.Names.Contains(key, StringComparer.Ordinal))) ??
        shape.Keys.FirstOrDefault(rule => rule.Rule is "FieldPath" or "Dynamic");
    private static string? SymbolValueShape(LanguageDefinition definition, string key) =>
        definition.Symbols.FirstOrDefault(symbol => string.Equals(symbol.Name, key, StringComparison.Ordinal))?.ValueShape;
    private static SymbolKinds KeyKinds(ShapeDefinition shape, LanguageDefinition definition) => shape.Keys.Aggregate(SymbolKinds.None, (all, rule) => all |
        (rule.Rule == "FieldPath" ? SymbolKinds.Field : rule.Rule is "Operator" or "Exclusive" ? rule.Kinds.Aggregate(SymbolKinds.None, (k, kind) => k | kind.ToFlag()) : SymbolKinds.None));
    private static SymbolKinds ValueKinds(ShapeDefinition shape, LanguageDefinition definition) => shape.Values.Aggregate(SymbolKinds.None,
        (all, value) => all | ExpectedForShape(value, definition));
    private static SymbolKinds ExpectedForShape(string shape, LanguageDefinition definition) => definition.Shapes.TryGetValue(shape, out var value) ? KeyKinds(value, definition) : ExpectedForPrimitive(shape);
    private static SymbolKinds ExpectedForPrimitive(string value) => value switch
    {
        "FieldPathString" or "FieldReference" => SymbolKinds.Field,
        "CollectionNameString" => SymbolKinds.Collection,
        "DatabaseNameString" => SymbolKinds.Database,
        _ => SymbolKinds.BsonConstructor | SymbolKinds.Snippet
    };
    private static string Value(IReadOnlyList<MongoToken> tokens, string text, int index) => index is >= 0 and < int.MaxValue && index < tokens.Count ? text.Substring(tokens[index].Start, tokens[index].Length) : "";
    private static string Unquote(string value) => value.Length >= 2 && value[0] is '\'' or '"' ? value[1..^1] : value;
}
