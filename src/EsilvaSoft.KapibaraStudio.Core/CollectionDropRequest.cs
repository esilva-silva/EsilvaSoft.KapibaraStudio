namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires a typed collection name before a destructive drop operation.</summary>
public sealed record CollectionDropRequest(string Database, string Collection, string ConfirmationName)
{
    public CollectionDropRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection) || Collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A coleção informada não pode ser removida pela interface.", nameof(Collection));
        }

        if (!string.Equals(Collection, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato da coleção para confirmar a remoção.", nameof(ConfirmationName));
        }

        return this;
    }
}
