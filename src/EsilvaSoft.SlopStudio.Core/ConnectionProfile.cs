using System.Text.RegularExpressions;

namespace EsilvaSoft.SlopStudio.Core;

public sealed record ConnectionProfile(
    Guid Id,
    string Name,
    string ConnectionString,
    string? DefaultDatabase = null,
    string? Environment = null,
    string? Color = null,
    string? Tags = null,
    bool IsReadOnly = false,
    bool IsFavorite = false,
    DateTimeOffset? LastConnectedAt = null,
    string? Folder = null)
{
    /// <summary>Per-connection opt-out from including tab context in local AI chat requests.</summary>
    public bool LocalAiContextEnabled { get; init; } = true;

    /// <summary>Explicit runtime target; never written into the saved connection URI.</summary>
    public string? TargetHost { get; init; }

    /// <summary>
    /// Durable, opaque marker of the data origin this profile currently points to. Renewed only by the
    /// repository (never by a ViewModel or per-process) when <see cref="ConnectionString"/>,
    /// <see cref="TargetHost"/> or <see cref="Environment"/> differs from the stored document; never derived
    /// from a hash of the connection string, which would keep credential-derived material at rest. Null until
    /// the profile has been persisted for the first time.
    /// </summary>
    public Guid? SourceGenerationId { get; init; }
    /// <summary>Opaque reference to a complete connection URI held by the operating-system secret store.</summary>
    public SecretReference? SecretReference { get; init; }
    public string RoutingLabel => TargetHost is not null ? "Instância explícita: " + TargetHost : Regex.IsMatch(ConnectionString, @"[?&]directConnection=true(?:&|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "Instância configurada na URI: " + Endpoint : "Seleção automática do driver";
    public string Endpoint
    {
        get
        {
            var start = ConnectionString.IndexOf("://", StringComparison.Ordinal);
            if (start < 0) return "URI não reconhecida";
            var address = ConnectionString[(start + 3)..];
            var end = address.IndexOfAny(['/', '?', '#']);
            var authority = end < 0 ? address : address[..end];
            var at = authority.LastIndexOf('@');
            if (at >= 0) return authority[(at + 1)..];
            // An unescaped delimiter in userinfo must not turn credentials into a visible host summary.
            if (end >= 0 && authority.Contains(':', StringComparison.Ordinal))
            {
                var remainder = address.AsSpan(end + 1);
                var nextDelimiter = remainder.IndexOfAny('?', '#');
                if (nextDelimiter >= 0) remainder = remainder[..nextDelimiter];
                if (remainder.Contains('@')) return "Confira a URI configurada";
            }
            return authority;
        }
    }
    private static readonly Regex EnvironmentToken = new("\\$\\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ConnectionProfile Create(string name, string connectionString, string? defaultDatabase = null, bool isReadOnly = false, bool isFavorite = false, string? environment = null, string? color = null, string? tags = null, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome da conexão é obrigatório.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A string de conexão é obrigatória.", nameof(connectionString));
        }

        if (!string.IsNullOrWhiteSpace(environment) && environment.Trim().Length > 60)
        {
            throw new ArgumentException("O ambiente deve ter no máximo 60 caracteres.", nameof(environment));
        }

        if (!string.IsNullOrWhiteSpace(color) && !HexColor.IsMatch(color.Trim()))
        {
            throw new ArgumentException("A cor deve usar o formato hexadecimal #RRGGBB.", nameof(color));
        }

        if (!string.IsNullOrWhiteSpace(folder) && folder.Trim().Length > 80)
        {
            throw new ArgumentException("A pasta deve ter no máximo 80 caracteres.", nameof(folder));
        }

        return new ConnectionProfile(Guid.NewGuid(), name.Trim(), connectionString.Trim(), defaultDatabase?.Trim(), environment?.Trim(), color?.Trim(), NormalizeTags(tags), isReadOnly, isFavorite, Folder: NormalizeFolder(folder));
    }

    /// <summary>Resolves environment placeholders such as ${MONGODB_PASSWORD} only at connection time.</summary>
    public string ResolveConnectionString(Func<string, string?>? getEnvironmentVariable = null, Func<string, string>? getVaultValue = null)
    {
        getEnvironmentVariable ??= global::System.Environment.GetEnvironmentVariable;
        var template = DynamicValues.ResolveText(ConnectionString, getVaultValue ?? (key => getEnvironmentVariable(key) ?? throw new InvalidOperationException($"Chave {key} não definida.")), uriEncode: true);
        return EnvironmentToken.Replace(template, match =>
        {
            var variable = match.Groups["name"].Value;
            var value = getEnvironmentVariable(variable);

            return string.IsNullOrEmpty(value)
                ? throw new InvalidOperationException($"A variável de ambiente {variable} não está definida para a conexão {Name}.")
                : value;
        });
    }

    /// <summary>Rejects a mutation when the connection was deliberately configured as read-only.</summary>
    public void EnsureWriteAllowed()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"A conexão {Name} está configurada como somente leitura.");
        }
    }

    public ConnectionProfile Duplicate(string name)
    {
        return Create(name, ConnectionString, DefaultDatabase, IsReadOnly, IsFavorite, Environment, Color, Tags, Folder)
            with { LocalAiContextEnabled = LocalAiContextEnabled, SecretReference = SecretReference };
    }

    /// <summary>Returns the profile with the local timestamp of a successful connection.</summary>
    public ConnectionProfile MarkConnected(DateTimeOffset? occurredAt = null) =>
        this with { LastConnectedAt = occurredAt ?? DateTimeOffset.UtcNow };

    private static string? NormalizeTags(string? tags)
    {
        var values = (tags ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length > 10 || values.Any(value => value.Length > 30))
        {
            throw new ArgumentException("Use no máximo 10 tags, cada uma com até 30 caracteres.", nameof(tags));
        }

        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private static string? NormalizeFolder(string? folder)
    {
        var values = (folder ?? string.Empty)
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Length > 0 ? string.Join(" / ", values) : null;
    }
}
