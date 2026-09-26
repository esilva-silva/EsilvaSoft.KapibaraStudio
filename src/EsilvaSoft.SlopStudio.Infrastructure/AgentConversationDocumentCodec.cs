using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>How a stored agent document was classified when read.</summary>
internal enum AgentStoredDocumentState
{
    Readable = 0,
    Unreadable = 1,
    UnsupportedVersion = 2,
}

/// <summary>
/// Classification of one stored conversation. Metadata fields are best effort (null when unreadable) and come from
/// the top-level fields, so a listing can still name an unreadable or newer-format document.
/// </summary>
internal sealed record AgentConversationDecoded(
    AgentStoredDocumentState State,
    Guid Id,
    AgentConversation? Conversation,
    string? ProviderId,
    string? Title,
    DateTimeOffset? UpdatedAt,
    long Revision);

/// <summary>
/// Document format of the <c>agentConversations</c> facet: one document per conversation with a few top-level fields
/// (identity, provider, revision, update time and title, used for listing and CAS) and the whole conversation as a
/// closed JSON payload. A <c>formatVersion</c> above <see cref="AgentConversation.CurrentFormatVersion"/> is read-only;
/// anything that fails the closed schema or the limits below is unreadable. Neither is ever repaired or replaced.
/// Messages, titles and attachment names are redacted before writing; attachments are descriptors only.
/// </summary>
internal static class AgentConversationDocumentCodec
{
    /// <summary>Per provider. Creating one more is refused (<c>ConversationLimitReached</c>): the UI chooses what to delete.</summary>
    internal const int MaximumConversationsPerProvider = 200;

    /// <summary>Per entry text, checked before (bounding the redaction work) and after redaction.</summary>
    internal const int MaximumEntryTextChars = 64 * 1024;

    /// <summary>UTF-8 size of the serialized conversation.</summary>
    internal const int MaximumDocumentBytes = 4 * 1024 * 1024;

    internal const int MaximumEntries = 2000;
    internal const int MaximumAttachmentsPerEntry = 64;
    internal const int MaximumTitleChars = 200;
    internal const int MaximumModelIdChars = 128;
    internal const int MaximumProviderSessionIdChars = 256;
    internal const int MaximumToolNameChars = 128;
    internal const int MaximumAttachmentNameChars = 512;
    internal const int MaximumAttachmentPathChars = 1024;

    private static readonly string[] DocumentFields =
        ["_id", "formatVersion", "providerId", "revision", "updatedAtUtc", "title", "json"];

    /// <summary>
    /// Validates the caller's value and returns the copy to store: redacted text, the given revision and the current
    /// format. Returns null with a stable error code when the value is refused; nothing is written in that case.
    /// </summary>
    /// <exception cref="AgentRuntimeException">Redaction timed out (fail closed: the caller maps it, nothing is written).</exception>
    internal static AgentConversation? Prepare(AgentConversation? conversation, long revision, out string? errorCode)
    {
        if (conversation is null || conversation.Entries is null || conversation.Title is null)
        {
            errorCode = "ConversationInvalid";
            return null;
        }

        if (conversation.Entries.Count > MaximumEntries)
        {
            errorCode = "ConversationTooManyEntries";
            return null;
        }

        // Input bound before redaction; checked again on the redacted text below.
        if (conversation.Title.Length > MaximumTitleChars * 4 ||
            conversation.Entries.Any(entry => entry?.Text is { Length: > MaximumEntryTextChars }))
        {
            errorCode = "ConversationEntryTooLarge";
            return null;
        }

        var entries = new List<AgentConversationEntry>(conversation.Entries.Count);
        foreach (var entry in conversation.Entries)
        {
            if (entry is null || entry.Text is null || entry.Attachments is null ||
                entry.Attachments.Count > MaximumAttachmentsPerEntry || entry.Attachments.Any(item => item is null))
            {
                errorCode = "ConversationEntryInvalid";
                return null;
            }

            entries.Add(entry with
            {
                Text = Redact(entry.Text),
                Attachments = entry.Attachments.Select(attachment => attachment with
                {
                    DisplayName = Redact(attachment.DisplayName ?? string.Empty),
                    PathOrName = attachment.PathOrName is null ? null : Redact(attachment.PathOrName),
                }).ToArray(),
            });
        }

        var stored = conversation with
        {
            Title = Redact(conversation.Title),
            Revision = revision,
            Entries = entries,
            FormatVersion = AgentConversation.CurrentFormatVersion,
        };
        errorCode = Validate(stored);
        return errorCode is null ? stored : null;
    }

    /// <summary>Encodes a prepared conversation; <paramref name="payloadBytes"/> is the UTF-8 size checked against the limit.</summary>
    internal static BsonDocument Encode(AgentConversation stored, out int payloadBytes)
    {
        var json = JsonSerializer.Serialize(stored, AgentPersistenceJson.Conversation);
        payloadBytes = Encoding.UTF8.GetByteCount(json);
        return new BsonDocument
        {
            ["_id"] = stored.Id,
            ["formatVersion"] = stored.FormatVersion,
            ["providerId"] = stored.ProviderId,
            ["revision"] = stored.Revision,
            ["updatedAtUtc"] = stored.UpdatedAt.UtcDateTime,
            ["title"] = stored.Title,
            ["json"] = json,
        };
    }

    /// <summary>Classifies a stored document. Never throws for content problems and never includes content in diagnostics.</summary>
    internal static AgentConversationDecoded Decode(BsonDocument document)
    {
        var id = document.TryGetValue("_id", out var idValue) && idValue.IsGuid ? idValue.AsGuid : Guid.Empty;
        var providerId = document.TryGetValue("providerId", out var providerValue) && providerValue.IsString ? providerValue.AsString : null;
        var title = document.TryGetValue("title", out var titleValue) && titleValue.IsString ? titleValue.AsString : null;
        DateTimeOffset? updatedAt = document.TryGetValue("updatedAtUtc", out var updatedValue) && updatedValue.IsDateTime
            ? LiteDbDates.ToUtcInstant(updatedValue.AsDateTime)
            : null;
        var revision = document.TryGetValue("revision", out var revisionValue) && revisionValue.IsInt64 ? revisionValue.AsInt64 : 0;

        AgentConversationDecoded Classified(AgentStoredDocumentState state, AgentConversation? conversation = null) =>
            new(state, id, conversation, providerId, title, updatedAt, revision);

        if (document.TryGetValue("formatVersion", out var versionValue) && versionValue.IsInt32 &&
            versionValue.AsInt32 > AgentConversation.CurrentFormatVersion)
            return Classified(AgentStoredDocumentState.UnsupportedVersion);

        if (id == Guid.Empty || providerId is null || title is null || updatedAt is null || revision < 1 ||
            document.Count != DocumentFields.Length || !document.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(DocumentFields) ||
            versionValue is not { IsInt32: true } || versionValue.AsInt32 != AgentConversation.CurrentFormatVersion ||
            !document["json"].IsString)
            return Classified(AgentStoredDocumentState.Unreadable);

        AgentConversation? conversation;
        try
        {
            conversation = JsonSerializer.Deserialize<AgentConversation>(document["json"].AsString, AgentPersistenceJson.Conversation);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return Classified(AgentStoredDocumentState.Unreadable);
        }

        if (conversation is null || Validate(conversation) is not null || conversation.Id != id ||
            !string.Equals(conversation.ProviderId, providerId, StringComparison.Ordinal) ||
            !string.Equals(conversation.Title, title, StringComparison.Ordinal) || conversation.Revision != revision)
            return Classified(AgentStoredDocumentState.Unreadable);

        return Classified(AgentStoredDocumentState.Readable, conversation);
    }

    /// <summary>Structural and limit checks shared by writing (after redaction) and reading.</summary>
    private static string? Validate(AgentConversation conversation)
    {
        if (conversation.Id == Guid.Empty) return "ConversationIdInvalid";
        if (!AgentPersistenceJson.IsValidProviderId(conversation.ProviderId)) return "ProviderIdInvalid";
        if (conversation.FormatVersion != AgentConversation.CurrentFormatVersion || conversation.Revision < 1 ||
            !Enum.IsDefined(conversation.Mode) || conversation.Entries is null ||
            !AgentPersistenceJson.IsSafeShortText(conversation.Title, MaximumTitleChars) ||
            (conversation.ModelId is not null && !AgentPersistenceJson.IsSafeShortText(conversation.ModelId, MaximumModelIdChars)) ||
            (conversation.ProviderSessionId is not null &&
             !AgentPersistenceJson.IsSafeShortText(conversation.ProviderSessionId, MaximumProviderSessionIdChars)))
            return "ConversationInvalid";
        if (conversation.Entries.Count > MaximumEntries) return "ConversationTooManyEntries";
        foreach (var entry in conversation.Entries)
        {
            if (entry?.Text is null || entry.Attachments is null) return "ConversationEntryInvalid";
            if (entry.Text.Length > MaximumEntryTextChars) return "ConversationEntryTooLarge";
            if (!Enum.IsDefined(entry.Kind) ||
                (entry.ToolName is not null && !AgentPersistenceJson.IsSafeShortText(entry.ToolName, MaximumToolNameChars)) ||
                (entry.ToolOutcome is { } outcome && !Enum.IsDefined(outcome)) ||
                entry.ProposalId == Guid.Empty ||
                entry.Attachments.Count > MaximumAttachmentsPerEntry ||
                !entry.Attachments.All(IsValidAttachment))
                return "ConversationEntryInvalid";
        }

        return null;
    }

    /// <summary>
    /// Descriptor only. Paths must be relative (workspace files) and external files keep only their name: an absolute
    /// path is refused rather than stored.
    /// </summary>
    private static bool IsValidAttachment(AgentAttachmentDescriptor? attachment)
    {
        if (attachment is null || !Enum.IsDefined(attachment.Kind) || attachment.SizeBytes < 0 ||
            !AgentPersistenceJson.IsSafeShortText(attachment.DisplayName, MaximumAttachmentNameChars) ||
            attachment.Sha256 is not { Length: 64 } hash || !hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
            return false;
        if (attachment.PathOrName is not { } path) return true;
        if (!AgentPersistenceJson.IsSafeShortText(path, MaximumAttachmentPathChars) || path.Length == 0 ||
            Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':', StringComparison.Ordinal) ||
            path.Split('/', '\\').Any(segment => segment == ".."))
            return false;
        return attachment.Kind != AgentAttachmentKind.ExternalFile || path.IndexOfAny(['/', '\\']) < 0;
    }

    private static string Redact(string text) => AgentSecretRedaction.Redact(text) ?? string.Empty;
}
