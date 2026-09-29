using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// <c>propose_file_edit</c>: builds an <see cref="AgentEditProposal"/> and hands it to the Desktop sink, which answers at
/// once. This path NEVER writes a file: it reads the base text (the open buffer, unsaved text included, or the file on
/// disk up to 256 KB) and the only output is the proposal object and a small receipt. Targets are validated inside the
/// workspace against the exclusions, the per-provider edit permissions and the data-sending permission of the target
/// (active file or workspace files). The model saw the REDACTED text, so <c>old_text</c> is matched against the redacted
/// text (exactly once) and the result is mapped back to the original line by line; a change touching a line altered by
/// the redaction cannot be mapped safely and is refused. Every refusal of that kind uses one generic code
/// (<c>EditNotApplicable</c>), so the tool is not an oracle of the file content ("absent" vs "ambiguous" vs "no change").
/// </summary>
public sealed partial class AgentToolRegistry
{
    internal const int MaximumProposalTextBytes = 256 * 1024;
    private const int MaximumProposalEdits = 50;

    /// <summary>Typed refusals of <c>propose_file_edit</c>; safe to return to the model (no path or text).</summary>
    internal static class ProposalErrors
    {
        public const string NoWorkspace = "NoWorkspace";
        public const string InvalidPath = "InvalidPath";
        public const string OutsideWorkspace = "OutsideWorkspace";
        public const string Excluded = "Excluded";
        public const string TargetNotPermitted = "TargetNotPermitted";
        public const string NotFound = "NotFound";
        public const string FileTooLarge = "FileTooLarge";
        public const string NotText = "NotText";
        /// <summary>old_text absent, ambiguous, no resulting change, or not mappable past a redacted line.</summary>
        public const string EditNotApplicable = "EditNotApplicable";
        public const string RedactionMarkerIntroduced = "RedactionMarkerIntroduced";
        public const string TargetUnavailable = "TargetUnavailable";
        public const string BaseChanged = "BaseChanged";
        public const string ProposalRejected = "ProposalRejected";
    }

    private const string ProposeFileEditInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"target":{"type":"string","const":"active_buffer"},"path":{"type":"string","minLength":1,"maxLength":1024},"edits":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["old_text","new_text"],"properties":{"old_text":{"type":"string","minLength":1,"maxLength":65536},"new_text":{"type":"string","maxLength":65536}}}},"new_content":{"type":"string","maxLength":65536}},"oneOf":[{"required":["path"],"not":{"required":["target"]}},{"required":["target"],"properties":{"target":{"const":"active_buffer"}},"not":{"required":["path"]}}],"allOf":[{"oneOf":[{"required":["edits"]},{"required":["new_content"]}]}]}
        """;

    private const string ProposeFileEditOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["status","proposalId","added","removed","hunks"],"properties":{"status":{"type":"string","const":"registered"},"proposalId":{"type":"string","format":"uuid"},"added":{"type":"integer","minimum":0},"removed":{"type":"integer","minimum":0},"hunks":{"type":"integer","minimum":1}}}
        """;

    private static readonly UTF8Encoding StrictProposalUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private async Task<AgentToolInvocationResult> InvokeProposeFileEditAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseProposalArguments(argumentsJson, out var path, out var activeBuffer, out var edits, out var newContent))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, ProposeFileEditToolName, out var scope) ||
            _sessionTools is not { ProposalSink: { } sink } || scope.WorkspaceContext is not { } snapshot)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        var permissions = scope.Permissions!;
        var editPermissions = permissions.EditProposals ?? new AgentEditProposalPermissions();

        var isActive = activeBuffer;
        string? fullPath = null;
        if (activeBuffer)
        {
            if (string.IsNullOrWhiteSpace(snapshot.TabId) || snapshot.BufferText is null)
                return Refuse(ProposalErrors.TargetUnavailable);
        }
        else
        {
            if (permissions.Workspace?.UseFilesFolder != true ||
                !AgentWorkspacePaths.TryGetWorkspaceRoot(snapshot.WorkspaceFolder, out var workspace))
                return Refuse(ProposalErrors.NoWorkspace);
            var exclusions = permissions.Workspace?.Exclusions ?? [];
            // Single path-safety rule shared with the attachment resolver (containment, ADS/8.3 aliases, links, exclusions).
            if (!AgentWorkspacePaths.TryResolveInside(workspace, path!, exclusions, out fullPath, out _, out var pathError))
                return Refuse(pathError switch
                {
                    AgentWorkspacePathError.NoWorkspace => ProposalErrors.NoWorkspace,
                    AgentWorkspacePathError.OutsideWorkspace or AgentWorkspacePathError.LinkTraversal => ProposalErrors.OutsideWorkspace,
                    AgentWorkspacePathError.Excluded or AgentWorkspacePathError.InvalidExclusion => ProposalErrors.Excluded,
                    _ => ProposalErrors.InvalidPath
                });
            isActive = snapshot.BufferText is not null && snapshot.ActiveFilePath is { } activePath &&
                AgentWorkspacePaths.TryResolveInside(workspace, activePath, exclusions, out var activeFull, out _, out _) &&
                string.Equals(activeFull, fullPath, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        var sending = permissions.DataSending ?? new AgentDataSendingPermissions();
        if (isActive
                ? !editPermissions.ActiveFile || !sending.ActiveFile
                : !editPermissions.OtherWorkspaceFiles || !sending.WorkspaceFiles)
            return Refuse(ProposalErrors.TargetNotPermitted);

        string original;
        if (isActive)
        {
            original = snapshot.BufferText!;
        }
        else
        {
            var read = await ReadProposalBaseAsync(fullPath!, cancellationToken).ConfigureAwait(false);
            if (read.Error is { } readError) return Refuse(readError);
            original = read.Text!;
        }
        if (Encoding.UTF8.GetByteCount(original) > MaximumProposalTextBytes)
            return Refuse(ProposalErrors.FileTooLarge);

        // The model only ever saw the redacted text: match there, then map the change back to the original.
        string redacted;
        try
        {
            redacted = AgentSecretRedaction.Redact(original) ?? string.Empty;
        }
        catch (AgentRuntimeException)
        {
            return Refuse(ProposalErrors.EditNotApplicable);
        }
        var proposedRedacted = ApplyProposalEdits(redacted, edits, newContent, out var applyError);
        if (applyError is not null) return Refuse(applyError);
        // The redacted base is what the model saw. Catch a newly introduced marker before mapping the edit back to
        // the secret-bearing source: the mapping intentionally refuses edits that touch a redacted line.
        if (AgentRedactionMarkers.IntroducesRedactionMarker(redacted, proposedRedacted))
            return Refuse(ProposalErrors.RedactionMarkerIntroduced);
        var proposed = MapRedactedChange(original, redacted, proposedRedacted!);
        if (proposed is null) return Refuse(ProposalErrors.EditNotApplicable);
        if (Encoding.UTF8.GetByteCount(proposed) > MaximumProposalTextBytes)
            return Refuse(ProposalErrors.FileTooLarge);
        cancellationToken.ThrowIfCancellationRequested();

        if (!LineDiff.TryCompute(original, proposed, out var hunks))
            return Refuse(ProposalErrors.FileTooLarge);
        if (hunks.Count == 0) return Refuse(ProposalErrors.EditNotApplicable);
        // The agent saw redacted text: a marker absent from the original would overwrite a real secret. Refused as a
        // whole (never registered, so it can never be applied, not even in Automatic mode).
        if (AgentRedactionMarkers.IntroducesRedactionMarker(original, proposed) ||
            hunks.Any(AgentRedactionMarkers.IntroducesRedactionMarker))
            return Refuse(ProposalErrors.RedactionMarkerIntroduced);

        var proposal = new AgentEditProposal(Guid.NewGuid(), scope.ConversationId, fullPath,
            isActive ? snapshot.TabId : null,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original))),
            original, proposed, hunks, DateTimeOffset.UtcNow)
        { TargetName = snapshot.ActiveFileName ?? (activeBuffer ? "Aba sem título" : Path.GetFileName(fullPath)) };
        // Release gate BEFORE the proposal leaves the registry: the channel and the plan must still be current.
        if (await RevalidateSessionReleaseAsync(principal!, context!, destination!, outputScope,
                ProposeFileEditToolName, cancellationToken)
                .ConfigureAwait(false) is { } lateDenial)
            return lateDenial;
        AgentEditProposalSubmission submission;
        try
        {
            submission = sink.Submit(proposal) ?? new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.Rejected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Refuse(ProposalErrors.ProposalRejected);
        }
        if (!submission.Registered)
            return Refuse(submission.Status switch
            {
                AgentEditProposalSubmissionStatus.TargetUnavailable => ProposalErrors.TargetUnavailable,
                AgentEditProposalSubmissionStatus.BaseChanged => ProposalErrors.BaseChanged,
                _ => ProposalErrors.ProposalRejected
            });

        var json = JsonSerializer.Serialize(new ProposalReceipt("registered", proposal.Id, proposal.AddedLineCount,
            proposal.RemovedLineCount, hunks.Count), SerializerOptions);
        return AgentToolInvocationResult.Success(json);
    }

    private static AgentToolInvocationResult Refuse(string code) =>
        AgentToolInvocationResult.Failure(code, AgentAuditDecisionReason.ValidationRejected);

    /// <summary>
    /// Applies the edits in order on the evolving text. Each <c>old_text</c> must occur exactly once; when it is absent
    /// as sent, it is retried with the base text's line terminator (models usually send <c>\n</c>).
    /// </summary>
    internal static string? ApplyProposalEdits(string original, IReadOnlyList<(string OldText, string NewText)>? edits,
        string? newContent, out string? error)
    {
        error = null;
        var lineEnding = LineDiff.DetectLineEnding(original, "\n");
        if (newContent is not null)
            return original.AsSpan().IndexOfAny('\r', '\n') < 0 ? newContent : LineDiff.NormalizeLineEndings(newContent, lineEnding);

        var text = original;
        foreach (var (oldText, newText) in edits!)
        {
            var target = oldText;
            var replacement = newText;
            var count = CountOccurrences(text, target);
            if (count == 0 && (oldText.Contains('\n') || oldText.Contains('\r')))
            {
                target = LineDiff.NormalizeLineEndings(oldText, lineEnding);
                replacement = LineDiff.NormalizeLineEndings(newText, lineEnding);
                count = CountOccurrences(text, target);
            }
            else if (newText.Contains('\n') || newText.Contains('\r'))
            {
                replacement = LineDiff.NormalizeLineEndings(newText, lineEnding);
            }
            if (count != 1)
            {
                error = ProposalErrors.EditNotApplicable;
                return null;
            }
            var index = text.IndexOf(target, StringComparison.Ordinal);
            text = string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + target.Length));
            if (text.Length > MaximumProposalTextBytes)
            {
                error = ProposalErrors.FileTooLarge;
                return null;
            }
        }
        return text;
    }

    /// <summary>
    /// Maps a change made on the redacted text back to the original, line by line. Lines equal in both texts map 1:1;
    /// a change that touches (or is inserted inside) a region altered by the redaction returns null. With no redaction
    /// the change is the proposal itself.
    /// </summary>
    internal static string? MapRedactedChange(string original, string redacted, string proposedRedacted)
    {
        if (string.Equals(original, redacted, StringComparison.Ordinal)) return proposedRedacted;
        if (!LineDiff.TryCompute(original, redacted, out var taint) ||
            !LineDiff.TryCompute(redacted, proposedRedacted, out var edits))
            return null;

        var (originalLines, originalTerminators) = SplitWithTerminators(original);
        var redactedCount = SplitWithTerminators(redacted).Lines.Count;
        // map[r] = original line of redacted line r, or -1 when the redaction altered it.
        var map = new int[redactedCount];
        int o = 0, r = 0;
        foreach (var hunk in taint.OrderBy(static hunk => hunk.OriginalStartLine))
        {
            while (r < hunk.ProposedStartLine && r < redactedCount) map[r++] = o++;
            for (var i = 0; i < hunk.ProposedLines.Count && r < redactedCount; i++) map[r++] = -1;
            o += hunk.OriginalLines.Count;
        }
        while (r < redactedCount) map[r++] = o++;

        var lineEnding = LineDiff.DetectLineEnding(original, "\n");
        var lines = new List<string>(originalLines);
        var terminators = new List<string>(originalTerminators);
        foreach (var hunk in edits.OrderByDescending(static hunk => hunk.OriginalStartLine))
        {
            var start = hunk.OriginalStartLine;
            var count = hunk.OriginalLines.Count;
            int target;
            if (count > 0)
            {
                if (start + count > redactedCount) return null;
                target = map[start];
                for (var i = 0; i < count; i++)
                    if (map[start + i] < 0 || map[start + i] != target + i) return null;
            }
            else
            {
                // Insertion before redacted line `start`: both neighbours must be unaltered and adjacent.
                target = start < redactedCount ? map[start] : lines.Count;
                if (target < 0 || start > 0 && (map[start - 1] < 0 || map[start - 1] != target - 1)) return null;
            }

            var replacedLast = target + count >= lines.Count;
            var lastTerminator = count > 0 ? terminators[target + count - 1] : string.Empty;
            var newTerminators = hunk.ProposedLines.Select(_ => lineEnding).ToList();
            if (replacedLast && newTerminators.Count > 0) newTerminators[^1] = lastTerminator;
            if (count == 0 && target == lines.Count && target > 0 && terminators[target - 1].Length == 0)
                terminators[target - 1] = lineEnding;
            lines.RemoveRange(target, count);
            terminators.RemoveRange(target, count);
            lines.InsertRange(target, hunk.ProposedLines);
            terminators.InsertRange(target, newTerminators);
            if (replacedLast && newTerminators.Count == 0 && target > 0) terminators[target - 1] = lastTerminator;
        }

        var builder = new StringBuilder(original.Length + 64);
        for (var i = 0; i < lines.Count; i++) builder.Append(lines[i]).Append(terminators[i]);
        return builder.ToString();
    }

    /// <summary>Same line model as <see cref="LineDiff"/>: CRLF/LF/CR, a trailing break yields a final empty line.</summary>
    private static (List<string> Lines, List<string> Terminators) SplitWithTerminators(string text)
    {
        var lines = new List<string>();
        var terminators = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var terminator = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
            lines.Add(text[start..i]);
            terminators.Add(terminator);
            i += terminator.Length - 1;
            start = i + 1;
        }
        lines.Add(text[start..]);
        terminators.Add(string.Empty);
        return (lines, terminators);
    }

    // Overlapping matches count too: "aa" in "aaa" is ambiguous.
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0 && count < 2;
             index = index + 1 < text.Length ? text.IndexOf(value, index + 1, StringComparison.Ordinal) : -1)
            count++;
        return count;
    }

    private static async Task<(string? Text, string? Error)> ReadProposalBaseAsync(string fullPath,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists) return (null, ProposalErrors.NotFound);
            if (info.Length > MaximumProposalTextBytes + 4) return (null, ProposalErrors.FileTooLarge);
            await using var stream = new FileStream(fullPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            var buffer = new byte[MaximumProposalTextBytes + 5];
            var read = 0;
            int chunk;
            while (read < buffer.Length &&
                   (chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
                read += chunk;
            if (read > MaximumProposalTextBytes + 4) return (null, ProposalErrors.FileTooLarge);
            bytes = buffer[..read];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, ProposalErrors.NotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              System.Security.SecurityException)
        {
            return (null, ProposalErrors.NotFound);
        }

        try
        {
            var text = bytes switch
            {
                [0xEF, 0xBB, 0xBF, ..] => StrictProposalUtf8.GetString(bytes, 3, bytes.Length - 3),
                [0xFF, 0xFE, ..] => new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2),
                [0xFE, 0xFF, ..] => new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2),
                _ => StrictProposalUtf8.GetString(bytes)
            };
            return text.Contains('\0', StringComparison.Ordinal) ? (null, ProposalErrors.NotText) : (text, null);
        }
        catch (DecoderFallbackException)
        {
            return (null, ProposalErrors.NotText);
        }
    }

    private static bool TryParseProposalArguments(string? json, out string? path, out bool activeBuffer,
        out IReadOnlyList<(string OldText, string NewText)>? edits, out string? newContent)
    {
        path = null;
        activeBuffer = false;
        edits = null;
        newContent = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                switch (property.Name)
                {
                    case "path" when property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 and <= 1024 } value:
                        path = value;
                        break;
                    case "target" when property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() == "active_buffer":
                        activeBuffer = true;
                        break;
                    case "new_content" when property.Value.ValueKind == JsonValueKind.String:
                        newContent = property.Value.GetString();
                        break;
                    case "edits" when property.Value.ValueKind == JsonValueKind.Array &&
                        property.Value.GetArrayLength() is >= 1 and <= MaximumProposalEdits:
                        var list = new List<(string, string)>();
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (!TryParseEdit(item, out var edit)) return false;
                            list.Add(edit);
                        }
                        edits = list;
                        break;
                    default:
                        return false;
                }
            }
            return (path is not null) != activeBuffer && (edits is null) != (newContent is null);
        }
        catch (JsonException) { return false; }

        static bool TryParseEdit(JsonElement item, out (string, string) edit)
        {
            edit = default;
            if (item.ValueKind != JsonValueKind.Object) return false;
            string? oldText = null, newText = null;
            var count = 0;
            foreach (var property in item.EnumerateObject())
            {
                count++;
                if (property.NameEquals("old_text") && oldText is null && property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is { Length: > 0 } value)
                    oldText = value;
                else if (property.NameEquals("new_text") && newText is null && property.Value.ValueKind == JsonValueKind.String)
                    newText = property.Value.GetString();
                else return false;
            }
            if (count != 2 || oldText is null || newText is null) return false;
            edit = (oldText, newText);
            return true;
        }
    }

    private sealed record ProposalReceipt(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("proposalId")] Guid ProposalId,
        [property: JsonPropertyName("added")] int Added,
        [property: JsonPropertyName("removed")] int Removed,
        [property: JsonPropertyName("hunks")] int Hunks);
}
