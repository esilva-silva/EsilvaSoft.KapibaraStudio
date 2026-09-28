namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires explicit typed confirmation before a database is removed.</summary>
public sealed record DatabaseDropRequest(string Database, string ConfirmationName)
{
    private static readonly HashSet<string> ProtectedDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "config",
        "local"
    };

    public DatabaseDropRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (ProtectedDatabases.Contains(Database))
        {
            throw new ArgumentException("Bancos internos não podem ser removidos pela interface.", nameof(Database));
        }

        if (!string.Equals(Database, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato do banco para confirmar a remoção.", nameof(ConfirmationName));
        }

        return this;
    }
}
