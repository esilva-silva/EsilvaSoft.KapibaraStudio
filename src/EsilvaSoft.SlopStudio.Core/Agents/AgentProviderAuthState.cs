namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>Credential state of a provider. It never carries key material or the provider's error text.</summary>
public enum AgentProviderAuthState
{
    NotRequired,
    NotConfigured,

    /// <summary>A credential exists in the vault; it is only proven valid by a real request.</summary>
    Configured,

    /// <summary>The service rejected the credential in this execution.</summary>
    Invalid,

    Expired,
    VaultUnavailable,

    /// <summary>The provider did not report its credential state (status not implemented or failed).</summary>
    Unknown,
}
