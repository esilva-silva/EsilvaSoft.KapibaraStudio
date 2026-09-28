using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Pure, bounded resolution using MongoLexer tokens. Supports top-level static chains and unique, earlier const
/// namespace aliases. Opaque scopes, dynamic arguments and ambiguous syntax return Unknown. It performs no I/O,
/// JavaScript evaluation or lookup of saved profiles. A ContextEngine may use ConnectionName to resolve an identity separately.
/// </summary>
public static class NamespaceTargetResolver
{
    public const int MaximumDocumentLength = 65_536;
    public const int MaximumTokens = 16_384;
    public const int MaximumDepth = 512;

    /// <summary>
    /// Resolves the top-level expression containing the UTF-16 caret, including arguments of known MongoDB methods.
    /// Only AggregationJson uses the tab collection as a default. Larger documents require a trusted statement window
    /// from the caller; this resolver never truncates a document and guesses its lexical state or distant aliases.
    /// </summary>
    public static NamespaceTarget Resolve(string document, int caret, NamespaceTarget? tabTarget = null,
        EditorDialects dialect = EditorDialects.Console, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!TryShortCircuit(document, caret, tabTarget, dialect, cancellationToken, out var target)) return target;

        var lexed = new List<MongoToken>();
        MongoLexer.Tokenize(document.AsSpan(), lexed, cancellationToken: cancellationToken);
        return ResolveCore(document, lexed, caret, tabTarget, cancellationToken);
    }

    /// <summary>
    /// Same resolution over tokens already produced for this exact document by <see cref="MongoLexer"/> in
    /// <see cref="MongoLexerMode.Script"/> (e.g. the tokens of a cached syntax tree), so that typing does not lex twice.
    /// Comments are filtered here: the caller passes the complete token stream of the document.
    /// </summary>
    public static NamespaceTarget Resolve(string document, IReadOnlyList<MongoToken> documentTokens, int caret,
        NamespaceTarget? tabTarget = null, EditorDialects dialect = EditorDialects.Console, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentTokens);
        if (!TryShortCircuit(document, caret, tabTarget, dialect, cancellationToken, out var target)) return target;
        return ResolveCore(document, documentTokens, caret, tabTarget, cancellationToken);
    }

    /// <summary>False when the dialect or the document size already decides the answer, without reading any token.</summary>
    private static bool TryShortCircuit(string document, int caret, NamespaceTarget? tabTarget, EditorDialects dialect,
        CancellationToken cancellationToken, out NamespaceTarget target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, document.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (dialect == EditorDialects.AggregationJson)
        {
            target = tabTarget is null ? NamespaceTarget.Unknown : tabTarget with { Confidence = NamespaceTargetConfidence.TabDefault };
            return false;
        }
        if (dialect is not (EditorDialects.Console or EditorDialects.MongoshScript) || document.Length > MaximumDocumentLength)
        {
            target = NamespaceTarget.Unknown;
            return false;
        }
        target = NamespaceTarget.Unknown;
        return true;
    }

    private static NamespaceTarget ResolveCore(string document, IReadOnlyList<MongoToken> documentTokens, int caret,
        NamespaceTarget? tabTarget, CancellationToken cancellationToken)
    {
        var tokens = new List<MongoToken>(Math.Min(documentTokens.Count, MaximumTokens + 1));
        for (var index = 0; index < documentTokens.Count; index++)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var token = documentTokens[index];
            // Do not interpret roots inside opaque lexical content, including an open comment at EOF.
            if (token.Start < caret && (caret < token.End || caret == token.End &&
                (!token.IsTerminated || token.Kind == MongoTokenKind.LineComment)) &&
                (token.IsComment || token.Kind is MongoTokenKind.Regex or MongoTokenKind.Template))
                return NamespaceTarget.Unknown;
            if (!token.IsComment) tokens.Add(token);
            if (tokens.Count > MaximumTokens) return NamespaceTarget.Unknown;
        }
        return new NamespaceChainReader(document, tokens, tabTarget, cancellationToken).Resolve(caret);
    }
}
