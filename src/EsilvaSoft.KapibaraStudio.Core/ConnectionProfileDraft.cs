namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Dados extraídos localmente de uma URI MongoDB para iniciar o cadastro de um perfil.
/// </summary>
public sealed record ConnectionProfileDraft(string ConnectionString, string SuggestedName, string? DefaultDatabase)
{
    /// <summary>
    /// Extrai o banco e um nome sugerido sem abrir conexão com o servidor.
    /// </summary>
    public static ConnectionProfileDraft FromConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Informe uma string de conexão MongoDB.", nameof(connectionString));
        }

        var normalized = connectionString.Trim();
        var schemeSeparator = normalized.IndexOf("://", StringComparison.Ordinal);
        var scheme = schemeSeparator < 0 ? string.Empty : normalized[..schemeSeparator];
        if (!string.Equals(scheme, "mongodb", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, "mongodb+srv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A string de conexão deve iniciar com mongodb:// ou mongodb+srv://.", nameof(connectionString));
        }

        var authorityStart = schemeSeparator + 3;
        var authorityEnd = normalized.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = normalized[authorityStart..(authorityEnd < 0 ? normalized.Length : authorityEnd)];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new ArgumentException("A string de conexão deve informar ao menos um servidor MongoDB.", nameof(connectionString));
        }

        var hosts = authority[(authority.LastIndexOf('@') + 1)..];
        var firstHost = hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstHost))
        {
            throw new ArgumentException("A string de conexão deve informar ao menos um servidor MongoDB.", nameof(connectionString));
        }

        var database = ExtractDatabase(normalized, authorityEnd);
        var suggestedName = database ?? ExtractHostName(firstHost);
        return new ConnectionProfileDraft(normalized, suggestedName, database);
    }

    private static string? ExtractDatabase(string connectionString, int authorityEnd)
    {
        if (authorityEnd < 0 || connectionString[authorityEnd] != '/')
        {
            return null;
        }

        var pathEnd = connectionString.IndexOfAny(['?', '#'], authorityEnd + 1);
        var encodedDatabase = connectionString[(authorityEnd + 1)..(pathEnd < 0 ? connectionString.Length : pathEnd)].Trim('/');
        return string.IsNullOrWhiteSpace(encodedDatabase) ? null : Uri.UnescapeDataString(encodedDatabase);
    }

    private static string ExtractHostName(string host)
    {
        if (host.Length > 0 && host[0] == '[')
        {
            var closingBracket = host.IndexOf(']');
            return closingBracket > 1 ? host[1..closingBracket] : host;
        }

        var portSeparator = host.LastIndexOf(':');
        return portSeparator > 0 ? host[..portSeparator] : host;
    }
}
