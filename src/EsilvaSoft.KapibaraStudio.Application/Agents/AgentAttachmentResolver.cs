using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// A chip to resolve. <see cref="Path"/> is used only by workspace (relative or absolute) and external files.
/// <see cref="ToString"/> shows the file name only.
/// </summary>
public sealed record AgentAttachmentRequest(AgentAttachmentKind Kind, string? Path = null)
{
    public override string ToString() =>
        $"{nameof(AgentAttachmentRequest)} {{ Kind = {Kind}, Name = {AgentAttachmentResolver.SafeFileName(Path)} }}";
}

/// <summary>Typed reason an attachment was refused. Codes are safe to show; they never embed paths or content.</summary>
public enum AgentAttachmentError
{
    ConsentMissing,
    NotPermitted,
    NoWorkspace,
    NoActiveFile,
    NoTabMetadata,
    InvalidPath,

    /// <summary>Not strictly inside the workspace, or reached through a link/junction.</summary>
    OutsideWorkspace,
    Excluded,
    InvalidExclusion,
    NotFound,
    FileTooLarge,
    MessageTooLarge,

    /// <summary>Binary, invalid UTF-8/UTF-16 or containing an unpaired surrogate.</summary>
    NotText,
    Unreadable,
    RedactionFailed,

    /// <summary>The permissions are malformed (<see cref="AgentProviderPermissions.IsWellFormed"/>); nothing resolves.</summary>
    InvalidPermissions,

    /// <summary>A path segment is an alias (NTFS stream, 8.3 short name, trailing dot/space).</summary>
    UnsafePath,
}

/// <summary>One refused request; <see cref="DisplayName"/> is a file name or chip label, never an absolute path.</summary>
public sealed record AgentAttachmentFailure(
    int RequestIndex, AgentAttachmentKind Kind, string DisplayName, AgentAttachmentError Error);

/// <summary>Resolved attachments (in request order) and the refusals. Callers should not send when any refusal exists.</summary>
public sealed record AgentAttachmentResolution(
    IReadOnlyList<AgentContextAttachment> Attachments, IReadOnlyList<AgentAttachmentFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;

    public long TotalBytes => Attachments.Sum(static attachment => attachment.SizeBytes);
}

/// <summary>
/// Turns chips into redacted runtime attachments. Rules: nothing when the permissions are malformed or without
/// persisted consent; each kind needs its data-sending permission; workspace files go through
/// <see cref="AgentWorkspacePaths.TryResolveInside"/> (strict containment, alias segments, links, exclusions); the
/// active file and external files are checked against the exclusions by their real path
/// (<see cref="AgentWorkspacePaths.CheckFile"/>), never by a tab title; external files only when permitted and kept by
/// file name only; a null exclusion list means the defaults and an invalid glob refuses every file (fail closed); the
/// active file content comes from the captured buffer (unsaved text included), never from disk; text only
/// (UTF-8/UTF-16 with BOM). Contents go through the same best-effort redaction as the tab context
/// (<see cref="AgentContextProvider"/>). Limits apply to the UTF-8 size of the redacted text:
/// <see cref="MaximumFileBytes"/> per attachment, and <see cref="MaximumMessageBytes"/> for the user message plus every
/// attachment of the send. Failures are typed values; only cancellation throws.
/// </summary>
public static class AgentAttachmentResolver
{
    public const int MaximumFileBytes = 256 * 1024;

    public const int MaximumMessageBytes = 1024 * 1024;

    private const string UntitledName = "Sem título";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Resolves without counting a user message against <see cref="MaximumMessageBytes"/>.</summary>
    public static Task<AgentAttachmentResolution> ResolveAsync(
        IReadOnlyList<AgentAttachmentRequest> requests,
        AgentWorkspaceContext context,
        AgentProviderPermissions permissions,
        CancellationToken cancellationToken, IAgentBoundedFileReader? fileReader = null, IAgentWorkspacePathProbe? pathProbe = null) =>
        ResolveAsync(requests, context, permissions, null, cancellationToken, fileReader, pathProbe);

    /// <summary>
    /// Resolves the chips of one send. The UTF-8 size of <paramref name="userMessage"/> is reserved first from
    /// <see cref="MaximumMessageBytes"/>; an attachment that no longer fits is refused with
    /// <see cref="AgentAttachmentError.MessageTooLarge"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<AgentAttachmentResolution> ResolveAsync(
        IReadOnlyList<AgentAttachmentRequest> requests,
        AgentWorkspaceContext context,
        AgentProviderPermissions permissions,
        string? userMessage,
        CancellationToken cancellationToken, IAgentBoundedFileReader? fileReader = null, IAgentWorkspacePathProbe? pathProbe = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permissions);

        var attachments = new List<AgentContextAttachment>();
        var failures = new List<AgentAttachmentFailure>();
        var wellFormed = permissions.IsWellFormed;
        var exclusions = wellFormed ? permissions.Workspace.EffectiveExclusions : AgentWorkspacePermissions.DefaultExclusions;
        long total = userMessage is null ? 0 : Encoding.UTF8.GetByteCount(userMessage);

        for (var index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = requests[index] ?? throw new ArgumentException("Pedido de anexo nulo.", nameof(requests));
            var outcome = !wellFormed
                ? Fail(DisplayNameOf(request, context), AgentAttachmentError.InvalidPermissions)
                : !permissions.HasExternalDestinationConsent
                    ? Fail(DisplayNameOf(request, context), AgentAttachmentError.ConsentMissing)
                    : await ResolveOneAsync(request, context, permissions, exclusions, cancellationToken, fileReader, pathProbe).ConfigureAwait(false);

            if (outcome.Attachment is { } attachment)
            {
                if (total + attachment.SizeBytes > MaximumMessageBytes)
                {
                    failures.Add(new AgentAttachmentFailure(index, request.Kind, attachment.DisplayName,
                        AgentAttachmentError.MessageTooLarge));
                    continue;
                }

                total += attachment.SizeBytes;
                attachments.Add(attachment);
            }
            else
            {
                failures.Add(new AgentAttachmentFailure(index, request.Kind, outcome.DisplayName, outcome.Error));
            }
        }

        return new AgentAttachmentResolution(attachments, failures);
    }

    private readonly record struct Outcome(AgentContextAttachment? Attachment, string DisplayName, AgentAttachmentError Error);

    private static Outcome Fail(string displayName, AgentAttachmentError error) => new(null, displayName, error);

    private static Task<Outcome> ResolveOneAsync(
        AgentAttachmentRequest request,
        AgentWorkspaceContext context,
        AgentProviderPermissions permissions,
        IReadOnlyList<string> exclusions,
        CancellationToken cancellationToken, IAgentBoundedFileReader? fileReader = null, IAgentWorkspacePathProbe? pathProbe = null)
    {
        var sending = permissions.DataSending;
        var displayName = DisplayNameOf(request, context);
        return request.Kind switch
        {
            AgentAttachmentKind.ActiveFile => Task.FromResult(!sending.ActiveFile
                ? Fail(displayName, AgentAttachmentError.NotPermitted)
                : ResolveActiveFile(context, exclusions, pathProbe)),
            AgentAttachmentKind.TabMetadata => Task.FromResult(!sending.TabMetadata
                ? Fail(displayName, AgentAttachmentError.NotPermitted)
                : ResolveTabMetadata(context)),
            AgentAttachmentKind.WorkspaceFile => !sending.WorkspaceFiles || !permissions.Workspace.UseFilesFolder
                ? Task.FromResult(Fail(displayName, AgentAttachmentError.NotPermitted))
                : ResolveWorkspaceFileAsync(request, context, exclusions, cancellationToken, fileReader, pathProbe),
            AgentAttachmentKind.ExternalFile => !sending.ExternalAttachments
                ? Task.FromResult(Fail(displayName, AgentAttachmentError.NotPermitted))
                : ResolveExternalFileAsync(request, context, exclusions, cancellationToken, fileReader, pathProbe),
            _ => Task.FromResult(Fail(displayName, AgentAttachmentError.NotPermitted)),
        };
    }

    private static Outcome ResolveActiveFile(AgentWorkspaceContext context, IReadOnlyList<string> exclusions, IAgentWorkspacePathProbe? pathProbe)
    {
        var displayName = ActiveDisplayName(context);
        if (context.BufferText is not { } buffer)
        {
            return Fail(displayName, AgentAttachmentError.NoActiveFile);
        }

        // An untitled buffer has no path: nothing on disk to exclude. A saved one is matched by its real path.
        var pathOrName = displayName;
        if (!string.IsNullOrWhiteSpace(context.ActiveFilePath))
        {
            var error = AgentWorkspacePaths.CheckFile(context.ActiveFilePath, context.WorkspaceFolder, exclusions, pathProbe);
            if (error != AgentWorkspacePathError.None)
            {
                return Fail(displayName, Map(error));
            }

            AgentWorkspacePaths.TryGetFullPath(context.ActiveFilePath, out var full);
            pathOrName = AgentWorkspacePaths.TryGetWorkspaceRoot(context.WorkspaceFolder, out var root, pathProbe) &&
                         AgentWorkspacePaths.IsStrictlyInside(full, root)
                ? AgentWorkspaceExclusions.NormalizeRelativePath(Path.GetRelativePath(root, full))
                : Path.GetFileName(full);
        }

        if (!TryGetUtf8ByteCount(buffer, out var bytes))
        {
            return Fail(displayName, AgentAttachmentError.NotText);
        }

        if (bytes > MaximumFileBytes)
        {
            return Fail(displayName, AgentAttachmentError.FileTooLarge);
        }

        return Build(AgentAttachmentKind.ActiveFile, displayName, pathOrName, buffer);
    }

    private static Outcome ResolveTabMetadata(AgentWorkspaceContext context)
    {
        var connectionId = Guid.TryParse(context.ConnectionId, out var id) && id != Guid.Empty
            ? id.ToString("D")
            : null;
        var name = CleanName(context.ConnectionName);
        var database = CleanName(context.DatabaseName);
        var collection = database is null ? null : CleanName(context.CollectionName);
        if (connectionId is null && name is null)
        {
            return Fail("Aba", AgentAttachmentError.NoTabMetadata);
        }

        var builder = new StringBuilder();
        if (name is not null)
        {
            builder.Append("Conexão: ").Append(name).Append('\n');
        }

        if (connectionId is not null)
        {
            builder.Append("Conexão (ID lógico): ").Append(connectionId).Append('\n');
        }

        if (database is not null)
        {
            builder.Append("Banco: ").Append(database).Append('\n');
        }

        if (collection is not null)
        {
            builder.Append("Coleção: ").Append(collection).Append('\n');
        }

        var label = string.Join(" › ", new[] { name ?? "conexão", database, collection }.Where(static part => part is not null));
        return Build(AgentAttachmentKind.TabMetadata, label, null, builder.ToString().TrimEnd('\n'));
    }

    private static async Task<Outcome> ResolveWorkspaceFileAsync(
        AgentAttachmentRequest request,
        AgentWorkspaceContext context,
        IReadOnlyList<string> exclusions,
        CancellationToken cancellationToken, IAgentBoundedFileReader? fileReader = null, IAgentWorkspacePathProbe? pathProbe = null)
    {
        var displayName = DisplayNameOf(request, context);
        if (!AgentWorkspacePaths.TryResolveInside(context.WorkspaceFolder, request.Path, exclusions,
                out var full, out var relative, out var error, pathProbe))
        {
            return Fail(displayName, Map(error));
        }

        return await ReadFileAsync(AgentAttachmentKind.WorkspaceFile, full, displayName, relative, fileReader, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Outcome> ResolveExternalFileAsync(
        AgentAttachmentRequest request,
        AgentWorkspaceContext context,
        IReadOnlyList<string> exclusions,
        CancellationToken cancellationToken, IAgentBoundedFileReader? fileReader = null, IAgentWorkspacePathProbe? pathProbe = null)
    {
        var fileName = SafeFileName(request.Path);
        var error = AgentWorkspacePaths.CheckFile(request.Path, context.WorkspaceFolder, exclusions, pathProbe);
        if (error != AgentWorkspacePathError.None)
        {
            return Fail(fileName, Map(error));
        }

        AgentWorkspacePaths.TryGetFullPath(request.Path!, out var full);
        // Only the file name is kept: the absolute path of an external file never leaves the machine or the history.
        return await ReadFileAsync(AgentAttachmentKind.ExternalFile, full, fileName, Path.GetFileName(full), fileReader, cancellationToken)
            .ConfigureAwait(false);
    }

    private static AgentAttachmentError Map(AgentWorkspacePathError error) => error switch
    {
        AgentWorkspacePathError.NoWorkspace => AgentAttachmentError.NoWorkspace,
        AgentWorkspacePathError.UnsafePath => AgentAttachmentError.UnsafePath,
        AgentWorkspacePathError.OutsideWorkspace or AgentWorkspacePathError.LinkTraversal => AgentAttachmentError.OutsideWorkspace,
        AgentWorkspacePathError.Excluded => AgentAttachmentError.Excluded,
        AgentWorkspacePathError.InvalidExclusion => AgentAttachmentError.InvalidExclusion,
        _ => AgentAttachmentError.InvalidPath,
    };

    private static async Task<Outcome> ReadFileAsync(
        AgentAttachmentKind kind, string fullPath, string displayName, string pathOrName, IAgentBoundedFileReader? fileReader, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            if (fileReader is null) return Fail(displayName, AgentAttachmentError.Unreadable);
            var result = await fileReader.ReadAsync(fullPath, MaximumFileBytes + 4, cancellationToken).ConfigureAwait(false);
            if (result.State == AgentFileReadState.NotFound) return Fail(displayName, AgentAttachmentError.NotFound);
            if (result.State == AgentFileReadState.TooLarge) return Fail(displayName, AgentAttachmentError.FileTooLarge);
            bytes = result.Bytes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail(displayName, AgentAttachmentError.NotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Fail(displayName, AgentAttachmentError.Unreadable);
        }

        if (!TryDecode(bytes, out var text) || !TryGetUtf8ByteCount(text, out var size))
        {
            return Fail(displayName, AgentAttachmentError.NotText);
        }

        if (size > MaximumFileBytes)
        {
            return Fail(displayName, AgentAttachmentError.FileTooLarge);
        }

        return Build(kind, displayName, pathOrName, text);
    }

    private static Outcome Build(AgentAttachmentKind kind, string displayName, string? pathOrName, string content)
    {
        string redacted;
        try
        {
            redacted = AgentContextProvider.Redact(content) ?? string.Empty;
        }
        catch (AgentRuntimeException)
        {
            return Fail(displayName, AgentAttachmentError.RedactionFailed);
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(redacted);
        }
        catch (EncoderFallbackException)
        {
            return Fail(displayName, AgentAttachmentError.NotText);
        }

        // Checked again after redaction: a marker may be longer than what it replaced.
        if (bytes.Length > MaximumFileBytes)
        {
            return Fail(displayName, AgentAttachmentError.FileTooLarge);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new(new AgentContextAttachment(kind, displayName, pathOrName, redacted, bytes.Length, hash), displayName,
            default);
    }

    private static bool TryGetUtf8ByteCount(string text, out int count)
    {
        try
        {
            count = StrictUtf8.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            count = 0;
            return false;
        }
    }

    private static bool TryDecode(byte[] bytes, out string text)
    {
        text = string.Empty;
        try
        {
            if (bytes is [0xEF, 0xBB, 0xBF, ..])
            {
                text = StrictUtf8.GetString(bytes, 3, bytes.Length - 3);
            }
            else if (bytes is [0xFF, 0xFE, ..])
            {
                text = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                    .GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes is [0xFE, 0xFF, ..])
            {
                text = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true)
                    .GetString(bytes, 2, bytes.Length - 2);
            }
            else
            {
                text = StrictUtf8.GetString(bytes);
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return !text.Contains('\0', StringComparison.Ordinal);
    }

    private static string DisplayNameOf(AgentAttachmentRequest request, AgentWorkspaceContext context) => request.Kind switch
    {
        AgentAttachmentKind.ActiveFile => ActiveDisplayName(context),
        AgentAttachmentKind.TabMetadata => "Aba",
        _ => SafeFileName(request.Path),
    };

    private static string ActiveDisplayName(AgentWorkspaceContext context) =>
        CleanName(context.ActiveFileName) ?? CleanName(SafeFileName(context.ActiveFilePath)) ?? UntitledName;

    internal static string SafeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl))
        {
            return UntitledName;
        }

        var name = Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        return name.Length == 0 ? UntitledName : name;
    }

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
        {
            return null;
        }

        try
        {
            return AgentContextProvider.Redact(name.Trim());
        }
        catch (AgentRuntimeException)
        {
            return null;
        }
    }
}
