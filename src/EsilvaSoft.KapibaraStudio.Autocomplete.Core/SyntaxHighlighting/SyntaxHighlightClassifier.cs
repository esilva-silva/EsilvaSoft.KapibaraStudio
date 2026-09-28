using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

/// <summary>Mutable working copy of <see cref="HighlightState"/> for one line; materialized once at the end of the line.</summary>
internal struct SyntaxHighlightClassifier
{
    private const string OperandMarker = "<value>";
    private static readonly string[] AsciiStrings = Enumerable.Range(0, 128).Select(c => ((char)c).ToString()).ToArray();

    private readonly char[] _frames;
    private readonly SyntaxLanguage _language;
    private readonly SyntaxContext _context;
    private int _depth, _searchFrames, _aggregateFrames;
    private bool _searchPending, _aggregatePending;
    private string _previous, _beforePrevious, _connection, _database, _collection;

    public SyntaxHighlightClassifier(HighlightState state, char[] frames, SyntaxLanguage language, SyntaxContext context)
    {
        _frames = frames; _language = language; _context = context;
        state.Frames.CopyTo(frames);
        _depth = state.Frames.Length;
        _searchFrames = state.Frames.AsSpan().Count('S');
        _aggregateFrames = state.Frames.AsSpan().Count('A');
        _searchPending = state.SearchPending; _aggregatePending = state.AggregatePending;
        _previous = state.Previous; _beforePrevious = state.BeforePrevious;
        _connection = state.Connection; _database = state.Database; _collection = state.Collection;
    }

    public readonly HighlightState ToState(MongoLexerState lexer, HighlightState before)
    {
        var frames = before.Frames.AsSpan().SequenceEqual(_frames.AsSpan(0, _depth)) ? before.Frames : new string(_frames, 0, _depth);
        return new(lexer, frames, _searchPending, _aggregatePending, _previous, _beforePrevious, _connection, _database, _collection);
    }

    public SyntaxTokenType Literal(string line, MongoToken token)
    {
        var continuing = token.Has(MongoTokenTraits.Continuation);
        var closed = token.IsTerminated;
        if (!continuing && closed && token.Has(MongoTokenTraits.FollowedByColon))
        {
            var value = line.Substring(token.Start + 1, token.Length - 2);
            var property = ClassifyProperty(value);
            Pending(value); Shift(value);
            return property;
        }
        var type = closed && !continuing ? ClassifyName(line.AsSpan(token.Start + 1, token.Length - 2), null, SyntaxTokenType.String) : SyntaxTokenType.String;
        if (closed) Shift(OperandMarker);
        return type;
    }

    public SyntaxTokenType Operand(SyntaxTokenType type)
    {
        Shift(OperandMarker);
        return type;
    }

    public SyntaxTokenType Word(string value, MongoTokenTraits flags)
    {
        if (value == "db") { _connection = _context.Connection; _database = _context.Database; _collection = _context.Collection; }
        var property = (flags & MongoTokenTraits.FollowedByColon) != 0;
        var call = (flags & MongoTokenTraits.FollowedByOpenParenthesis) != 0;
        var type = property ? ClassifyProperty(value) : value switch
        {
            "true" or "false" => SyntaxTokenType.Boolean,
            "null" or "undefined" => SyntaxTokenType.Null,
            _ when call && MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value) => SyntaxTokenType.MongoType,
            _ when call && MongoSyntaxVocabulary.DslFunctions.Contains(value) => SyntaxTokenType.Function,
            _ when call && MongoSyntaxVocabulary.Functions.Contains(value) => SyntaxTokenType.MongoFunction,
            _ when _language != SyntaxLanguage.Json && MongoSyntaxVocabulary.Keywords.Contains(value) => SyntaxTokenType.Keyword,
            _ when value.StartsWith('$') => SyntaxTokenType.MongoOperator,
            _ when call => _previous == "." ? SyntaxTokenType.Method : SyntaxTokenType.Function,
            _ => SyntaxTokenType.Identifier
        };
        if (!property && !call) type = ClassifyName(value, value, type);
        Pending(value); Shift(value);
        return type;
    }

    public SyntaxTokenType Symbol(char ch, MongoTokenKind kind)
    {
        if (ch is '{' or '[' or '(')
        {
            var frame = ch == '{' && _searchPending ? 'S'
                : ch == '[' && (_aggregatePending || _language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch) ? 'A' : ch;
            if (_depth < SyntaxHighlightingOptions.MaximumNesting)
            {
                _frames[_depth++] = frame;
                if (frame == 'S') _searchFrames++;
                else if (frame == 'A') _aggregateFrames++;
            }
            if (ch == '{') _searchPending = false;
            if (ch == '[') _aggregatePending = false;
        }
        else if (ch is '}' or ']' or ')' && _depth > 0)
        {
            var frame = _frames[--_depth];
            if (frame == 'S') _searchFrames--;
            else if (frame == 'A') _aggregateFrames--;
        }
        Shift(ch < AsciiStrings.Length ? AsciiStrings[ch] : ch.ToString());
        return kind == MongoTokenKind.Punctuation ? SyntaxTokenType.Punctuation : SyntaxTokenType.Operator;
    }

    private void Shift(string value) { _beforePrevious = _previous; _previous = value; }

    private void Pending(string value)
    {
        _searchPending = value is "$search" or "$searchMeta" || _searchPending;
        _aggregatePending = value == "aggregate" || _aggregatePending;
    }

    private readonly SyntaxTokenType ClassifyProperty(string value)
    {
        if (MongoSyntaxVocabulary.ExtendedJsonTypes.Contains(value)) return SyntaxTokenType.MongoType;
        if (MongoSyntaxVocabulary.AggregationStages.Contains(value)
            && (value is not ("$set" or "$unset") || _language is SyntaxLanguage.Aggregation or SyntaxLanguage.AtlasSearch || _aggregateFrames > 0))
            return SyntaxTokenType.MongoStage;
        if (value.StartsWith('$')) return SyntaxTokenType.MongoOperator;
        if (MongoSyntaxVocabulary.AtlasSearchOperators.Contains(value) && (_language == SyntaxLanguage.AtlasSearch || _searchFrames > 0))
            return SyntaxTokenType.AtlasSearchOperator;
        return SyntaxTokenType.PropertyName;
    }

    /// <summary>Namespace names only match loaded metadata; the value string is materialized only when it becomes state.</summary>
    private SyntaxTokenType ClassifyName(ReadOnlySpan<char> value, string? text, SyntaxTokenType fallback)
    {
        var names = _context.Names;
        if (names.Count == 0) return fallback;
        var connection = _connection; var database = _database; var collection = _collection;
        var previous = _previous; var before = _beforePrevious;
        if (previous == "(" && before == "getConnection" || previous == "." && MongoSyntaxVocabulary.DslRoots.Contains(before))
            for (var i = 0; i < names.Count; i++)
                if (value.SequenceEqual(names[i].Connection))
                { _connection = text ?? value.ToString(); _database = ""; _collection = ""; return SyntaxTokenType.Connection; }
        if (previous == "(" && before is "getDatabase" or "GetDatabase" or "getSiblingDB" or "getDB" || previous == "." && before == connection)
            for (var i = 0; i < names.Count; i++)
                if (names[i].Connection == connection && value.SequenceEqual(names[i].Database))
                { _database = text ?? value.ToString(); _collection = ""; return SyntaxTokenType.Database; }
        if (previous == "(" && before is "getCollection" or "GetCollection" || previous == "." && (before == "db" || before == database))
            for (var i = 0; i < names.Count; i++)
                if (names[i].Connection == connection && names[i].Database == database && value.SequenceEqual(names[i].Collection))
                { _collection = text ?? value.ToString(); return SyntaxTokenType.Collection; }
        if (previous == "(" && before is "dropIndex" or "hint" || previous == ":" && before == "name")
            for (var i = 0; i < names.Count; i++)
                if (names[i].Connection == connection && names[i].Database == database && names[i].Collection == collection && value.SequenceEqual(names[i].Index))
                    return SyntaxTokenType.Index;
        return fallback;
    }
}
