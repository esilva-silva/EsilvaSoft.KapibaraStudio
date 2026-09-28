namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "String is the semantic name of the language token, not a variable type prefix.")]
public enum SyntaxTokenType
{
    Default, PropertyName, String, Number, Boolean, Null, Identifier, Keyword, Function, Method,
    Operator, Punctuation, Comment, Regex, MongoOperator, MongoStage, MongoFunction, MongoType,
    Connection, Database, Collection, Index, Error, Warning, Success, GhostText, MatchingBracket,
    UnmatchedBracket, SearchMatch, AtlasSearchOperator
}
