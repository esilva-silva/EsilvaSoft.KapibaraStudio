using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

/// <summary>
/// Maps only documented SDK 1.0.14 error categories to product-owned codes. Native messages, stacks,
/// URLs, upstream codes and account/request identifiers never cross this boundary.
/// </summary>
internal static class CopilotSessionErrorClassifier
{
    public static string FromSessionError(SessionErrorData? error) => error?.ErrorType switch
    {
        "authentication" => "CopilotAuthenticationFailed",
        "authorization" => "CopilotAccessDenied",
        "quota" => "CopilotQuotaExceeded",
        "rate_limit" => "CopilotRateLimited",
        "context_limit" => "CopilotContextLimitExceeded",
        "query" => "CopilotQueryFailed",
        _ => "CopilotProviderFailure",
    };

    // A timeout describes the wait, not whether the remote operation executed. Classification must never
    // start a retry or reconstruct/replay a prompt/tool; the existing delivery report retains that uncertainty.
    public static string FromException(Exception error, string operationCode) =>
        error is TimeoutException ? "CopilotRequestTimedOut" : operationCode;
}
