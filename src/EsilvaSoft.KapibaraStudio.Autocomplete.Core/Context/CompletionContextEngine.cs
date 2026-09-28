using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Converts a snapshot and caret into completion intent. It only consumes the shared tolerant lexer: no metadata, code
/// evaluation, or document mutation is permitted here. The shallow structural rules deliberately prefer a safe broad
/// result to a misleading narrow one while the full shape walker is still unavailable.
/// </summary>
public sealed class CompletionContextEngine
{
    public static CompletionContextAnalysis Analyze(ContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = Mode(request);
        var text = request.Snapshot.GetText(0, request.Snapshot.Length);
        var tokens = new List<MongoToken>(Math.Min(text.Length / 3 + 8, 8192));
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: mode, cancellationToken: cancellationToken);
        return Analyze(request, mode, text, tokens, cancellationToken);
    }

    /// <summary>
    /// Analyzes reusing the lexing of this snapshot kept by <paramref name="tokens"/>. This is the typing path: one
    /// keystroke costs one lexing of the document, and every repeated analysis of the same version — refiltering with
    /// the list open, a second provider, ghost text — costs nothing, not even materializing the text. No syntax node is
    /// built here; the cache is keyed by document, so a tab never sees another tab's tokens.
    /// </summary>
    public static CompletionContextAnalysis Analyze(ContextRequest request, TokenCache? tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (tokens is null) return Analyze(request, cancellationToken);
        var lexed = tokens.GetOrLex(request.Snapshot, Mode(request), cancellationToken);
        return Analyze(request, lexed.Mode, lexed.Text, lexed.Tokens, cancellationToken);
    }

    /// <summary>
    /// Analyzes reusing the syntax tree of this snapshot kept by <paramref name="trees"/>. Reserved for consumers that
    /// already need the nodes of the tree: a tree is more expensive than the tokens the context actually reads, so the
    /// typing path uses <see cref="TokenCache"/> instead. The cache keys every entry by document, so a tab never sees
    /// another tab's tree; a snapshot already superseded by a newer version has no current tree and is lexed on its own,
    /// because its result will be discarded by the caller anyway and must never be built from another version's tokens.
    /// </summary>
    public static CompletionContextAnalysis Analyze(ContextRequest request, SyntaxTreeCache? trees,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = Mode(request);
        var text = request.Snapshot.GetText(0, request.Snapshot.Length);
        return Analyze(request, mode, text, Tokens(request, trees, text, mode, cancellationToken), cancellationToken);
    }

    private static CompletionContextAnalysis Analyze(ContextRequest request, MongoLexerMode mode, string text,
        IReadOnlyList<MongoToken> tokens, CancellationToken cancellationToken)
    {
        var caret = request.ValidCaret;
        var tokenIndex = FindTokenAt(tokens, caret);
        if (tokenIndex >= 0 && tokens[tokenIndex].Kind == MongoTokenKind.String && tokens[tokenIndex].Has(MongoTokenTraits.Continuation))
        {
            // A normal JavaScript/Mongo string cannot consume the next source line. The shared lexer preserves
            // continuation state for highlighting, but completion must recover the new statement independently.
            var lineStart = text.LastIndexOf('\n', Math.Max(0, tokens[tokenIndex].Start - 1)) + 1;
            var recovered = tokens.Where(token => token.End <= lineStart).ToList();
            var lineTokens = new List<MongoToken>();
            MongoLexer.Tokenize(text.AsSpan(lineStart), lineTokens, mode: mode, cancellationToken: cancellationToken);
            recovered.AddRange(lineTokens.Select(token => token with { Start = token.Start + lineStart }));
            tokens = recovered;
            tokenIndex = FindTokenAt(tokens, caret);
        }
        if (tokenIndex >= 0 && IsNonCompletable(tokens[tokenIndex])) return Empty(request, CompletionCursorRole.NonCompletable, caret);

        var tabTarget = request.TabScope is { } tabScope
            ? new NamespaceTarget(null, tabScope.Database, tabScope.Collection, NamespaceTargetConfidence.TabDefault) { Connection = tabScope.Connection }
            : null;
        // Os tokens da árvore são exatamente os do documento em modo Script; o resolvedor filtra comentários sozinho.
        var target = mode == MongoLexerMode.Script
            ? NamespaceTargetResolver.Resolve(text, tokens, caret, tabTarget, request.Dialect, cancellationToken)
            : NamespaceTargetResolver.Resolve(text, caret, tabTarget, request.Dialect, cancellationToken);

        var token = tokenIndex >= 0 ? tokens[tokenIndex] : default;
        var prefix = tokenIndex >= 0 && token.Kind is MongoTokenKind.Identifier or MongoTokenKind.String
            ? TextBeforeCaret(text, token, caret) : string.Empty;
        var replace = tokenIndex >= 0 && token.Kind == MongoTokenKind.String
            ? StringContentSpan(text, token, caret)
            : tokenIndex >= 0 && token.Kind == MongoTokenKind.Identifier
                ? new TextSpan(token.Start, Math.Max(0, token.End - token.Start))
                : new TextSpan(caret, 0);
        var dottedExclusive = ExclusiveBeforeCaret(tokens, caret);
        if (PreviousText(tokens, text, dottedExclusive) == "."
            && IsObjectMemberPosition(tokens, text, dottedExclusive - 2, allowMissingComma: false))
        {
            var dottedStart = tokens[dottedExclusive - 2].Start;
            replace = new TextSpan(dottedStart, caret - dottedStart);
            prefix = text.Substring(dottedStart, caret - dottedStart);
        }
        var insert = new TextSpan(replace.Start, Math.Max(0, caret - replace.Start));

        var role = Classify(request, text, tokens, tokenIndex, caret, request.Dialect, out var expected, out var quote, cancellationToken);
        if (role is CompletionCursorRole.NonCompletable) return Empty(request, role, caret);
        var unterminatedQuote = tokenIndex >= 0 && tokens[tokenIndex].Kind == MongoTokenKind.String && tokens[tokenIndex].Has(MongoTokenTraits.Unterminated);
        var preferredQuote = role == CompletionCursorRole.PropertyKey ? DominantPropertyQuote(tokens, text, caret) : null;
        var shape = ShapeWalker.Walk(text, tokens, caret, role, rootShape: RootShape(request, role), cancellationToken: cancellationToken);
        if (role == CompletionCursorRole.ArrayElement && PipelineStageReader.FindPipelineRoot(tokens, text, caret, request.Dialect) >= 0)
            shape = shape with { ShapeId = "Pipeline" };
        if (shape.ExpectedKinds != SymbolKinds.None && role is CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString
            or CompletionCursorRole.PropertyValue or CompletionCursorRole.ArrayElement or CompletionCursorRole.FieldReferenceString)
            expected = role == CompletionCursorRole.PropertyValue && quote is not null && expected != SymbolKinds.None
                ? expected
                : role == CompletionCursorRole.PropertyValue ? shape.ExpectedKinds : shape.ExpectedKinds;
        expected = NarrowKinds(expected, role, prefix, shape.ShapeId, request.Dialect,
            PipelineStageReader.FindPipelineRoot(tokens, text, caret, request.Dialect) >= 0);
        var exclusiveBeforeCaret = ExclusiveBeforeCaret(tokens, caret);
        if (role == CompletionCursorRole.PropertyKey && PreviousText(tokens, text, exclusiveBeforeCaret) == "."
            && IsObjectMemberPosition(tokens, text, exclusiveBeforeCaret - 2, allowMissingComma: false))
            expected = SymbolKinds.Field;
        if (role == CompletionCursorRole.FieldReferenceString)
            expected = SymbolKinds.Field;
        if (role == CompletionCursorRole.PropertyKeyString && prefix.Contains('.', StringComparison.Ordinal))
            expected = SymbolKinds.Field;
        if (role is CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString && shape.ShapeId == "Stage")
            expected |= SymbolKinds.Snippet;
        if (role == CompletionCursorRole.PropertyKey && prefix.Length == 0 && shape.ShapeId == "Filter")
            expected |= SymbolKinds.Snippet;

        var sibling = (expected & SymbolKinds.Field) != 0
            ? PipelineStageReader.FindSiblingCollectionContext(tokens, text, caret)
            : null;
        if (sibling is { } foreign && target.Database is { Length: > 0 })
            target = target with { Collection = foreign.Collection, Confidence = NamespaceTargetConfidence.Explicit };

        var pipeline = Pipeline(request, tokens, text, caret, expected, sibling, cancellationToken);
        // A stage object such as `$match` is a structural wrapper, not a document field. Once PipelineInfo has
        // established the post-stage document, querying that schema at parent `$match` would incorrectly return no
        // fields. Nested field operators still retain their real parent path (for example `status`).
        var effectiveParentPath = role == CompletionCursorRole.FieldReferenceString
            || shape.ValueShape is "FieldPathString" or "FieldReference"
            ? ""
            : shape.ParentPath.StartsWith('$')
            ? ""
            : shape.ParentPath;
        var valueSchema = sibling is { } scoped
            ? request.ResolveForeignSchema(scoped.Collection)
            : request.InputSchema;
        var context = new CompletionContext(request.Snapshot.Version, request.Dialect, expected, prefix, replace)
        {
            Scope = target.Confidence == NamespaceTargetConfidence.TabDefault
                ? request.TabScope
                : target.Connection is { } connection
                    ? new CatalogScope(connection, target.Database ?? "", target.Collection ?? "")
                    : null,
            ShapeId = shape.ShapeId,
            Role = role,
            Quote = quote,
            UnterminatedQuote = unterminatedQuote,
            PreferredQuote = preferredQuote,
            CallAhead = role == CompletionCursorRole.MemberAccess
                && text[caret..].TrimStart().StartsWith('('),
            ParentPath = effectiveParentPath,
            // O tipo de valor só descreve uma posição de valor; em posição de chave não há valor algum a tipar.
            ValueType = role == CompletionCursorRole.PropertyValue ? shape.ValueShape : null,
            ValueTypes = role is CompletionCursorRole.PropertyKey or CompletionCursorRole.PropertyKeyString or CompletionCursorRole.PropertyValue
                && valueSchema?.Find(effectiveParentPath) is { } parent
                ? parent.Types.Keys.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal),
            CatalogAccess = Access(request.Trigger),
            LocalSchemas = pipeline is { IsKnown: true } known ? [known.ToSchema()] : [],
            RestrictFieldsToLocalSchemas = pipeline is { IsKnown: true }
        };
        return new(context, role, insert, quote, false, target);
    }

    private static MongoLexerMode Mode(ContextRequest request) =>
        request.Dialect == EditorDialects.AggregationJson ? MongoLexerMode.Json : MongoLexerMode.Script;

    /// <summary>
    /// Tokens da versão pedida: os da árvore quando ela é a corrente do documento, senão uma lexificação própria.
    /// Em ambos os caminhos o fluxo de tokens é o mesmo do documento inteiro — nada é truncado por janela.
    /// </summary>
    private static IReadOnlyList<MongoToken> Tokens(ContextRequest request, SyntaxTreeCache? trees, string text,
        MongoLexerMode mode, CancellationToken cancellationToken)
    {
        if (trees?.GetOrParse(request.Snapshot, mode, cancellationToken).Tree is { } tree) return tree.Tokens;
        var tokens = new List<MongoToken>(Math.Min(text.Length / 3 + 8, 8192));
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: mode, cancellationToken: cancellationToken);
        return tokens;
    }

    // Ctrl+Espaço não é digitação: uma invocação explícita pode agendar carga; digitar nunca agenda.
    private static MetadataAccess Access(CompletionTrigger trigger) =>
        trigger == CompletionTrigger.Invoked ? MetadataAccess.LoadIfNeeded : MetadataAccess.Peek;

    // Documento de agregação é o próprio pipeline: sem chamada envolvente, a raiz é o array de estágios.
    private static string? RootShape(ContextRequest request, CompletionCursorRole role) =>
        request.Dialect == EditorDialects.AggregationJson && role is CompletionCursorRole.PropertyKey
            or CompletionCursorRole.PropertyKeyString or CompletionCursorRole.ArrayElement
            or CompletionCursorRole.PropertyValue or CompletionCursorRole.FieldReferenceString
            ? "Pipeline" : null;

    /// <summary>
    /// Infere a forma dos documentos no ponto do pipeline em que o caret está, sem qualquer I/O. Só é calculada quando
    /// campos são esperados e o schema de entrada foi capturado; um resultado desconhecido prefere a lista ampla e
    /// segura da coleção a uma lista estreita e enganosa.
    /// </summary>
    private static PipelineInfo? Pipeline(ContextRequest request, IReadOnlyList<MongoToken> tokens, string text, int caret,
        SymbolKinds expected, SiblingCollectionContext? sibling, CancellationToken cancellationToken)
    {
        if ((expected & SymbolKinds.Field) == 0) return null;
        if (sibling is { } foreign)
        {
            if (request.ResolveForeignSchema(foreign.Collection) is not { } foreignSchema) return null;
            var scoped = PipelineInfo.From(foreignSchema);
            if (foreign.PipelineOpen < 0) return scoped;
            var foreignStages = PipelineStageReader.ReadStagesBefore(tokens, text, foreign.PipelineOpen, caret,
                cancellationToken: cancellationToken);
            return scoped.Apply(foreignStages, stage => ResolveLookupSchema(request, stage));
        }

        if (request.InputSchema is null) return null;
        var open = PipelineStageReader.FindPipelineRoot(tokens, text, caret, request.Dialect);
        if (open < 0) return null;
        var stages = PipelineStageReader.ReadStagesBefore(tokens, text, open, caret, cancellationToken: cancellationToken);
        // No primeiro estágio os campos da própria coleção são a resposta certa: nada a estreitar.
        if (stages.Count == 0) return null;
        return PipelineInfo.From(request.InputSchema).Apply(stages, stage => ResolveLookupSchema(request, stage));
    }

    private static CollectionSchema? ResolveLookupSchema(ContextRequest request, PipelineStage stage)
    {
        if (stage.Name != "$lookup") return null;
        var from = stage.Properties.SingleOrDefault(property => property.Name == "from")?.Value;
        return from?.Kind == PipelineValueKind.Literal && from.Text is { Length: > 0 } collection
            ? request.ResolveForeignSchema(collection)
            : null;
    }

    private static CompletionContextAnalysis Empty(ContextRequest request, CompletionCursorRole role, int caret) =>
        new(new CompletionContext(request.Snapshot.Version, request.Dialect, SymbolKinds.None, string.Empty, new TextSpan(caret, 0)),
            role, new TextSpan(caret, 0), null, false, NamespaceTarget.Unknown);

    private static CompletionCursorRole Classify(ContextRequest request, string text, IReadOnlyList<MongoToken> tokens, int current, int caret,
        EditorDialects dialect, out SymbolKinds expected, out char? quote, CancellationToken cancellationToken)
    {
        quote = null;
        if (current >= 0 && tokens[current].Kind == MongoTokenKind.String)
        {
            quote = text[tokens[current].Start];
            var content = TextBeforeCaret(text, tokens[current], caret);
            if (IsObjectMemberPosition(tokens, text, current, allowMissingComma: false))
            {
                expected = SymbolKinds.Field | SymbolKinds.QueryOperator | SymbolKinds.AggregationStage;
                return CompletionCursorRole.PropertyKeyString;
            }
            if (content.StartsWith("$$", StringComparison.Ordinal)) { expected = SymbolKinds.SystemVariable; return CompletionCursorRole.VariableReferenceString; }
            if (content.StartsWith('$')) { expected = SymbolKinds.Field; return CompletionCursorRole.FieldReferenceString; }
            if (TryClassifyContextualStringValue(tokens, text, current, out var valueKind))
            {
                expected = valueKind;
                return CompletionCursorRole.PropertyValue;
            }
            if (PreviousText(tokens, text, current) is "[") { expected = SymbolKinds.Collection; return CompletionCursorRole.IndexerString; }
            if (PreviousText(tokens, text, current) is "(")
            {
                var method = TokenText(tokens, text, current - 2);
                if (method == "hint") { expected = SymbolKinds.Index; return CompletionCursorRole.StringArgument; }
                if (method == "getCollection") { expected = SymbolKinds.Collection; return CompletionCursorRole.StringArgument; }
                expected = SymbolKinds.None;
                return CompletionCursorRole.StringArgument;
            }
            expected = SymbolKinds.None;
            return PropertyNameBeforeString(tokens, text, current) is "hint" or "comment"
                ? CompletionCursorRole.StringArgument
                : CompletionCursorRole.NonCompletable;
        }

        // When the caret is in whitespace, tokens after it still exist in the document. They must never become the
        // "previous" token: completion in `[ | ]` is an array element, not whatever closes the document later.
        var exclusive = current < 0 ? ExclusiveBeforeCaret(tokens, caret) : tokens[current].Kind == MongoTokenKind.Identifier ? current : current + 1;
        var previous = PreviousText(tokens, text, exclusive);
        if (previous == ".")
        {
            if (IsObjectMemberPosition(tokens, text, exclusive - 2, allowMissingComma: false))
            {
                expected = SymbolKinds.Field;
                return CompletionCursorRole.PropertyKey;
            }
            var receiver = PreviousText(tokens, text, exclusive - 1);
            expected = receiver == "db" ? SymbolKinds.Collection | SymbolKinds.DatabaseMethod
                : receiver == ")" && MethodBeforeClosedCall(tokens, text, exclusive - 1) == "getSiblingDB" ? SymbolKinds.Collection | SymbolKinds.DatabaseMethod
                : receiver == ")" && MethodBeforeClosedCall(tokens, text, exclusive - 1) == "getConnection" ? SymbolKinds.ConnectionMethod | SymbolKinds.Database
                : receiver is "" or ")" ? SymbolKinds.CursorMethod
                : receiver == "ConnectionPool" ? SymbolKinds.Connection
                : HasConnectionPoolReceiver(tokens, text, exclusive - 1) ? SymbolKinds.Collection | SymbolKinds.DatabaseMethod
                : SymbolKinds.CollectionMethod;
            return CompletionCursorRole.MemberAccess;
        }
        if (current >= 0 && tokens[current].Kind == MongoTokenKind.Identifier && IsObjectMemberPosition(tokens, text, current, allowMissingComma: true))
        {
            expected = SymbolKinds.Field | SymbolKinds.QueryOperator | SymbolKinds.AggregationStage;
            return CompletionCursorRole.PropertyKey;
        }
        if (previous is "{" or ",") { expected = SymbolKinds.Field | SymbolKinds.QueryOperator | SymbolKinds.AggregationStage; return CompletionCursorRole.PropertyKey; }
        if (previous == "[") { expected = dialect == EditorDialects.AggregationJson ? SymbolKinds.AggregationStage : SymbolKinds.Field | SymbolKinds.Snippet; return CompletionCursorRole.ArrayElement; }
        if (previous == ":") { expected = SymbolKinds.Operators | SymbolKinds.BsonConstructor | SymbolKinds.Snippet; return CompletionCursorRole.PropertyValue; }
        if (previous is "(" or ",") { expected = SymbolKinds.Field | SymbolKinds.Snippet | SymbolKinds.LocalVariable; return CompletionCursorRole.CallArgument; }
        expected = SymbolKinds.DslRoot | SymbolKinds.GlobalFunction | SymbolKinds.Keyword;
        return string.IsNullOrWhiteSpace(text[..caret]) || previous is ";" or "}" ? CompletionCursorRole.StatementStart : CompletionCursorRole.Unknown;
    }

    private static SymbolKinds NarrowKinds(SymbolKinds expected, CompletionCursorRole role, string prefix, string? shape,
        EditorDialects dialect, bool insidePipeline)
    {
        if (role == CompletionCursorRole.ArrayElement && insidePipeline && dialect != EditorDialects.AggregationJson && string.IsNullOrEmpty(prefix))
            return SymbolKinds.Snippet;
        // Operator names accept the optional leading '$'. Keep the shape's operator kinds for a textual prefix such
        // as "re" so "$regex" remains discoverable; structural shapes, not the spelling of the prefix, exclude
        // operators from unrelated positions.
        return expected;
    }

    private static string MethodBeforeClosedCall(IReadOnlyList<MongoToken> tokens, string text, int closeIndex)
    {
        var depth = 0;
        for (var index = closeIndex; index >= 0; index--)
        {
            var value = TokenText(tokens, text, index);
            if (value == ")") depth++;
            else if (value == "(" && depth-- == 1)
                return index > 0 ? TokenText(tokens, text, index - 1) : "";
        }
        return "";
    }

    private static bool HasConnectionPoolReceiver(IReadOnlyList<MongoToken> tokens, string text, int index)
    {
        for (var cursor = Math.Max(0, index - 6); cursor <= index; cursor++)
            if (TokenText(tokens, text, cursor) == "ConnectionPool") return true;
        return false;
    }

    private static bool IsNonCompletable(MongoToken token) => token.Kind is MongoTokenKind.LineComment or MongoTokenKind.BlockComment or MongoTokenKind.Regex or MongoTokenKind.Number or MongoTokenKind.Template;
    private static int FindTokenAt(IReadOnlyList<MongoToken> tokens, int caret)
    {
        for (var index = 0; index < tokens.Count; index++) if (tokens[index].Span.IntersectsWith(caret)) return index;
        return -1;
    }
    private static int PreviousIndex(IReadOnlyList<MongoToken> tokens, int exclusive) => Math.Clamp(exclusive - 1, -1, tokens.Count - 1);

    private static int ExclusiveBeforeCaret(IReadOnlyList<MongoToken> tokens, int caret)
    {
        var exclusive = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].End > caret) break;
            exclusive = index + 1;
        }
        return exclusive;
    }

    private static bool IsObjectMemberPosition(IReadOnlyList<MongoToken> tokens, string text, int memberIndex, bool allowMissingComma)
    {
        if (memberIndex < 0 || memberIndex >= tokens.Count) return false;
        if (tokens[memberIndex].Has(MongoTokenTraits.FollowedByColon)) return true;
        var frames = new Stack<char>();
        for (var index = 0; index < memberIndex; index++)
        {
            var value = TokenText(tokens, text, index);
            if (value is "{" or "[") frames.Push(value[0]);
            else if (value == "}" && frames.TryPeek(out var frame) && frame == '{') frames.Pop();
            else if (value == "]" && frames.TryPeek(out var closingFrame) && closingFrame == '[') frames.Pop();
        }
        if (!frames.TryPeek(out var top) || top != '{') return false;
        var previous = memberIndex == 0 ? "" : TokenText(tokens, text, memberIndex - 1);
        if (previous is "{" or ",") return true;
        if (!allowMissingComma || memberIndex == 0) return false;
        return tokens[memberIndex - 1].Kind is MongoTokenKind.String or MongoTokenKind.Number
            || previous is "}" or "]";
    }

    private static string TokenText(IReadOnlyList<MongoToken> tokens, string text, int index) =>
        index < 0 || index >= tokens.Count ? "" : text.Substring(tokens[index].Start, tokens[index].Length);

    private static char? DominantPropertyQuote(IReadOnlyList<MongoToken> tokens, string text, int caret)
    {
        var doubleCount = 0;
        var singleCount = 0;
        foreach (var token in tokens)
        {
            if (token.Start >= caret || token.Kind != MongoTokenKind.String || !token.Has(MongoTokenTraits.FollowedByColon)) continue;
            if (text[token.Start] == '"') doubleCount++;
            else if (text[token.Start] == '\'') singleCount++;
        }
        return doubleCount == singleCount ? null : doubleCount > singleCount ? '"' : '\'';
    }

    private static bool TryClassifyContextualStringValue(IReadOnlyList<MongoToken> tokens, string text, int current, out SymbolKinds kind)
    {
        kind = SymbolKinds.None;
        var previous = TokenText(tokens, text, current - 1);
        if (previous == ":")
        {
            var property = Unquote(TokenText(tokens, text, current - 2));
            kind = property switch
            {
                "from" or "coll" => SymbolKinds.Collection,
                "hint" => SymbolKinds.Index,
                "localField" or "foreignField" or "path" or "$getField" => SymbolKinds.Field,
                _ => SymbolKinds.None
            };
            return kind != SymbolKinds.None;
        }
        if (previous == "(")
        {
            var method = TokenText(tokens, text, current - 2);
            if (method is "$getField" or "getField") { kind = SymbolKinds.Field; return true; }
        }
        return false;
    }

    private static string PropertyNameBeforeString(IReadOnlyList<MongoToken> tokens, string text, int current) =>
        TokenText(tokens, text, current - 1) == ":" ? Unquote(TokenText(tokens, text, current - 2)) : "";

    private static string Unquote(string value) => value.Length >= 2 && value[0] is '\'' or '"' ? value[1..^1] : value;

    private static string PreviousText(IReadOnlyList<MongoToken> tokens, string text, int exclusive)
    {
        var index = PreviousIndex(tokens, exclusive);
        return index < 0 ? string.Empty : text.Substring(tokens[index].Start, tokens[index].Length);
    }
    private static string TextBeforeCaret(string text, MongoToken token, int caret)
    {
        var start = token.Kind == MongoTokenKind.String ? token.Start + 1 : token.Start;
        return text.Substring(start, Math.Clamp(caret, start, token.End) - start);
    }

    private static TextSpan StringContentSpan(string text, MongoToken token, int caret)
    {
        var start = Math.Min(token.Start + 1, token.End);
        var end = Math.Clamp(caret, start, token.End);
        if (end > start && text[end - 1] is '\'' or '"' or '`') end--;
        return new TextSpan(start, Math.Max(0, end - start));
    }
}
