namespace EsilvaSoft.KapibaraStudio.Application;

public sealed record AutocompleteContextSnapshot(string Text, int Caret, string Language,
    string Input = "", IReadOnlyList<string>? ResultFields = null, IReadOnlyList<string>? KnownNames = null,
    IReadOnlyList<string>? RecentCommands = null, string? FileName = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string PrefixCacheSessionKey { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationToken PrefixCacheSessionLifetime { get; init; }

    /// <summary>
    /// Coleção alvo do statement no cursor, quando resolvida. Só o contrato <c>editor-context-v2</c> a serializa
    /// (rótulo da linha <c>TARGET FIELDS</c>); o v1 a ignora byte a byte.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetCollection { get; init; }

    /// <summary>
    /// Campos e tipos da coleção alvo, na ordem do schema inferido (metadado, nunca valores). Só o contrato
    /// <c>editor-context-v2</c> os serializa.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AutocompleteTargetField>? TargetFields { get; init; }
}
