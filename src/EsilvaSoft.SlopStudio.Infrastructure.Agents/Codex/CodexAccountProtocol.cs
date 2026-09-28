using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Codex;

/// <summary>
/// Pure JSON-RPC builders and allowlisted account parsers for the Codex App Server account surface.
/// This type never launches a process, opens a browser, or accepts, returns, or stores credentials.
/// </summary>
internal static class CodexAccountProtocol
{
    internal const string LoginStartMethod = "account/login/start";
    internal const string AccountReadMethod = "account/read";
    internal const string LoginCancelMethod = "account/login/cancel";
    internal const string LogoutMethod = "account/logout";

    private const int MaximumPayloadBytes = 32 * 1024;
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 8 };

    private static readonly HashSet<string> KnownPlanTypes = new(StringComparer.Ordinal)
    {
        "free",
        "go",
        "plus",
        "pro",
        "prolite",
        "team",
        "self_serve_business_prolite",
        "self_serve_business_usage_based",
        "business",
        "ent26",
        "enterprise_cbp_automation",
        "enterprise_cbp_usage_based",
        "enterprise",
        "edu",
        "edu_plus",
        "edu_pro",
        "unknown",
    };

    /// <summary>Creates the only login request this helper permits: Codex-managed ChatGPT browser login.</summary>
    internal static byte[] BuildLoginStartRequest(long requestId)
    {
        ValidateRequestId(requestId);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", LoginStartMethod);
            writer.WriteNumber("id", requestId);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteString("type", "chatgpt");
            writer.WriteBoolean("useHostedLoginSuccessPage", true);
            writer.WriteString("appBrand", "codex");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Creates an account check that never asks the runtime to refresh its token.</summary>
    internal static byte[] BuildAccountReadRequest(long requestId)
    {
        ValidateRequestId(requestId);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", AccountReadMethod);
            writer.WriteNumber("id", requestId);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteBoolean("refreshToken", false);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Creates a cancellation for a pending managed ChatGPT login.</summary>
    internal static byte[] BuildLoginCancelRequest(long requestId, string loginId)
    {
        ValidateRequestId(requestId);
        var canonicalLoginId = CanonicalLoginId(loginId);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", LoginCancelMethod);
            writer.WriteNumber("id", requestId);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteString("loginId", canonicalLoginId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Creates a Codex-managed global logout request. The caller must obtain user confirmation.</summary>
    internal static byte[] BuildLogoutRequest(long requestId)
    {
        ValidateRequestId(requestId);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", LogoutMethod);
            writer.WriteNumber("id", requestId);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Parses the documented <c>account/read</c> JSON-RPC result. Only a ChatGPT-managed account with OpenAI auth
    /// required is accepted. Email is validated but intentionally discarded; only the plan token crosses this port.
    /// </summary>
    internal static bool TryParseAccountReadResponse(ReadOnlySpan<byte> utf8Json, out CodexChatGptAccount? account)
    {
        account = null;
        if (!TryParseDocument(utf8Json, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!HasOnlyUniqueProperties(root, "id", "result") ||
                !TryGetUniqueProperty(root, "result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !HasOnlyUniqueProperties(result, "account", "requiresOpenaiAuth") ||
                !TryGetUniqueProperty(result, "requiresOpenaiAuth", out var requiresAuth) ||
                requiresAuth.ValueKind != JsonValueKind.True ||
                !TryGetUniqueProperty(result, "account", out var accountElement) ||
                accountElement.ValueKind != JsonValueKind.Object ||
                !HasOnlyUniqueProperties(accountElement, "type", "email", "planType") ||
                !TryGetUniqueProperty(accountElement, "type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "chatgpt" ||
                !TryGetUniqueProperty(accountElement, "email", out var email) ||
                email.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                !TryGetUniqueProperty(accountElement, "planType", out var planType) ||
                !TryGetPlanType(planType, allowNull: false, out var safePlanType))
            {
                return false;
            }

            account = new CodexChatGptAccount(safePlanType);
            return true;
        }
    }

    /// <summary>
    /// Parses the documented <c>account/updated</c> notification params. Only the managed ChatGPT auth mode is
    /// accepted; all identifiers, emails, tokens, and other account details are intentionally ignored.
    /// </summary>
    internal static bool TryParseAccountUpdatedParams(ReadOnlySpan<byte> utf8Json, out CodexChatGptAccount? account)
    {
        account = null;
        if (!TryParseDocument(utf8Json, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasOnlyUniqueProperties(root, "authMode", "planType") ||
                !TryGetUniqueProperty(root, "authMode", out var authMode) ||
                authMode.ValueKind != JsonValueKind.String || authMode.GetString() != "chatgpt")
            {
                return false;
            }

            string? safePlanType = null;
            if (TryGetUniqueProperty(root, "planType", out var planType) &&
                !TryGetPlanType(planType, allowNull: true, out safePlanType))
            {
                return false;
            }

            account = new CodexChatGptAccount(safePlanType);
            return true;
        }
    }

    private static bool TryParseDocument(ReadOnlySpan<byte> utf8Json, out JsonDocument document)
    {
        document = null!;
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumPayloadBytes)
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray(), DocumentOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasOnlyUniqueProperties(JsonElement element, params string[] allowedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetUniqueProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                value = default;
                return false;
            }

            value = property.Value;
            found = true;
        }

        return found;
    }

    private static bool TryGetPlanType(JsonElement value, bool allowNull, out string? planType)
    {
        planType = null;
        if (allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } candidate ||
            !KnownPlanTypes.Contains(candidate))
        {
            return false;
        }

        planType = candidate;
        return true;
    }

    private static string CanonicalLoginId(string loginId)
    {
        if (!Guid.TryParse(loginId, out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException("Codex login ID is invalid.", nameof(loginId));
        }

        return parsed.ToString("D");
    }

    private static void ValidateRequestId(long requestId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestId);
    }
}

/// <summary>Allowlisted account data safe for local presentation; no identity or credential is retained.</summary>
internal sealed record CodexChatGptAccount(string? PlanType);
