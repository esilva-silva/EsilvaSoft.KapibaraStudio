namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Classification of legacy profile content that may need a credential migration.</summary>
public enum LegacyCredentialCategory
{
    InlineUriPassword = 1,
    DynamicUriReference = 2,
    UnrecognizedUriUserInfo = 3
}

/// <summary>Only the persisted profile identity and a fixed classification leave the repository.</summary>
public sealed record LegacyCredentialFinding(Guid ProfileId, LegacyCredentialCategory Category);

/// <summary>No URI, profile name, environment key, or environment value is included.</summary>
public sealed record LegacyCredentialInventory(
    IReadOnlyList<LegacyCredentialFinding> ProfileFindings,
    int EnvironmentValueCount)
{
    /// <summary>Number of findings, not distinct profiles; one profile may have multiple categories.</summary>
    public int ProfileFindingCount => ProfileFindings.Count;
}
