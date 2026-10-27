namespace EsilvaSoft.KapibaraStudio.Application;

public sealed record AutocompleteContextSnapshot(string Text, int Caret, string Language,
    string Input = "", IReadOnlyList<string>? ResultFields = null, IReadOnlyList<string>? KnownNames = null,
    IReadOnlyList<string>? RecentCommands = null, string? FileName = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string PrefixCacheSessionKey { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationToken PrefixCacheSessionLifetime { get; init; }
}
