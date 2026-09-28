using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

/// <summary>
/// Small, non-evaluating structural parser for completion. It deliberately does not validate JavaScript or MongoDB;
/// malformed input is represented by diagnostics and opaque nodes so completion can continue.
/// </summary>
public sealed class TolerantParser
{
    private const int MaxNestingDepth = 512;
    private static readonly Dictionary<char, char> Closing = new() { ['{'] = '}', ['['] = ']', ['('] = ')' };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance API keeps the parser replaceable for future grammar services.")]
    public MongoSyntaxTree Parse(ITextSnapshot snapshot, MongoLexerMode mode = MongoLexerMode.Script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = snapshot.GetText(0, snapshot.Length);
        var tokens = new List<MongoToken>(Math.Min(text.Length / 3, 4096));
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: mode, cancellationToken: cancellationToken);
        return ParseCore(snapshot.Version, text, tokens, cancellationToken);
    }

    /// <summary>
    /// Reparses only the statements touched by the edit from the parser's point of view. The lexer still runs once over
    /// the new snapshot to establish safe statement boundaries; unaffected statements retain their immutable syntax nodes.
    /// </summary>
    /// <remarks>
    /// A statement is only retained when its span is identical in both versions: rewriting the spans of everything after
    /// an insertion would mean cloning those subtrees, which costs more than the nodes the full parse already produced
    /// (measured: up to 40x and ~100 MB per keystroke on a 1 MiB document). Reuse by relative widths, which makes
    /// shifting free, belongs to the green-tree step that is still pending.
    /// </remarks>
    public MongoSyntaxTree ParseIncremental(ITextSnapshot previousSnapshot, MongoSyntaxTree previousTree, ITextSnapshot snapshot,
        IReadOnlyList<TextChange> changes, MongoLexerMode mode = MongoLexerMode.Script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previousSnapshot);
        ArgumentNullException.ThrowIfNull(previousTree);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) return Parse(snapshot, mode, cancellationToken);
        if (!previousSnapshot.Version.IsSameDocument(snapshot.Version) || previousTree.Version != previousSnapshot.Version)
            return Parse(snapshot, mode, cancellationToken);

        var full = Parse(snapshot, mode, cancellationToken);
        var firstOldStart = int.MaxValue;
        var lastOldEnd = 0;
        var cumulativeDelta = 0;
        foreach (var change in changes)
        {
            var oldStart = change.Start - cumulativeDelta;
            firstOldStart = Math.Min(firstOldStart, oldStart);
            lastOldEnd = Math.Max(lastOldEnd, oldStart + change.OldLength);
            cumulativeDelta += change.Delta;
        }
        // Um índice por span mantém a mesclagem linear no número de statements; a varredura por statement era quadrática.
        var oldStatements = new Dictionary<TextSpan, MongoSyntaxNode>(previousTree.Root.Children.Count);
        foreach (var node in previousTree.Root.Children)
            if (node.Kind is MongoSyntaxNodeKind.Statement or MongoSyntaxNodeKind.OpaqueStatement)
                oldStatements.TryAdd(node.Span, node);
        var newStatements = full.Root.Children;
        var merged = new MongoSyntaxNode[newStatements.Count];
        var reused = 0;
        for (var index = 0; index < newStatements.Count; index++)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var candidate = newStatements[index];
            var isBeforeTheEdit = candidate.Span.End <= firstOldStart;
            var isAfterTheEdit = cumulativeDelta == 0 && candidate.Span.Start >= lastOldEnd;
            if ((isBeforeTheEdit || isAfterTheEdit) && oldStatements.TryGetValue(candidate.Span, out var old))
            {
                merged[index] = old;
                reused++;
            }
            else merged[index] = candidate;
        }
        if (reused == 0) return full;
        var root = new MongoSyntaxNode(MongoSyntaxNodeKind.Document, new(0, snapshot.Length), merged);
        return new(snapshot.Version, root, full.Tokens, full.Diagnostics, reused);
    }

    private static MongoSyntaxTree ParseCore(TextSnapshotVersion version, string source, List<MongoToken> tokens,
        CancellationToken cancellationToken = default)
    {
        var documentLength = source.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(documentLength);
        ArgumentNullException.ThrowIfNull(tokens);
        var diagnostics = new List<MongoSyntaxDiagnostic>();
        var statements = new List<MongoSyntaxNode>();
        var current = new List<MongoSyntaxNode>();
        var stack = new Stack<(char Open, int Start, List<MongoSyntaxNode> Children)>();
        var statementStart = -1;
        MongoToken? previous = null;

        for (var index = 0; index < tokens.Count; index++)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var token = tokens[index];
            if (statementStart < 0) statementStart = token.Start;

            if (!token.IsTerminated)
                diagnostics.Add(new(MongoSyntaxDiagnosticKind.Unterminated, token.Span, "Construção lexical não terminada."));

            if (token.Kind is MongoTokenKind.String or MongoTokenKind.Identifier && previous is { Kind: MongoTokenKind.Punctuation } p && p.End <= token.Start && source[p.Start..p.End] == ".")
                diagnostics.Add(new(MongoSyntaxDiagnosticKind.DottedKeyRecovery, token.Span, "Caminho pontuado recuperado."));

            var node = new MongoSyntaxNode(MongoSyntaxNodeKind.Token, token.Span, token: token);
            var character = token.Length == 1 && token.Start < source.Length ? source[token.Start] : '\0';
            if (token.Kind == MongoTokenKind.Punctuation && character is '{' or '[' or '(' && Closing.ContainsKey(character))
            {
                if (stack.Count >= MaxNestingDepth)
                {
                    diagnostics.Add(new(MongoSyntaxDiagnosticKind.SkippedTokens, token.Span,
                        $"Profundidade máxima de {MaxNestingDepth} níveis atingida."));
                    current.Add(node);
                }
                else
                {
                    stack.Push((character, token.Start, current));
                    current = [];
                }
            }
            else if (token.Kind == MongoTokenKind.Punctuation && character is '}' or ']' or ')' && Closing.ContainsValue(character))
            {
                if (stack.Count == 0)
                {
                    diagnostics.Add(new(MongoSyntaxDiagnosticKind.SkippedTokens, token.Span, "Fechamento sem abertura correspondente."));
                    current.Add(node);
                }
                else
                {
                    var frame = stack.Pop();
                    var expected = Closing[frame.Open];
                    if (character != expected)
                    {
                        diagnostics.Add(new(MongoSyntaxDiagnosticKind.SkippedTokens, token.Span,
                            $"Fechamento '{character}' não corresponde a '{expected}'."));
                        stack.Push(frame);
                        current.Add(node);
                        previous = token;
                        continue;
                    }
                    var span = TextSpan.FromBounds(frame.Start, token.End);
                    var kind = frame.Open == '{' ? MongoSyntaxNodeKind.Object : frame.Open == '[' ? MongoSyntaxNodeKind.Array : MongoSyntaxNodeKind.Group;
                    var completed = new MongoSyntaxNode(kind, span, [.. current, node]);
                    current = frame.Children;
                    current.Add(completed);
                }
            }
            else
            {
                current.Add(node);
                if (token.Kind == MongoTokenKind.Punctuation && character == ';' && stack.Count == 0)
                {
                    statements.Add(CreateStatement(statementStart, token.End, current));
                    current = [];
                    statementStart = -1;
                }
            }
            previous = token;
        }

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            var end = documentLength;
            diagnostics.Add(new(MongoSyntaxDiagnosticKind.MissingClose, TextSpan.FromBounds(frame.Start, end), $"Fechamento '{Closing[frame.Open]}' ausente."));
            var kind = frame.Open == '{' ? MongoSyntaxNodeKind.Object : frame.Open == '[' ? MongoSyntaxNodeKind.Array : MongoSyntaxNodeKind.Group;
            var incomplete = new MongoSyntaxNode(kind, TextSpan.FromBounds(frame.Start, end), [.. current]);
            current = frame.Children;
            current.Add(incomplete);
        }

        if (current.Count > 0)
            statements.Add(CreateStatement(statementStart < 0 ? current[0].Span.Start : statementStart, documentLength, current));

        var root = new MongoSyntaxNode(MongoSyntaxNodeKind.Document, new(0, documentLength), statements);
        return new(version, root, tokens, diagnostics);
    }

    private static MongoSyntaxNode CreateStatement(int start, int end, List<MongoSyntaxNode> children) =>
        new(children.Count > 0 && children.Any(x => x.Kind is MongoSyntaxNodeKind.Object or MongoSyntaxNodeKind.Array or MongoSyntaxNodeKind.Group)
            ? MongoSyntaxNodeKind.Statement : MongoSyntaxNodeKind.OpaqueStatement,
            TextSpan.FromBounds(Math.Max(0, start), Math.Max(start, end)), [.. children]);

}
