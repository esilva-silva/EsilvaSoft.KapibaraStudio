using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

/// <summary>Lexical category only; MongoDB meaning (stage, operator, namespace, property) is assigned by consumers.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "String is the lexical category name, not a variable type prefix.")]
public enum MongoTokenKind : byte
{
    Identifier, String, Template, Number, Regex, LineComment, BlockComment, Punctuation, Operator
}

[Flags]
public enum MongoTokenTraits : byte
{
    None = 0,
    /// <summary>String, template, block comment or regex whose closing delimiter is not on this line.</summary>
    Unterminated = 1,
    /// <summary>Continues a string, template or block comment opened on a previous line.</summary>
    Continuation = 2,
    /// <summary>Identifier, string or template whose next non-white character on the same line is ':'.</summary>
    FollowedByColon = 4,
    /// <summary>Identifier whose next non-white character on the same line is '('.</summary>
    FollowedByOpenParenthesis = 8,
}

/// <summary>Script: '/' may start a regex literal. Json: '/' is never a regex.</summary>
public enum MongoLexerMode : byte { Script, Json }

public readonly record struct MongoToken(int Start, int Length, MongoTokenKind Kind, MongoTokenTraits Traits)
{
    public int End => Start + Length;
    public TextSpan Span => new(Start, Length);
    public bool IsTerminated => (Traits & MongoTokenTraits.Unterminated) == 0;
    public bool IsComment => Kind is MongoTokenKind.LineComment or MongoTokenKind.BlockComment;
    public bool Has(MongoTokenTraits flags) => (Traits & flags) == flags;
}

/// <summary>
/// Lexical state at a line boundary. Value equality makes it a cache key: a line with the same text and the same state
/// before it yields the same tokens and the same state after it. <c>default</c> is the state at the start of a document.
/// </summary>
/// <param name="OpenQuote">Quote (<c>"</c>, <c>'</c> or <c>`</c>) of a string still open at the end of the line, or <c>'\0'</c>.</param>
/// <param name="InBlockComment">A <c>/*</c> comment is still open.</param>
/// <param name="SlashIsDivision">The last significant token makes <c>/</c> a division and <c>-</c> a binary minus (no regex or negative literal).</param>
public readonly record struct MongoLexerState(char OpenQuote, bool InBlockComment, bool SlashIsDivision)
{
    /// <summary>The next line starts outside strings, templates and comments.</summary>
    public bool IsCodeContext => OpenQuote == '\0' && !InBlockComment;
}

/// <summary>
/// Fault-tolerant MongoDB/JavaScript lexer shared by highlighting and parsing; the only lexer of the language services.
/// It reads one line at a time — the text up to and including '\n' (a CR stays in the line as whitespace) — and carries
/// <see cref="MongoLexerState"/> across lines, so consumers can cache tokens per line. It does not allocate, evaluate,
/// validate or split template substitutions; unterminated constructs end at the end of the line.
/// </summary>
public ref struct MongoLexer
{
    private readonly ReadOnlySpan<char> _line;
    private readonly int _offset;
    private readonly bool _json;
    private readonly CancellationToken _cancellationToken;
    private int _position;
    private MongoLexerState _state;

    /// <param name="line">One line, including its trailing '\n' when present.</param>
    /// <param name="state">State after the previous line (<c>default</c> for the first line).</param>
    /// <param name="mode">Script or Json.</param>
    /// <param name="offset">Added to token starts, e.g. the line start for document offsets.</param>
    /// <param name="cancellationToken">Checked periodically.</param>
    public MongoLexer(ReadOnlySpan<char> line, MongoLexerState state, MongoLexerMode mode = MongoLexerMode.Script, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _line = line; _state = state; _json = mode == MongoLexerMode.Json; _offset = offset; _cancellationToken = cancellationToken;
        _position = 0;
    }

    /// <summary>State after the tokens read so far; after the last token it is the state for the next line.</summary>
    public readonly MongoLexerState State => _state;

    public bool TryRead(out MongoToken token)
    {
        var text = _line;
        var i = _position;
        while (i < text.Length)
        {
            if ((i & 255) == 0) _cancellationToken.ThrowIfCancellationRequested();
            var start = i;
            var ch = text[i];
            if (_state.InBlockComment)
            {
                var close = text[i..].IndexOf("*/", StringComparison.Ordinal);
                i = close < 0 ? text.Length : i + close + 2;
                _state = _state with { InBlockComment = close < 0 };
                return Emit(out token, start, i, MongoTokenKind.BlockComment,
                    MongoTokenTraits.Continuation | (close < 0 ? MongoTokenTraits.Unterminated : MongoTokenTraits.None));
            }
            if (_state.OpenQuote != '\0' || ch is '"' or '\'' or '`') return ReadString(out token, start);
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/') return Emit(out token, start, text.Length, MongoTokenKind.LineComment, MongoTokenTraits.None);
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                i = close < 0 ? text.Length : i + 2 + close + 2;
                _state = _state with { InBlockComment = close < 0 };
                return Emit(out token, start, i, MongoTokenKind.BlockComment, close < 0 ? MongoTokenTraits.Unterminated : MongoTokenTraits.None);
            }
            if (ch == '/' && !_json && !_state.SlashIsDivision) return ReadRegex(out token, start);
            if (char.IsDigit(ch) || ch == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && !_state.SlashIsDivision)
            {
                if (ch == '-') i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or '_')) i++;
                if (i < text.Length && text[i] is 'e' or 'E')
                {
                    i++;
                    if (i < text.Length && text[i] is '+' or '-') i++;
                    while (i < text.Length && char.IsDigit(text[i])) i++;
                }
                _state = _state with { SlashIsDivision = true };
                return Emit(out token, start, i, MongoTokenKind.Number, MongoTokenTraits.None);
            }
            if (IsWordCharacter(ch))
            {
                while (i < text.Length && IsWordCharacter(text[i])) i++;
                var next = SkipWhiteSpace(text, i);
                var flags = next >= text.Length ? MongoTokenTraits.None
                    : text[next] == ':' ? MongoTokenTraits.FollowedByColon
                    : text[next] == '(' ? MongoTokenTraits.FollowedByOpenParenthesis : MongoTokenTraits.None;
                _state = _state with { SlashIsDivision = text[start..i] is not "return" };
                return Emit(out token, start, i, MongoTokenKind.Identifier, flags);
            }
            var punctuation = ch is '{' or '}' or '[' or ']' or '(' or ')' or ':' or ',' or ';' or '.';
            _state = _state with { SlashIsDivision = ch is not ('(' or '[' or '{' or ':' or ',' or '=' or '!' or ';') };
            return Emit(out token, start, i + 1, punctuation ? MongoTokenKind.Punctuation : MongoTokenKind.Operator, MongoTokenTraits.None);
        }
        _position = i;
        token = default;
        return false;
    }

    /// <summary>Tokenizes a whole text line by line with document offsets and returns the state after the last line.</summary>
    public static MongoLexerState Tokenize(ReadOnlySpan<char> text, ICollection<MongoToken> tokens, MongoLexerState state = default,
        MongoLexerMode mode = MongoLexerMode.Script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        for (var start = 0; start < text.Length;)
        {
            var length = LineLength(text, start);
            var lexer = new MongoLexer(text.Slice(start, length), state, mode, start, cancellationToken);
            while (lexer.TryRead(out var token)) tokens.Add(token);
            state = lexer.State;
            start += length;
        }
        return state;
    }

    /// <summary>Length of the line that starts at <paramref name="start"/>: through the next '\n', or to the end of the text.</summary>
    public static int LineLength(ReadOnlySpan<char> text, int start)
    {
        var newline = text[start..].IndexOf('\n');
        return newline < 0 ? text.Length - start : newline + 1;
    }

    private bool ReadString(out MongoToken token, int start)
    {
        var text = _line;
        var continuing = _state.OpenQuote != '\0';
        var quote = continuing ? _state.OpenQuote : text[start];
        var i = continuing ? start : start + 1;
        var closed = false;
        while (i < text.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var found = text[i..].IndexOfAny(quote, '\\');
            if (found < 0) { i = text.Length; break; }
            i += found;
            if (text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
            i++; closed = true; break;
        }
        var flags = (continuing ? MongoTokenTraits.Continuation : MongoTokenTraits.None) | (closed ? MongoTokenTraits.None : MongoTokenTraits.Unterminated);
        var next = SkipWhiteSpace(text, i);
        if (next < text.Length && text[next] == ':') flags |= MongoTokenTraits.FollowedByColon;
        _state = _state with { OpenQuote = closed ? '\0' : quote };
        // A property key leaves its own content as the previous token; any other closed literal is an operand.
        if (closed)
            _state = _state with
            {
                SlashIsDivision = continuing || (flags & MongoTokenTraits.FollowedByColon) == 0 || !AllowsRegexAfter(text[(start + 1)..(i - 1)])
            };
        return Emit(out token, start, i, quote == '`' ? MongoTokenKind.Template : MongoTokenKind.String, flags);
    }

    private bool ReadRegex(out MongoToken token, int start)
    {
        var text = _line;
        var i = start + 1;
        var inClass = false;
        var closed = false;
        while (i < text.Length)
        {
            var current = text[i];
            if (current == '\\') { i = Math.Min(text.Length, i + 2); continue; }
            if (current == '[') inClass = true;
            if (current == ']') inClass = false;
            i++;
            if (current == '/' && !inClass) { closed = true; break; }
        }
        while (i < text.Length && char.IsLetter(text[i])) i++;
        _state = _state with { SlashIsDivision = true };
        return Emit(out token, start, i, MongoTokenKind.Regex, closed ? MongoTokenTraits.None : MongoTokenTraits.Unterminated);
    }

    private bool Emit(out MongoToken token, int start, int end, MongoTokenKind kind, MongoTokenTraits flags)
    {
        token = new(_offset + start, end - start, kind, flags);
        _position = end;
        return true;
    }

    private static bool AllowsRegexAfter(ReadOnlySpan<char> previous) =>
        previous is "" or "(" or "[" or "{" or ":" or "," or "=" or "return" or "=>" or "!" or ";";

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static int SkipWhiteSpace(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }
}
