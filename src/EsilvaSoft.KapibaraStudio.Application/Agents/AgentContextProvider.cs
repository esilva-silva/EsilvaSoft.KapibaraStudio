using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Production context capture of one turn. It works only on the immutable request the tab captured before awaiting,
/// shares exactly the fields the caller included (consent is expressed by what is present), and publishes the
/// connection only by its logical ID after confirming that ID belongs to a registered profile. Profile fields (name,
/// URI, stored credentials) and query results are never read into the snapshot. Editor text the user chose to share is
/// different: it is forwarded after a <b>best-effort</b> redaction of recognizable secrets (MongoDB URIs, also JSON- or
/// URL-escaped; password/pwd/secret/token/API-key values in JSON or assignments; <c>.auth(...)</c> arguments; common
/// API-key, bearer, JWT and private-key formats). Redaction cannot recognize every secret a user may type, so it is a
/// safety net, not a guarantee. The size limit is enforced on the redacted text, since a marker may be longer than
/// what it replaces. Namespace parts without their parent are dropped, never guessed. Failures are visible as
/// <see cref="AgentRuntimeException"/> with a safe code.
/// </summary>
public sealed partial class AgentContextProvider : IAgentContextProvider
{
    /// <summary>Upper bound for the shared editor text (selection plus editor content).</summary>
    public const int MaximumSharedTextChars = 32 * 1024;

    private const int MaximumNameChars = 255;
    private const int MaximumTabIdChars = 128;
    internal const string RedactedConnectionString = "[connection string removida]";
    internal const string RedactedSecret = "[segredo removido]";

    private readonly IConnectionProfileRepository _profiles;
    private readonly TimeProvider _time;

    public AgentContextProvider(IConnectionProfileRepository profiles, TimeProvider? time = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _time = time ?? TimeProvider.System;
    }

    public async Task<AgentContextSnapshot> CaptureAsync(AgentContextCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Every field is read once, here, before the only await; nothing mutable is consulted afterwards.
        var tabId = request.TabId;
        var version = request.DocumentVersion;
        var connectionText = request.ConnectionId;
        var database = request.DatabaseName;
        var collection = request.CollectionName;
        var selection = request.SelectedText;
        var editor = request.EditorText;

        if (string.IsNullOrWhiteSpace(tabId) || tabId.Length > MaximumTabIdChars || tabId.Any(char.IsControl) || version < 0)
        {
            throw new AgentRuntimeException("ContextInvalid", "Context request is invalid.");
        }

        // Input bound (also caps the redaction work); the shared limit is checked again after redaction below.
        if ((selection?.Length ?? 0) + (editor?.Length ?? 0) > MaximumSharedTextChars)
        {
            throw new AgentRuntimeException("ContextTooLarge", "Shared text exceeds the context limit.");
        }

        selection = Redact(selection);
        editor = Redact(editor);
        if ((selection?.Length ?? 0) + (editor?.Length ?? 0) > MaximumSharedTextChars)
        {
            throw new AgentRuntimeException("ContextTooLarge", "Shared text exceeds the context limit.");
        }

        Guid? connectionId = null;
        if (!string.IsNullOrWhiteSpace(connectionText))
        {
            // Only a logical ID may leave the tab; anything else (for example a URI) is refused, not forwarded.
            if (!Guid.TryParse(connectionText, out var parsed) || parsed == Guid.Empty)
            {
                throw new AgentRuntimeException("ContextConnectionInvalid", "Connection reference is invalid.");
            }

            connectionId = parsed;
        }

        database = connectionId is null ? null : NormalizeName(database);
        collection = database is null ? null : NormalizeName(collection);

        if (connectionId is { } id && !await IsRegisteredAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new AgentRuntimeException("ContextConnectionUnknown", "Connection is not registered.");
        }

        var connection = connectionId?.ToString("D", CultureInfo.InvariantCulture);
        return new AgentContextSnapshot(tabId, version, _time.GetUtcNow(), connection, database, collection,
            BuildAuthorizedContext(connection, database, collection, selection, editor));
    }

    private async Task<bool> IsRegisteredAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Core.ConnectionProfile> profiles;
        try
        {
            profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AgentRuntimeException("ContextUnavailable", "Connection profiles are unavailable.");
        }

        // Only the ID is compared; no other profile field is read or copied.
        return profiles is not null && profiles.Any(profile => profile is not null && profile.Id == connectionId);
    }

    private static string? NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameChars || name.Any(char.IsControl) ? null : name;

    /// <summary>Shared best-effort secret redaction (also used by the attachment resolver and the prompt builder).</summary>
    /// <exception cref="AgentRuntimeException">Code <c>ContextRedactionFailed</c> when redaction times out.</exception>
    internal static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            // Order matters: whole blocks and URIs first, then key/value pairs, then free-standing token formats.
            text = PrivateKeyBlockPattern().Replace(text, RedactedSecret);
            text = ConnectionStringPattern().Replace(text, RedactedConnectionString);
            text = AuthCallPattern().Replace(text, ".auth(" + RedactedSecret + ")");
            text = SecretAssignmentPattern().Replace(text, match => match.Groups["key"].Value + RedactedSecret);
            return KnownTokenPattern().Replace(text, RedactedSecret);
        }
        catch (RegexMatchTimeoutException)
        {
            // Unredacted text is never shared: fail closed.
            throw new AgentRuntimeException("ContextRedactionFailed", "Shared text could not be sanitized.");
        }
    }

    private static string? BuildAuthorizedContext(
        string? connection, string? database, string? collection, string? selection, string? editor)
    {
        var builder = new StringBuilder();
        if (connection is not null)
        {
            builder.Append("Conexão (ID lógico): ").Append(connection).Append('\n');
        }

        if (database is not null)
        {
            builder.Append("Banco: ").Append(database).Append('\n');
        }

        if (collection is not null)
        {
            builder.Append("Coleção: ").Append(collection).Append('\n');
        }

        if (selection is not null)
        {
            AppendBlock(builder, "Seleção do editor:", selection);
        }

        if (editor is not null)
        {
            AppendBlock(builder, "Conteúdo do editor:", editor);
        }

        return builder.Length == 0 ? null : builder.ToString().TrimEnd('\n');
    }

    private static void AppendBlock(StringBuilder builder, string label, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(label).Append('\n').Append(text).Append('\n');
    }

    // mongodb:// and mongodb+srv://, also with JSON-escaped slashes (mongodb:\/\/) or URL-encoded (mongodb%3A%2F%2F).
    [GeneratedRegex(@"mongodb(?:\+|%2B)?(?:srv)?(?::|%3A)(?:\\?/|%2F){2}[^\s""'`<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConnectionStringPattern();

    // Arguments of db.auth(...) / x.auth(...): user and password are positional or an object.
    [GeneratedRegex(@"\.\s*auth\s*\([^)]*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AuthCallPattern();

    // "password": "x", pwd: 'x', password=x, apiKey: x, client_secret=..., access_token: ... (JSON, JS, env, query).
    [GeneratedRegex(
        @"(?<key>(?<![A-Za-z0-9])(?:password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?key|secret[_-]?key|private[_-]?key|access[_-]?token|auth[_-]?token|refresh[_-]?token|token)(?:\\?[""'])?\s*[:=]\s*)(?<value>""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`|[^\s,;}&)\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SecretAssignmentPattern();

    // Recognizable credential formats: OpenAI/Anthropic sk-, GitHub, AWS access key, Google API key, Slack, bearer, JWT.
    [GeneratedRegex(
        @"\bsk-[A-Za-z0-9_-]{16,}|\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{22,})|\b(?:AKIA|ASIA)[0-9A-Z]{16}\b|\bAIza[0-9A-Za-z_-]{35}|\bxox[abprs]-[A-Za-z0-9-]{10,}|\bBearer\s+[A-Za-z0-9._~+/=-]{16,}|\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex KnownTokenPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z0-9 ]*PRIVATE KEY-----|$)",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PrivateKeyBlockPattern();
}
