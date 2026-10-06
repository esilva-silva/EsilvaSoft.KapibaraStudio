using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Result of reading one stored permission document.</summary>
internal sealed record AgentProviderPermissionsDecoded(AgentStoredDocumentState State, AgentProviderPermissions? Permissions);

/// <summary>
/// Document format of the <c>agentProviderPermissions</c> facet (<c>_id = providerId</c>): top-level identity,
/// format version and revision for CAS, and the permissions as a closed JSON payload. Only the nulls tolerated by the
/// Core contract are materialized, to their restrictive meaning (see <see cref="Normalize"/>); a null section makes a
/// stored document unreadable and a value to save invalid, as does every other deviation or failed limit. A newer <c>formatVersion</c> is read-only. Neither is replaced by defaults.
/// Version 1 is read without rewriting and uses the default tool budget of 100; a successful save writes version 2,
/// whose explicit nullable budget distinguishes unlimited calls from a missing or damaged field.
/// </summary>
internal static class AgentProviderPermissionsDocumentCodec
{
    internal const int MaximumExclusions = 256;
    internal const int MaximumExclusionChars = 512;
    internal const int MaximumSelectedConnections = 1024;
    internal const int MaximumEnabledReadTools = 64;
    internal const int MaximumToolNameChars = 128;
    internal const int MaximumModelIdChars = 128;

    private static readonly string[] DocumentFields = ["_id", "formatVersion", "revision", "updatedAtUtc", "json"];

    /// <summary>Normalizes null sections and validates. Returns the copy to store, or null with a stable code.</summary>
    internal static AgentProviderPermissions? Prepare(AgentProviderPermissions? permissions, long revision, out string? errorCode)
    {
        if (permissions is null)
        {
            errorCode = "PermissionsInvalid";
            return null;
        }

        // A malformed value (null section, undefined enum...) is refused, never completed with permissive defaults.
        if (!permissions.IsWellFormed)
        {
            errorCode = "PermissionsMalformed";
            return null;
        }

        var stored = Normalize(permissions) with
        {
            Revision = revision,
            FormatVersion = AgentProviderPermissions.CurrentFormatVersion,
        };
        errorCode = Validate(stored);
        return errorCode is null ? stored : null;
    }

    internal static BsonDocument Encode(AgentProviderPermissions stored) => new()
    {
        ["_id"] = stored.ProviderId,
        ["formatVersion"] = stored.FormatVersion,
        ["revision"] = stored.Revision,
        ["updatedAtUtc"] = DateTime.UtcNow,
        ["json"] = JsonSerializer.Serialize(stored, AgentPersistenceJson.Permissions),
    };

    internal static AgentProviderPermissionsDecoded Decode(BsonDocument document)
    {
        if (document.TryGetValue("formatVersion", out var versionValue) && versionValue.IsInt32 &&
            versionValue.AsInt32 > AgentProviderPermissions.CurrentFormatVersion)
            return new(AgentStoredDocumentState.UnsupportedVersion, null);

        if (document.Count != DocumentFields.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(DocumentFields) ||
            versionValue is not { IsInt32: true } || versionValue.AsInt32 is < 1 ||
            versionValue.AsInt32 > AgentProviderPermissions.CurrentFormatVersion ||
            !document["_id"].IsString || !document["revision"].IsInt64 || document["revision"].AsInt64 < 1 ||
            !document["updatedAtUtc"].IsDateTime || !document["json"].IsString)
            return Unreadable();

        AgentProviderPermissions? permissions;
        try
        {
            using var payload = JsonDocument.Parse(document["json"].AsString);
            if (versionValue.AsInt32 >= 2 && !payload.RootElement.TryGetProperty(nameof(AgentProviderPermissions.MaximumToolCallsPerTurn), out _))
                return Unreadable();
            permissions = JsonSerializer.Deserialize<AgentProviderPermissions>(document["json"].AsString, AgentPersistenceJson.Permissions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return Unreadable();
        }

        // A stored null section can only come from a foreign or damaged writer (this codec never writes one). It is
        // reported as unreadable, kept as is and never widened to defaults; the UI shows the failure and the policy,
        // having no permissions, sends nothing.
        if (permissions is null || !permissions.IsWellFormed || permissions.FormatVersion != versionValue.AsInt32) return Unreadable();
        permissions = Normalize(permissions);
        if (Validate(permissions) is not null ||
            !string.Equals(permissions.ProviderId, document["_id"].AsString, StringComparison.Ordinal) ||
            permissions.Revision != document["revision"].AsInt64)
            return Unreadable();
        return new(AgentStoredDocumentState.Readable, permissions);
    }

    /// <summary>
    /// Only the nulls the Core contract tolerates are materialized, each to its restrictive meaning: a null
    /// <see cref="AgentProviderPermissions.EnabledReadTools"/> is no read tool (<c>[]</c>, never the defaults), a null
    /// exclusion list is <see cref="AgentWorkspacePermissions.DefaultExclusions"/> (its <c>EffectiveExclusions</c>) and a
    /// null connection list outside the Selected scope is empty. Null sections are never replaced: callers check
    /// <see cref="AgentProviderPermissions.IsWellFormed"/> first.
    /// </summary>
    private static AgentProviderPermissions Normalize(AgentProviderPermissions permissions) => permissions with
    {
        Workspace = permissions.Workspace.Exclusions is null
            ? permissions.Workspace with { Exclusions = AgentWorkspacePermissions.DefaultExclusions }
            : permissions.Workspace,
        SelectedConnectionIds = permissions.SelectedConnectionIds ?? [],
        EnabledReadTools = permissions.EnabledReadTools ?? [],
    };

    private static string? Validate(AgentProviderPermissions permissions)
    {
        if (!AgentPersistenceJson.IsValidProviderId(permissions.ProviderId)) return "ProviderIdInvalid";
        if (permissions.FormatVersion is < 1 || permissions.FormatVersion > AgentProviderPermissions.CurrentFormatVersion || permissions.Revision < 1 ||
            !Enum.IsDefined(permissions.ConnectionScope) || !Enum.IsDefined(permissions.DefaultMode) ||
            (permissions.ConfirmationCategories & ~AgentConfirmationCategories.All) != 0 ||
            (permissions.DefaultModel is not null && !AgentPersistenceJson.IsSafeShortText(permissions.DefaultModel, MaximumModelIdChars)))
            return "PermissionsInvalid";
        var exclusions = permissions.Workspace.Exclusions;
        if (exclusions.Count > MaximumExclusions ||
            exclusions.Any(pattern => pattern is null || pattern.Length == 0 || !AgentPersistenceJson.IsSafeShortText(pattern, MaximumExclusionChars)))
            return "PermissionsExclusionsInvalid";
        if (permissions.SelectedConnectionIds.Count > MaximumSelectedConnections ||
            permissions.SelectedConnectionIds.Contains(Guid.Empty))
            return "PermissionsConnectionsInvalid";
        if (permissions.EnabledReadTools.Count > MaximumEnabledReadTools ||
            permissions.EnabledReadTools.Any(tool => tool is null || tool.Length == 0 || !AgentPersistenceJson.IsSafeShortText(tool, MaximumToolNameChars)))
            return "PermissionsToolsInvalid";
        return null;
    }

    private static AgentProviderPermissionsDecoded Unreadable() => new(AgentStoredDocumentState.Unreadable, null);
}
