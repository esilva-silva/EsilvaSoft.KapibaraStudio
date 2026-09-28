using System.ClientModel;
using EsilvaSoft.SlopStudio.Application;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

/// <summary>
/// Safe ASCII codes emitted in <c>AgentError</c> events. Native messages, response bodies, headers and exception text
/// never leave the adapter, so an echoed key, prompt or data fragment cannot reach events or diagnostics.
/// </summary>
internal static class OpenAiFailureCodes
{
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string ProviderAccessDenied = "ProviderAccessDenied";
    public const string ModelUnavailable = "ModelUnavailable";
    public const string ProviderRequestRejected = "ProviderRequestRejected";
    public const string RateLimited = "RateLimited";
    public const string ProviderTimeout = "ProviderTimeout";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string NetworkUnavailable = "NetworkUnavailable";
    public const string ProviderFailure = "ProviderFailure";
    public const string ProviderProtocolViolation = "ProviderProtocolViolation";
    public const string IncompleteResponse = "IncompleteResponse";
    public const string ResponseTruncated = "ResponseTruncated";
    public const string ContentFiltered = "ContentFiltered";
    public const string InputTooLarge = "InputTooLarge";
    public const string RequestBudgetExceeded = "RequestBudgetExceeded";
    public const string ResponseBudgetExceeded = "ResponseBudgetExceeded";
    public const string TokenBudgetExceeded = "TokenBudgetExceeded";
    public const string ToolRoundLimitExceeded = "ToolRoundLimitExceeded";
    public const string ToolCallLimitExceeded = "ToolCallLimitExceeded";
    public const string ToolResultTimeout = "ToolResultTimeout";

    /// <summary>
    /// O histórico retido excede o limite configurado. Mesmo código de <c>ClaudeErrorCodes.ContextBudgetExceeded</c>:
    /// nenhum adapter reduz contexto silenciosamente, ambos recusam o turno e pedem outra sessão.
    /// </summary>
    public const string ContextBudgetExceeded = "ContextBudgetExceeded";

    public static string FromStatus(int status) => status switch
    {
        401 => AuthenticationFailed,
        403 => ProviderAccessDenied,
        404 => ModelUnavailable,
        408 => ProviderTimeout,
        413 => RequestBudgetExceeded,
        429 => RateLimited,
        0 => NetworkUnavailable,
        >= 500 => ProviderUnavailable,
        >= 400 => ProviderRequestRejected,
        _ => ProviderFailure,
    };

    public static string FromException(Exception exception) => exception switch
    {
        ClientResultException result => FromStatus(result.Status),
        HttpRequestException => NetworkUnavailable,
        TimeoutException => ProviderTimeout,
        System.Text.Json.JsonException or InvalidDataException or FormatException => ProviderProtocolViolation,
        _ => ProviderFailure,
    };

    public static OpenAiAgentUnavailableReason FromVault(SecretStoreFailureCode code) => code switch
    {
        SecretStoreFailureCode.NotFound => OpenAiAgentUnavailableReason.CredentialNotConfigured,
        SecretStoreFailureCode.Locked => OpenAiAgentUnavailableReason.VaultLocked,
        SecretStoreFailureCode.Denied => OpenAiAgentUnavailableReason.VaultAccessDenied,
        SecretStoreFailureCode.Cancelled => OpenAiAgentUnavailableReason.VaultPromptCancelled,
        SecretStoreFailureCode.Corrupt => OpenAiAgentUnavailableReason.CredentialCorrupt,
        _ => OpenAiAgentUnavailableReason.VaultUnavailable,
    };
}
