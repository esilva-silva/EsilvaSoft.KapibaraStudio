#if DEBUG
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Diagnósticos locais da integração, somente em Debug. Recebe apenas categorias e nomes de ferramentas; nunca recebe
/// frames JSON, prompts, respostas, argumentos, caminhos de workspace, IDs de sessão ou credenciais.
/// </summary>
internal static class ClaudeCodeDebugLog
{
    private const int MaximumToolNames = 32;

    public static void InitMismatch(
        IClaudeCodeSystem system, string? directory, IReadOnlyCollection<string> reasons, IReadOnlyCollection<string> missingTools,
        IReadOnlyCollection<string> unexpectedTools, int observedToolCount, bool expectsMcp,
        int observedMcpServerCount, string? observedMcpStatus, string? permissionMode,
        string? apiKeySource, string? model, string? version)
    {
        if (directory is null) return;
        Write(system, directory, new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventType = "ClaudeCodeInitMismatch",
            reasons = reasons.Take(16).Select(SafeToken).ToArray(),
            missingTools = missingTools.Take(MaximumToolNames).Select(SafeToken).ToArray(),
            unexpectedTools = unexpectedTools.Take(MaximumToolNames).Select(SafeToken).ToArray(),
            missingToolCount = missingTools.Count,
            unexpectedToolCount = unexpectedTools.Count,
            observedToolCount,
            expectsMcp,
            observedMcpServerCount,
            observedMcpStatus = SafeToken(observedMcpStatus),
            permissionMode = SafeToken(permissionMode),
            apiKeySource = SafeToken(apiKeySource),
            model = SafeToken(model),
            version = SafeToken(version),
        });
    }

    public static void TurnFailure(IClaudeCodeSystem system, string? directory, string errorCode, string stage, bool resultReceived, int discardedLines,
        int? exitCode)
    {
        if (directory is null) return;
        Write(system, directory, new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventType = "ClaudeCodeTurnFailure",
            errorCode = KnownErrorCode(errorCode),
            stage = KnownStage(stage),
            resultReceived,
            discardedLines,
            exitCode,
        });
    }

    private static string KnownErrorCode(string value) =>
        ClaudeCodeErrorCodes.TurnErrorCodes.Contains(value, StringComparer.Ordinal) ? value : "Unknown";

    private static string KnownStage(string value) => value switch
    {
        "Validation" or "AuthenticationCheck" or "McpProvisioning" or "Process" or "ResumeFallback" => value,
        _ => "Unknown",
    };

    private static string SafeToken(string? value)
    {
        if (value is null) return "<missing>";
        if (value.Length == 0) return "<empty>";
        return value.Length <= 128 &&
            !value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("bearer", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("token", StringComparison.OrdinalIgnoreCase) &&
            value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':')
                ? value
                : "<redacted>";
    }

    private static void Write(IClaudeCodeSystem system, string directory, object entry)
    {
        try { system.AppendDebugLog(directory, JsonSerializer.Serialize(entry)); }
        catch (Exception) { /* Diagnóstico nunca altera o resultado do turno. */ }
    }
}
#endif
