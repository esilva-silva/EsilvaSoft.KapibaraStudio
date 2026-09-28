using System.Buffers;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

/// <summary>
/// Fault-tolerant visual classification over <see cref="MongoLexer"/> tokens, with a per-line cache. It does not parse,
/// validate, execute or normalize BSON; MongoDB meaning comes from the language vocabulary and loaded namespace names.
/// </summary>
public sealed class SyntaxHighlightingService : ISyntaxHighlightingService
{
    public SyntaxSnapshot Highlight(string text, SyntaxLanguage language, SyntaxContext? context = null,
        SyntaxSnapshot? previous = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        context ??= SyntaxContext.Empty;
        var reusable = previous is not null && previous.Language == language && previous.Context == context;
        if (reusable && ReferenceEquals(previous!.Text, text)) return previous;
        var oldLines = reusable ? previous!.Lines : [];
        var state = new HighlightState(Connection: context.Connection, Database: context.Database, Collection: context.Collection);
        var starts = new List<(int Start, int Length)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (text[i] != '\n') continue;
            starts.Add((start, i + 1 - start)); start = i + 1;
        }
        if (start < text.Length) starts.Add((start, text.Length - start));
        // Align unchanged suffixes even when new lines have been inserted/deleted.
        var suffix = 0;
        while (suffix < Math.Min(starts.Count, oldLines.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = starts[^(suffix + 1)];
            if (!text.AsSpan(span.Start, span.Length).SequenceEqual(oldLines[^(suffix + 1)].Text)) break;
            suffix++;
        }
        var mode = language == SyntaxLanguage.Json ? MongoLexerMode.Json : MongoLexerMode.Script;
        var lines = new SyntaxLine[starts.Count];
        var processed = 0;
        var total = 0;
        var frames = ArrayPool<char>.Shared.Rent(SyntaxHighlightingOptions.MaximumNesting);
        var scratch = new List<SyntaxToken>();
        try
        {
            for (var index = 0; index < starts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = starts[index];
                var oldIndex = index >= starts.Count - suffix ? oldLines.Count - (starts.Count - index) : index;
                var old = oldIndex >= 0 && oldIndex < oldLines.Count ? oldLines[oldIndex] : null;
                SyntaxLine line;
                if (old is not null && old.Before == state && text.AsSpan(span.Start, span.Length).SequenceEqual(old.Text)) line = old;
                else
                {
                    var value = text.Substring(span.Start, span.Length);
                    var before = state;
                    var local = TokenizeLine(value, mode, language, context, ref state, frames, scratch, cancellationToken);
                    line = new(value, before, state, local); processed++;
                }
                state = line.After;
                lines[index] = line;
                total += line.Tokens.Length;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(frames);
        }
        var tokens = new SyntaxToken[total];
        var position = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var offset = starts[index].Start;
            foreach (var token in lines[index].Tokens) tokens[position++] = token with { Start = token.Start + offset };
        }
        var brackets = new Dictionary<int, int>();
        var stack = new Stack<(char Bracket, int Position)>();
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type != SyntaxTokenType.Punctuation || token.Length != 1) continue;
            var ch = text[token.Start];
            if (ch is '(' or '[' or '{') stack.Push((ch, token.Start));
            else if (ch is ')' or ']' or '}')
            {
                if (stack.TryPeek(out var opening) && Matches(opening.Bracket, ch))
                {
                    stack.Pop(); brackets[opening.Position] = token.Start; brackets[token.Start] = opening.Position;
                }
                else brackets[token.Start] = -1;
            }
        }
        foreach (var opening in stack) brackets[opening.Position] = -1;
        return new(text, language, context, tokens, brackets, processed)
        {
            Lines = starts.Count <= SyntaxHighlightingOptions.MaximumCachedLines ? lines : [],
            LineStarts = starts.Select(s => s.Start).ToArray()
        };
    }

    private static bool Matches(char a, char b) => (a, b) is ('(', ')') or ('[', ']') or ('{', '}');

    private static SyntaxToken[] TokenizeLine(string line, MongoLexerMode mode, SyntaxLanguage language, SyntaxContext context,
        ref HighlightState state, char[] frames, List<SyntaxToken> tokens, CancellationToken cancellationToken)
    {
        tokens.Clear();
        var classifier = new SyntaxHighlightClassifier(state, frames, language, context);
        var lexer = new MongoLexer(line, state.Lexer, mode, 0, cancellationToken);
        while (lexer.TryRead(out var token))
        {
            var type = token.Kind switch
            {
                MongoTokenKind.LineComment or MongoTokenKind.BlockComment => SyntaxTokenType.Comment,
                MongoTokenKind.String or MongoTokenKind.Template => classifier.Literal(line, token),
                MongoTokenKind.Regex => classifier.Operand(SyntaxTokenType.Regex),
                MongoTokenKind.Number => classifier.Operand(SyntaxTokenType.Number),
                MongoTokenKind.Identifier => classifier.Word(line.Substring(token.Start, token.Length), token.Traits),
                _ => classifier.Symbol(line[token.Start], token.Kind)
            };
            tokens.Add(new(token.Start, token.Length, type));
        }
        state = classifier.ToState(lexer.State, state);
        return tokens.ToArray();
    }
}
