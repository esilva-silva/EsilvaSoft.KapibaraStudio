using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

/// <summary>
/// State at a line boundary: the shared lexer state plus highlighting-only classification context (bracket frames,
/// pending $search/aggregate, previous tokens and the namespace being resolved). Value equality drives line reuse.
/// </summary>
internal sealed record HighlightState(MongoLexerState Lexer = default, string Frames = "", bool SearchPending = false,
    bool AggregatePending = false, string Previous = "", string BeforePrevious = "", string Connection = "", string Database = "", string Collection = "");
