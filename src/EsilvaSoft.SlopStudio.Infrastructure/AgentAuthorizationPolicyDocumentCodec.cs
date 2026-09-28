using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Closed version-one schema. No tolerant/defaulting deserialization at the authorization boundary.</summary>
internal static class AgentAuthorizationPolicyDocumentCodec
{
    internal const int MaximumGrants = 1024;

    internal static BsonDocument Encode(AgentAuthorizationPolicySnapshot policy) => new()
    {
        ["_id"] = policy.PrincipalId,
        ["schemaVersion"] = policy.SchemaVersion,
        ["revision"] = policy.Revision,
        ["updatedAtUtc"] = DateTime.UtcNow,
        ["grants"] = new BsonArray(policy.Grants.Select(EncodeGrant))
    };

    internal static AgentAuthorizationPolicySnapshot Decode(BsonDocument document, Guid principalId)
    {
        try
        {
            RequireFields(document, "_id", "schemaVersion", "revision", "updatedAtUtc", "grants");
            if (ReadGuid(document, "_id") != principalId ||
                !document["schemaVersion"].IsInt32 ||
                document["schemaVersion"].AsInt32 != AgentAuthorizationPolicySnapshot.CurrentSchemaVersion ||
                !document["revision"].IsInt64 || document["revision"].AsInt64 < 1 ||
                !document["updatedAtUtc"].IsDateTime ||
                !document["grants"].IsArray || document["grants"].AsArray.Count > MaximumGrants)
                throw InvalidPolicy();
            var grants = document["grants"].AsArray.Select(value =>
                value.IsDocument ? DecodeGrant(value.AsDocument) : throw InvalidPolicy()).ToArray();
            var policy = AgentAuthorizationPolicySnapshot.Load(
                principalId, document["schemaVersion"].AsInt32, document["revision"].AsInt64, grants);
            if (!policy.IsValid) throw InvalidPolicy();
            return policy;
        }
        catch (ArgumentException)
        {
            // Do not include persisted content in diagnostics.
            throw InvalidPolicy();
        }
    }

    private static BsonDocument EncodeGrant(AgentPermissionGrant grant) => new()
    {
        ["principalId"] = grant.PrincipalId,
        ["invocationKind"] = (int)grant.InvocationScope.Kind,
        ["sessionId"] = grant.InvocationScope.SessionId,
        ["turnId"] = grant.InvocationScope.TurnId is { } turnId ? new BsonValue(turnId) : BsonValue.Null,
        ["sourceGenerationId"] = grant.SourceGenerationId,
        ["permission"] = (int)grant.Permission,
        ["connectionId"] = grant.Scope.ConnectionId,
        ["databaseName"] = grant.Scope.DatabaseName,
        ["collectionName"] = grant.Scope.CollectionName,
        ["destinationKind"] = (int)grant.Destination.Kind,
        ["providerId"] = grant.Destination.ProviderId,
        ["outputDataScope"] = (int)grant.OutputDataScope
    };

    private static AgentPermissionGrant DecodeGrant(BsonDocument document)
    {
        RequireFields(document, "principalId", "invocationKind", "sessionId", "turnId", "sourceGenerationId",
            "permission", "connectionId", "databaseName", "collectionName", "destinationKind", "providerId", "outputDataScope");
        var sessionId = ReadGuid(document, "sessionId");
        var invocationKind = ReadEnum<AgentInvocationScopeKind>(document, "invocationKind");
        var invocation = invocationKind switch
        {
            AgentInvocationScopeKind.Session when document["turnId"].IsNull => AgentInvocationScope.ForSession(sessionId),
            AgentInvocationScopeKind.Turn => AgentInvocationScope.ForTurn(sessionId, ReadGuid(document, "turnId")),
            _ => throw InvalidPolicy()
        };
        var connectionId = ReadGuid(document, "connectionId");
        var databaseName = ReadNullableString(document, "databaseName");
        var collectionName = ReadNullableString(document, "collectionName");
        var scope = (databaseName, collectionName) switch
        {
            (null, null) => AgentNamespaceScope.ForConnection(connectionId),
            (not null, null) => AgentNamespaceScope.ForDatabase(connectionId, databaseName),
            (not null, not null) => AgentNamespaceScope.ForCollection(connectionId, databaseName, collectionName),
            _ => throw InvalidPolicy()
        };
        var destinationKind = ReadEnum<AgentOutputDestinationKind>(document, "destinationKind");
        var providerId = ReadNullableString(document, "providerId");
        var destination = destinationKind switch
        {
            AgentOutputDestinationKind.Local when providerId is null => AgentOutputDestination.Local(),
            AgentOutputDestinationKind.ProviderExternal when providerId is not null => AgentOutputDestination.ProviderExternal(providerId),
            AgentOutputDestinationKind.McpExternal when providerId is not null => AgentOutputDestination.McpExternal(providerId),
            _ => throw InvalidPolicy()
        };
        return new AgentPermissionGrant(ReadGuid(document, "principalId"), invocation,
            ReadGuid(document, "sourceGenerationId"), ReadEnum<AgentPermission>(document, "permission"), scope,
            destination, ReadEnum<AgentOutputDataScope>(document, "outputDataScope"));
    }

    private static void RequireFields(BsonDocument document, params string[] names)
    {
        if (document.Count != names.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
            throw InvalidPolicy();
    }

    private static Guid ReadGuid(BsonDocument document, string field) =>
        document[field].IsGuid && document[field].AsGuid != Guid.Empty ? document[field].AsGuid : throw InvalidPolicy();

    private static string? ReadNullableString(BsonDocument document, string field) =>
        document[field].IsNull ? null : document[field].IsString ? document[field].AsString : throw InvalidPolicy();

    private static T ReadEnum<T>(BsonDocument document, string field) where T : struct, Enum
    {
        if (!document[field].IsInt32) throw InvalidPolicy();
        var value = (T)Enum.ToObject(typeof(T), document[field].AsInt32);
        return Enum.IsDefined(value) ? value : throw InvalidPolicy();
    }

    private static InvalidDataException InvalidPolicy() => new("Política de autorização ilegível ou com versão não suportada. O documento foi preservado.");
}
