namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Non-secret health state for the configured operating-system secret store.</summary>
public enum SecretStoreAvailability
{
    /// <summary>The native API answered a nonce lookup; this does not prove write permission or unlock state.</summary>
    Available,
    /// <summary>The platform is supported, but Credential Manager reported no credential set for this logon.</summary>
    Unavailable,
    /// <summary>The current operating system has no adapter for this store.</summary>
    UnsupportedPlatform
}
