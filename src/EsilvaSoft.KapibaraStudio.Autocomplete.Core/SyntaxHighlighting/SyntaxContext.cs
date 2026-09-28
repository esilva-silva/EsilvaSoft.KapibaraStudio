namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

public sealed record SyntaxContext(IReadOnlyList<SyntaxNamespace> Names, string Connection = "", string Database = "", string Collection = "")
{
    public static SyntaxContext Empty { get; } = new([]);
}
