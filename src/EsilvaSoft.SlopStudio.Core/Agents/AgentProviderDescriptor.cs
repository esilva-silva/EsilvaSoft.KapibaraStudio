namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Static, side-effect free description of a registered provider. It contains no secret, account or model output.
/// It deliberately has no destination field: where data goes is derived from the provider's <c>IsLocal</c>
/// declaration, the same source the runtime uses for the tool output destination, so the two cannot diverge.
/// </summary>
public sealed record AgentProviderDescriptor
{
    public const int MaximumDisplayNameLength = 80;

    public AgentProviderDescriptor(
        string providerId,
        string displayName,
        IReadOnlyList<AgentAuthenticationMethod> authenticationMethods,
        AgentProviderCapabilities capabilities)
    {
        if (!IsValidProviderId(providerId))
        {
            throw new ArgumentException("O identificador do provider é inválido.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength ||
            displayName.Any(char.IsControl))
        {
            throw new ArgumentException("O nome de exibição do provider é inválido.", nameof(displayName));
        }

        ArgumentNullException.ThrowIfNull(authenticationMethods);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (authenticationMethods.Any(static method => !Enum.IsDefined(method)) ||
            authenticationMethods.Distinct().Count() != authenticationMethods.Count)
        {
            throw new ArgumentException("Os métodos de autenticação são inválidos.", nameof(authenticationMethods));
        }

        ProviderId = providerId;
        DisplayName = displayName;
        AuthenticationMethods = [.. authenticationMethods];
        Capabilities = capabilities.Normalize();
    }

    public string ProviderId { get; }

    /// <summary>Human label (may be a brand). The UI shows it; it never branches on it.</summary>
    public string DisplayName { get; }

    public IReadOnlyList<AgentAuthenticationMethod> AuthenticationMethods { get; }

    /// <summary>Normalized declared capabilities (see <see cref="AgentProviderCapabilities.Normalize"/>).</summary>
    public AgentProviderCapabilities Capabilities { get; }

    public bool RequiresApiKey => AuthenticationMethods.Contains(AgentAuthenticationMethod.ApiKey);

    /// <summary>Descriptor of a provider that does not describe itself: its ID as label and no capability.</summary>
    public static AgentProviderDescriptor Minimal(string providerId) =>
        new(providerId, providerId, [], AgentProviderCapabilities.None);

    /// <summary>Same rules the runtime applies to provider IDs.</summary>
    public static bool IsValidProviderId(string? providerId) =>
        !string.IsNullOrWhiteSpace(providerId) && providerId.Length <= 64 && !providerId.Any(char.IsControl);
}
