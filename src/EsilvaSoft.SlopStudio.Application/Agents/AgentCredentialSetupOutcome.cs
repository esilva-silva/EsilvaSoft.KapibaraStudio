namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Result of an explicit API-key write or removal. It never carries key material or vault text.</summary>
public enum AgentCredentialSetupOutcome
{
    Saved,
    Removed,
    VaultUnavailable,

    /// <summary>The user dismissed a vault unlock prompt.</summary>
    Cancelled,

    Failed,

    /// <summary>The value is empty, too long or contains whitespace/control characters; nothing was written.</summary>
    Rejected,

    /// <summary>The provider has no API-key slot in this composition; nothing was written.</summary>
    UnknownProvider,
}
