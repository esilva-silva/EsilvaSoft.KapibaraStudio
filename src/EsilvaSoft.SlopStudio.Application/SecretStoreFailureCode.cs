namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Safe, non-sensitive failure categories returned by a secret store.</summary>
public enum SecretStoreFailureCode
{
    Unavailable,
    Locked,
    Denied,
    Cancelled,
    NotFound,
    Corrupt
}
