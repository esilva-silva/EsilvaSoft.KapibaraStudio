namespace EsilvaSoft.KapibaraStudio.Core.Agents;

public enum AgentUsageScope { CallTotal, TurnTotal }

/// <summary>Official usage observation; missing values are not zero. Cache is shown separately and never added
/// to input. This is consumption, not context-window occupancy or an account quota. Memory only.</summary>
public sealed record AgentUsageMetrics(
    string ObservationId, AgentUsageScope Scope, long Revision, string Source,
    long? InputTokens = null, long? OutputTokens = null,
    long? CacheReadTokens = null, long? CacheWriteTokens = null,
    decimal? Cost = null, string? Currency = null, string? Model = null, bool IsPartial = false)
{
    public bool IsWellFormed => Enum.IsDefined(Scope) && Revision >= 0 &&
        Safe(ObservationId, 160) && Safe(Source, 120) && (Model is null || Safe(Model, 160)) &&
        InputTokens is null or >= 0 && OutputTokens is null or >= 0 &&
        CacheReadTokens is null or >= 0 && CacheWriteTokens is null or >= 0 &&
        Cost is null or >= 0 && (Cost is null ? Currency is null : Currency is { Length: 3 } && Currency.All(char.IsAsciiLetterUpper));

    private static bool Safe(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/');
}
