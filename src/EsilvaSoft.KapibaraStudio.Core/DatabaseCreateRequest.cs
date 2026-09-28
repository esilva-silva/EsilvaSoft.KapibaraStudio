namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Creates a database explicitly by creating its required initial user collection.</summary>
public sealed record DatabaseCreateRequest(string Database, string InitialCollection, string ConfirmationName)
{
    private static readonly HashSet<string> ProtectedDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "config",
        "local"
    };

    public DatabaseCreateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O nome do banco de dados é obrigatório.", nameof(Database));
        }

        if (ProtectedDatabases.Contains(Database))
        {
            throw new ArgumentException("Bancos internos não podem ser criados pela interface.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(InitialCollection) || InitialCollection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A coleção inicial precisa ser uma coleção de usuário.", nameof(InitialCollection));
        }

        if (!string.Equals(Database, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato do banco para confirmar sua criação.", nameof(ConfirmationName));
        }

        return this;
    }
}
