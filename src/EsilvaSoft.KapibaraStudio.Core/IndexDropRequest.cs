namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record IndexDropRequest(string Database, string Collection, string Name)
{
    public IndexDropRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("O nome do índice é obrigatório.", nameof(Name));
        }

        if (string.Equals(Name.Trim(), "_id_", StringComparison.Ordinal))
        {
            throw new ArgumentException("O índice obrigatório _id_ não pode ser removido.", nameof(Name));
        }

        return this;
    }
}
