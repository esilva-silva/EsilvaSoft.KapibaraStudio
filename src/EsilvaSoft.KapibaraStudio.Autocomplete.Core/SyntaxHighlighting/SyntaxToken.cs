namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

public readonly record struct SyntaxToken(int Start, int Length, SyntaxTokenType Type);
