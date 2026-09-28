using System.Collections.ObjectModel;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Validated immutable schema sampling consents for one principal at one monotonically versioned revision.</summary>
/// <remarks>
/// Schema version 2 binds each consent to destination, maximum sample size and policy revision. Version 1 documents
/// stay readable (so a UI can list and revoke them) but contain only <see cref="AgentSchemaSamplingConsentGrant.IsLegacy"/>
/// grants, which never authorize sampling. New writes are always version 2; a legacy grant cannot be written back as
/// version 2, so migrating means granting again with every binding explicit.
/// </remarks>
public sealed class AgentSchemaSamplingConsentSnapshot
{
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    private AgentSchemaSamplingConsentSnapshot(Guid principalId, int schemaVersion, long revision, bool isValid, IReadOnlyList<AgentSchemaSamplingConsentGrant> consents)
    {
        PrincipalId = principalId;
        SchemaVersion = schemaVersion;
        Revision = revision;
        IsValid = isValid;
        Consents = consents;
    }

    public Guid PrincipalId { get; }
    public int SchemaVersion { get; }
    public long Revision { get; }
    public bool IsValid { get; }
    public IReadOnlyList<AgentSchemaSamplingConsentGrant> Consents { get; }

    internal static AgentSchemaSamplingConsentSnapshot Load(Guid principalId, int schemaVersion, long revision, IEnumerable<AgentSchemaSamplingConsentGrant?>? consents)
    {
        if (principalId == Guid.Empty || consents is null ||
            schemaVersion is not (LegacySchemaVersion or CurrentSchemaVersion) || revision < 1)
            return Invalid(principalId, schemaVersion, revision);

        var items = consents.ToArray();
        if (items.Any(static consent => consent is null)) return Invalid(principalId, schemaVersion, revision);
        var concrete = items.Cast<AgentSchemaSamplingConsentGrant>().ToArray();
        if (concrete.Any(consent => consent.PrincipalId != principalId))
            return Invalid(principalId, schemaVersion, revision);
        // A snapshot never mixes versions: v1 holds only legacy grants, v2 only fully bound grants.
        if (concrete.Any(consent => consent.IsLegacy != (schemaVersion == LegacySchemaVersion)))
            return Invalid(principalId, schemaVersion, revision);

        var duplicate = concrete.GroupBy(static consent => (consent.Scope, consent.SourceGenerationId, consent.Destination))
            .Any(static group => group.Count() > 1);
        return duplicate
            ? Invalid(principalId, schemaVersion, revision)
            : new AgentSchemaSamplingConsentSnapshot(principalId, schemaVersion, revision, true, new ReadOnlyCollection<AgentSchemaSamplingConsentGrant>(concrete));
    }

    private static AgentSchemaSamplingConsentSnapshot Invalid(Guid principalId, int schemaVersion, long revision) =>
        new(principalId, schemaVersion, revision, false, Array.Empty<AgentSchemaSamplingConsentGrant>());
}
