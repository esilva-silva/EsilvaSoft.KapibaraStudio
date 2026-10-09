namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires an explicit collection confirmation before a potentially expensive server integrity check.</summary>
public sealed record CollectionIntegrityCheckRequest(
    string Database,
    string Collection,
    string ConfirmationName,
    CollectionMaintenancePreflight? Preflight = null)
{
    public CollectionIntegrityCheckRequest Validate()
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
            throw new ArgumentException("Coleções de sistema não podem ser validadas pela interface.", nameof(Collection));
        }

        if (!string.Equals(Collection, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato da coleção para confirmar a validação.", nameof(ConfirmationName));
        }

        (Preflight ?? throw new ArgumentException("A prévia obrigatória da manutenção não foi informada.", nameof(Preflight)))
            .Validate(Database, Collection);

        return this;
    }
}
