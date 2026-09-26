namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Public entry point to the single best-effort secret redaction of <see cref="AgentContextProvider"/>, for layers
/// that cannot see its internal helper (the LiteDB owner redacts conversation text before writing). No logic lives
/// here: every caller shares the same patterns, markers and fail-closed timeout.
/// </summary>
public static class AgentSecretRedaction
{
    /// <summary>Returns the redacted text (null for null or empty input).</summary>
    /// <exception cref="AgentRuntimeException">Code <c>ContextRedactionFailed</c> when redaction times out.</exception>
    public static string? Redact(string? text) => AgentContextProvider.Redact(text);
}
