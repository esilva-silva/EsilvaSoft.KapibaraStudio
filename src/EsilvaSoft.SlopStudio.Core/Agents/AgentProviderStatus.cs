namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Dynamic, side-effect free availability of a provider: no network call, no data sent and no authentication started.
/// <see cref="UnavailableCode"/> is a short ASCII code, never provider or vault text.
/// </summary>
public sealed record AgentProviderStatus
{
    public AgentProviderStatus(
        bool isAvailable,
        AgentProviderAuthState authState,
        AgentProviderCapabilities capabilities,
        IReadOnlyList<string>? models = null,
        string? defaultModel = null,
        string? unavailableCode = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(authState))
        {
            throw new ArgumentOutOfRangeException(nameof(authState));
        }

        IsAvailable = isAvailable;
        AuthState = authState;
        Capabilities = capabilities.Normalize();
        Models = models is null ? [] : [.. models.Where(static model => IsSafeText(model, 128))];
        DefaultModel = IsSafeText(defaultModel, 128) ? defaultModel : null;
        UnavailableCode = isAvailable ? null : IsSafeCode(unavailableCode) ? unavailableCode : "ProviderUnavailable";
    }

    public bool IsAvailable { get; }

    public AgentProviderAuthState AuthState { get; }

    /// <summary>Effective capabilities in this state (e.g. tool calling only while tools are released).</summary>
    public AgentProviderCapabilities Capabilities { get; }

    public IReadOnlyList<string> Models { get; }

    public string? DefaultModel { get; }

    public string? UnavailableCode { get; }

    /// <summary>Status of a provider that does not report one. Fails closed: unavailable, nothing declared.</summary>
    public static AgentProviderStatus NotReported { get; } =
        new(false, AgentProviderAuthState.Unknown, AgentProviderCapabilities.None, unavailableCode: "StatusNotReported");

    public static bool IsSafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit);

    private static bool IsSafeText(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
}
