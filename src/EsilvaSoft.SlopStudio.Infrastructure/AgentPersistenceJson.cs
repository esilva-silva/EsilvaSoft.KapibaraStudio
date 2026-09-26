using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// JSON shapes of the agent conversation and provider permission documents. Enums are persisted by name (the Core
/// enums are append-only by name) and numbers are refused. Unknown members are refused, so a document carrying fields
/// this version does not know is reported instead of being silently truncated by a later save; a future format that
/// adds fields must bump its <c>FormatVersion</c>. Computed read-only properties are not written.
/// </summary>
internal static class AgentPersistenceJson
{
    internal static JsonSerializerOptions Conversation { get; } = Create(strictNullability: true);

    /// <summary>Tolerates null sections (normalized to defaults after reading); everything else is as strict.</summary>
    internal static JsonSerializerOptions Permissions { get; } = Create(strictNullability: false);

    private static JsonSerializerOptions Create(bool strictNullability)
    {
        var options = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = strictNullability,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RemoveReadOnlyProperties } },
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    private static void RemoveReadOnlyProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
        // e.g. AgentProviderPermissions.HasExternalDestinationConsent: derived, never stored.
        for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            var property = typeInfo.Properties[index];
            if (property.Set is null && property.AssociatedParameter is null) typeInfo.Properties.RemoveAt(index);
        }
    }

    /// <summary>Provider ids are short lowercase keys (e.g. <c>claude-code</c>), so LiteDB's case-insensitive collation cannot merge two of them.</summary>
    internal static bool IsValidProviderId(string? providerId) =>
        providerId is { Length: > 0 and <= 64 } &&
        (char.IsAsciiLetterLower(providerId[0]) || char.IsAsciiDigit(providerId[0])) &&
        providerId.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.');

    internal static bool IsSafeShortText(string? text, int maximumLength) =>
        text is not null && text.Length <= maximumLength && !text.Any(char.IsControl);
}
