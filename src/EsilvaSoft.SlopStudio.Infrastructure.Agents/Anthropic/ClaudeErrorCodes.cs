using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>
/// Códigos seguros emitidos em <c>AgentError</c> pelo adapter Claude. São fixos, sem texto do provider, prompt,
/// chave ou identificadores de requisição.
/// </summary>
public static class ClaudeErrorCodes
{
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string CredentialUnavailable = "CredentialUnavailable";
    public const string ProviderPermissionDenied = "ProviderPermissionDenied";
    public const string ModelUnavailable = "ModelUnavailable";
    public const string RateLimited = "RateLimited";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string ProviderUnreachable = "ProviderUnreachable";
    public const string ProviderRequestRejected = "ProviderRequestRejected";
    public const string ProviderStreamError = "ProviderStreamError";
    public const string ProviderStreamIncomplete = "ProviderStreamIncomplete";
    public const string ProviderProtocolViolation = "ProviderProtocolViolation";
    public const string ProviderTimeout = "ProviderTimeout";
    public const string ProviderRefused = "ProviderRefused";
    public const string ProviderFailure = "ProviderFailure";
    public const string TurnTimeout = "TurnTimeout";
    public const string ToolResultTimeout = "ToolResultTimeout";
    public const string OutputTokenLimit = "OutputTokenLimit";
    public const string UnsupportedStopReason = "UnsupportedStopReason";
    public const string EmptyMessage = "EmptyMessage";
    public const string InputTooLarge = "InputTooLarge";
    public const string ContextBudgetExceeded = "ContextBudgetExceeded";
    public const string TokenBudgetExceeded = "TokenBudgetExceeded";
    public const string SessionBudgetExceeded = "SessionBudgetExceeded";
    public const string RequestBudgetExceeded = "RequestBudgetExceeded";
    public const string OutputBudgetExceeded = "OutputBudgetExceeded";
    public const string ToolCallBudgetExceeded = "ToolCallBudgetExceeded";
    public const string ToolArgumentsTooLarge = "ToolArgumentsTooLarge";
    public const string InvalidToolArguments = "InvalidToolArguments";
}

internal sealed record ToolErrorPayload(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error")] string Error);

[JsonSerializable(typeof(ToolErrorPayload))]
internal sealed partial class ClaudeJsonContext : JsonSerializerContext;
