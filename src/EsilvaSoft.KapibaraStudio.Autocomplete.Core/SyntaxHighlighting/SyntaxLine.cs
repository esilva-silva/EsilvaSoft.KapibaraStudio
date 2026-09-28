namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

internal sealed record SyntaxLine(string Text, HighlightState Before, HighlightState After, SyntaxToken[] Tokens);
