namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

public sealed record SyntaxSnapshot(string Text, SyntaxLanguage Language, SyntaxContext Context,
    IReadOnlyList<SyntaxToken> Tokens, IReadOnlyDictionary<int, int> Brackets, int TokenizedLines)
{
    internal IReadOnlyList<SyntaxLine> Lines { get; init; } = [];
    public IReadOnlyList<int> LineStarts { get; init; } = [];
}
