namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

/// <summary>Central internal budgets; no text is truncated or changed by highlighting.</summary>
public static class SyntaxHighlightingOptions
{
    public const int DebounceMilliseconds = 80;
    public const int FullHighlightCharacters = 64 * 1024;
    public const int MaximumStyledTokens = 12000;
    public const int MaximumNesting = 512;
    public const int MaximumCachedLines = 100000;
    public const int LongLineThreshold = 64 * 1024;
    public const int LongLineWindowCharacters = 4096;
}
