using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Closed schema, no tolerant/defaulting deserialization at the consent boundary. Version 2 is written; version 1 is
/// still read (additive migration) into legacy grants that never authorize, and is never rewritten as version 2.
/// </summary>
internal static class AgentSchemaSamplingConsentDocumentCodec
{
    internal const int MaximumConsents = 1024;

    private static readonly string[] DocumentFields = ["_id", "schemaVersion", "revision", "updatedAtUtc", "consents"];

    private static readonly string[] LegacyConsentFields =
    [
        "principalId", "sourceGenerationId", "connectionId", "databaseName", "collectionName", "grantedAtUtc",
        "expiresAtUtc"
    ];

    private static readonly string[] CurrentConsentFields =
    [
        .. LegacyConsentFields, "destinationKind", "providerId", "maximumSampleSize", "policyRevision"
    ];

    internal static BsonDocument Encode(AgentSchemaSamplingConsentSnapshot consent)
    {
        if (!consent.IsValid) throw new ArgumentException("Snapshot de consentimento inválido.", nameof(consent));
        return new()
        {
            ["_id"] = consent.PrincipalId,
            ["schemaVersion"] = consent.SchemaVersion,
            ["revision"] = consent.Revision,
            ["updatedAtUtc"] = DateTime.UtcNow,
            ["consents"] = new BsonArray(consent.Consents.Select(EncodeConsent))
        };
    }

    internal static AgentSchemaSamplingConsentSnapshot Decode(BsonDocument document, Guid principalId)
    {
        try
        {
            RequireFields(document, DocumentFields);
            if (ReadGuid(document, "_id") != principalId ||
                !document["schemaVersion"].IsInt32 ||
                document["schemaVersion"].AsInt32 is not (AgentSchemaSamplingConsentSnapshot.LegacySchemaVersion or
                    AgentSchemaSamplingConsentSnapshot.CurrentSchemaVersion) ||
                !document["revision"].IsInt64 || document["revision"].AsInt64 < 1 ||
                !document["updatedAtUtc"].IsDateTime ||
                !document["consents"].IsArray || document["consents"].AsArray.Count > MaximumConsents)
                throw InvalidConsent();
            var schemaVersion = document["schemaVersion"].AsInt32;
            var consents = document["consents"].AsArray.Select(value =>
                value.IsDocument ? DecodeConsent(value.AsDocument, principalId, schemaVersion) : throw InvalidConsent()).ToArray();
            var snapshot = AgentSchemaSamplingConsentSnapshot.Load(
                principalId, schemaVersion, document["revision"].AsInt64, consents);
            if (!snapshot.IsValid) throw InvalidConsent();
            return snapshot;
        }
        catch (ArgumentException)
        {
            // Do not include persisted content in diagnostics.
            throw InvalidConsent();
        }
    }

    private static BsonDocument EncodeConsent(AgentSchemaSamplingConsentGrant consent)
    {
        var document = new BsonDocument
        {
            ["principalId"] = consent.PrincipalId,
            ["sourceGenerationId"] = consent.SourceGenerationId,
            ["connectionId"] = consent.Scope.ConnectionId,
            ["databaseName"] = consent.Scope.DatabaseName is { } database ? new BsonValue(database) : BsonValue.Null,
            ["collectionName"] = consent.Scope.CollectionName is { } collection ? new BsonValue(collection) : BsonValue.Null,
            ["grantedAtUtc"] = consent.GrantedAtUtc.UtcDateTime,
            ["expiresAtUtc"] = consent.ExpiresAtUtc is { } expires ? new BsonValue(expires.UtcDateTime) : BsonValue.Null
        };
        if (consent.Destination is { } destination)
        {
            document["destinationKind"] = (int)destination.Kind;
            document["providerId"] = destination.ProviderId is { } providerId ? new BsonValue(providerId) : BsonValue.Null;
            document["maximumSampleSize"] = consent.MaximumSampleSize;
            document["policyRevision"] = consent.PolicyRevision;
        }
        return document;
    }

    private static AgentSchemaSamplingConsentGrant DecodeConsent(BsonDocument document, Guid principalId, int schemaVersion)
    {
        var legacy = schemaVersion == AgentSchemaSamplingConsentSnapshot.LegacySchemaVersion;
        RequireFields(document, legacy ? LegacyConsentFields : CurrentConsentFields);
        if (ReadGuid(document, "principalId") != principalId) throw InvalidConsent();
        var connectionId = ReadGuid(document, "connectionId");
        var databaseName = ReadNullableString(document, "databaseName");
        var collectionName = ReadNullableString(document, "collectionName");
        var scope = (databaseName, collectionName) switch
        {
            (null, null) => AgentNamespaceScope.ForConnection(connectionId),
            (not null, null) => AgentNamespaceScope.ForDatabase(connectionId, databaseName),
            (not null, not null) => AgentNamespaceScope.ForCollection(connectionId, databaseName, collectionName),
            _ => throw InvalidConsent()
        };
        var generationId = ReadGuid(document, "sourceGenerationId");
        var grantedAt = ReadDateTime(document, "grantedAtUtc");
        var expiresAt = ReadNullableDateTime(document, "expiresAtUtc");
        if (legacy)
            return AgentSchemaSamplingConsentGrant.Legacy(principalId, generationId, scope, grantedAt, expiresAt);

        if (!document["destinationKind"].IsInt32 || !document["maximumSampleSize"].IsInt32 ||
            !document["policyRevision"].IsInt64)
            throw InvalidConsent();
        var destinationKind = (AgentOutputDestinationKind)document["destinationKind"].AsInt32;
        var providerId = ReadNullableString(document, "providerId");
        var destination = destinationKind switch
        {
            AgentOutputDestinationKind.Local when providerId is null => AgentOutputDestination.Local(),
            AgentOutputDestinationKind.ProviderExternal when providerId is not null =>
                AgentOutputDestination.ProviderExternal(providerId),
            AgentOutputDestinationKind.McpExternal when providerId is not null => AgentOutputDestination.McpExternal(providerId),
            _ => throw InvalidConsent()
        };
        return new AgentSchemaSamplingConsentGrant(principalId, generationId, scope, destination,
            document["maximumSampleSize"].AsInt32, document["policyRevision"].AsInt64, grantedAt, expiresAt);
    }

    private static void RequireFields(BsonDocument document, string[] names)
    {
        if (document.Count != names.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
            throw InvalidConsent();
    }

    private static Guid ReadGuid(BsonDocument document, string field) =>
        document[field].IsGuid && document[field].AsGuid != Guid.Empty ? document[field].AsGuid : throw InvalidConsent();

    private static string? ReadNullableString(BsonDocument document, string field) =>
        document[field].IsNull ? null : document[field].IsString ? document[field].AsString : throw InvalidConsent();

    private static DateTimeOffset ReadDateTime(BsonDocument document, string field) =>
        document[field].IsDateTime ? LiteDbDates.ToUtcInstant(document[field].AsDateTime) : throw InvalidConsent();

    private static DateTimeOffset? ReadNullableDateTime(BsonDocument document, string field) =>
        document[field].IsNull ? null : ReadDateTime(document, field);

    private static InvalidDataException InvalidConsent() =>
        new("Consentimento de amostragem de schema ilegível ou com versão não suportada. O documento foi preservado.");
}
