namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires confirmation before requesting MongoDB collection compaction.</summary>
public sealed record CollectionCompactRequest(
    string Database,
    string Collection,
    string ConfirmationName,
    bool Force = false,
    CollectionMaintenancePreflight? Preflight = null)
{
    public CollectionCompactRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (Collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Coleções de sistema não podem ser compactadas pela interface.", nameof(Collection));
        }

        if (!string.Equals(Collection, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato da coleção para confirmar a compactação.", nameof(ConfirmationName));
        }

        (Preflight ?? throw new ArgumentException("A prévia obrigatória da manutenção não foi informada.", nameof(Preflight)))
            .Validate(Database, Collection);

        return this;
    }
}
