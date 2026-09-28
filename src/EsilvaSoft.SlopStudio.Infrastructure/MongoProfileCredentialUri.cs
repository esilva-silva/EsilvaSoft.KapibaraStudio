using System.Text.RegularExpressions;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Conservative persistence classification; never includes the URI in an error.</summary>
internal sealed record MongoProfileCredentialUri(string RedactedUri, bool HasLiteralPassword, bool IsUsernameOnly)
{
    private static readonly Regex DynamicPassword = new(
        "^\\$\\{(?:[A-Za-z_][A-Za-z0-9_]*|ENV\\.get\\([\"'][A-Za-z_][A-Za-z0-9_]*[\"']\\))\\}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> SafeTextOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "authSource", "authMechanism", "replicaSet", "appName", "readPreference", "readPreferenceTags",
        "w", "uuidRepresentation", "compressors", "zlibCompressionLevel"
    };
    private static readonly HashSet<string> SafeBooleanOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "retryWrites", "retryReads", "tls", "ssl", "directConnection", "journal", "loadBalanced"
    };
    private static readonly HashSet<string> SafeNumberOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "serverSelectionTimeoutMS", "connectTimeoutMS", "socketTimeoutMS", "maxPoolSize", "minPoolSize",
        "maxIdleTimeMS", "waitQueueTimeoutMS", "heartbeatFrequencyMS", "wTimeoutMS"
    };

    public static MongoProfileCredentialUri Classify(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw Unsafe();
        var start = connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ? 10
            : connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase) ? 14 : -1;
        if (start < 0 || connectionString.IndexOf('#', start) >= 0)
            throw Unsafe();
        var end = connectionString.IndexOfAny(['/', '?'], start);
        if (end < 0) end = connectionString.Length;
        var authority = connectionString.AsSpan(start, end - start);
        if (authority.IsEmpty) throw Unsafe();
        var at = authority.IndexOf('@');
        if (at >= 0 && (at == 0 || authority[(at + 1)..].IsEmpty || authority[(at + 1)..].Contains('@')))
            throw Unsafe();
        var hosts = at < 0 ? authority : authority[(at + 1)..];
        foreach (var host in hosts.ToString().Split(','))
        {
            if (host.Length == 0) throw Unsafe();
            if (host.Length > 0 && host[0] == '[')
            {
                var close = host.IndexOf(']');
                if (close < 0 || (close + 1 < host.Length &&
                    (host[close + 1] != ':' || close + 2 == host.Length ||
                     !host[(close + 2)..].All(char.IsAsciiDigit)))) throw Unsafe();
                continue;
            }
            var hostColon = host.LastIndexOf(':');
            if (hostColon >= 0 && (hostColon == host.Length - 1 ||
                !host[(hostColon + 1)..].All(char.IsAsciiDigit))) throw Unsafe();
        }
        // An unescaped delimiter before '@' can make a password look like path or query data.
        var firstPathOrQuery = connectionString[end..];
        var queryStart = firstPathOrQuery.IndexOf('?');
        var path = queryStart < 0 ? firstPathOrQuery : firstPathOrQuery[..queryStart];
        if (path.Contains('@', StringComparison.Ordinal)) throw Unsafe();
        // Without a literal password nothing is moved to the OS store and the URI is persisted as typed, so
        // only options that themselves carry a secret are refused. The closed allowlist applies only when a
        // literal password is protected (RedactInlinePassword), because the stored URI is then verified against it.
        RejectSecretBearingOptions(queryStart < 0 ? string.Empty : firstPathOrQuery[(queryStart + 1)..]);

        if (at < 0) return new(connectionString, false, false);
        var userInfo = authority[..at];
        var colon = userInfo.IndexOf(':');
        if (colon < 0) return new(connectionString, false, true); // Valid username-only authentication.
        if (colon == 0 || colon == userInfo.Length - 1 || userInfo[(colon + 1)..].Contains(':'))
            throw Unsafe();
        var password = userInfo[(colon + 1)..].ToString();
        if (password.Contains("${", StringComparison.Ordinal) ||
            password.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase))
        {
            if (!DynamicPassword.IsMatch(password)) throw Unsafe();
            return new(connectionString, false, false);
        }
        var redacted = LiteDbConnectionProfileRepository.RedactInlinePassword(connectionString);
        if (redacted is null) throw Unsafe();
        return new(redacted, true, false);
    }

    private static readonly HashSet<string> SecretBearingOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "tlsCertificateKeyFilePassword", "sslPEMKeyPassword", "sslClientCertificateKeyPassword"
    };
    private static readonly Regex DynamicValue = new(
        @"^\$\{[A-Za-z_][A-Za-z0-9_]*\}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Refuses options whose value is a secret (key passwords, any key naming a password/secret/token, or a
    /// literal AWS session token in authMechanismProperties). Environment placeholders stay allowed: they are
    /// resolved only at connection time and never persisted resolved.
    /// </summary>
    private static void RejectSecretBearingOptions(string query)
    {
        if (query.Length == 0) return;
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) throw Unsafe();
            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(pair[..separator]);
                value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            catch (UriFormatException) { throw Unsafe(); }
            if (SecretBearingOptions.Contains(key) ||
                key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("token", StringComparison.OrdinalIgnoreCase))
                throw Unsafe();
            if (!key.Equals("authMechanismProperties", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var property in value.Split(','))
            {
                var colon = property.IndexOf(':');
                var name = colon < 0 ? property : property[..colon];
                if ((name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)) &&
                    (colon < 0 || !DynamicValue.IsMatch(property[(colon + 1)..])))
                    throw Unsafe();
            }
        }
    }

    /// <summary>
    /// Closed allowlist shared by save, legacy migration and connection-time verification. Option values can
    /// carry credentials (for example authMechanismProperties), so every other option keeps the URI ineligible.
    /// </summary>
    internal static bool IsSafeQuery(string query)
    {
        if (query.Length == 0) return true;
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1) return false;
            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(pair[..separator]);
                value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            catch (UriFormatException) { return false; }
            if (SafeBooleanOptions.Contains(key))
            {
                if (value is not ("true" or "false")) return false;
            }
            else if (SafeNumberOptions.Contains(key))
            {
                if (value.Length > 12 || !value.All(char.IsAsciiDigit)) return false;
            }
            else if (SafeTextOptions.Contains(key))
            {
                if (value.Length > 256 || value.Any(char.IsControl) ||
                    value.Contains("${", StringComparison.Ordinal) ||
                    value.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase)) return false;
            }
            else return false;
        }
        return true;
    }

    private static ArgumentException Unsafe() =>
        new("A URI da conexão contém credencial ou opção que não pode ser classificada com segurança. Corrija a URI antes de salvar.");
}
