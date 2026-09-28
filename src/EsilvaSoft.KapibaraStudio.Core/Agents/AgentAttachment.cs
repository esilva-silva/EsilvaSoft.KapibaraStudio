namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>Kind of a context chip. Persisted by name (append-only).</summary>
public enum AgentAttachmentKind
{
    /// <summary>Active editor buffer (unsaved text included), captured synchronously before the send.</summary>
    ActiveFile = 0,

    /// <summary>A file inside the workspace folder.</summary>
    WorkspaceFile = 1,

    /// <summary>A file outside the workspace, picked in the native dialog; only when permitted.</summary>
    ExternalFile = 2,

    /// <summary>Connection › database › collection of the originating tab (names only).</summary>
    TabMetadata = 3,
}

/// <summary>
/// Persistable description of an attachment that was sent: never its content. <see cref="PathOrName"/> is the path
/// relative to the workspace for workspace/active files inside it, otherwise only the file name (external files never
/// keep their absolute path). <see cref="Sha256"/> is the lowercase hex hash of the UTF-8 content actually sent (after
/// redaction), so the history can show identity without keeping data.
/// </summary>
public sealed record AgentAttachmentDescriptor(
    AgentAttachmentKind Kind,
    string DisplayName,
    string? PathOrName,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Runtime attachment of one turn, with its redacted content. Never persisted: only <see cref="ToDescriptor"/> is.
/// <see cref="ToString"/> omits the content so it cannot leak into logs.
/// </summary>
public sealed record AgentContextAttachment(
    AgentAttachmentKind Kind,
    string DisplayName,
    string? PathOrName,
    string Content,
    long SizeBytes,
    string Sha256)
{
    public AgentAttachmentDescriptor ToDescriptor() => new(Kind, DisplayName, PathOrName, SizeBytes, Sha256);

    public override string ToString() =>
        $"{nameof(AgentContextAttachment)} {{ Kind = {Kind}, DisplayName = {DisplayName}, SizeBytes = {SizeBytes} }}";
}
