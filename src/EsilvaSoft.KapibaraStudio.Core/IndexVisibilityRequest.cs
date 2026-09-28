namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record IndexVisibilityRequest(string Database, string Collection, string Name, string ConfirmationName, bool IsHidden)
{
    public IndexVisibilityRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(Collection) || string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Banco, coleção e índice são obrigatórios.");
        }

        if (string.Equals(Name.Trim(), "_id_", StringComparison.Ordinal))
        {
            throw new ArgumentException("O índice obrigatório _id_ não pode ter sua visibilidade alterada.", nameof(Name));
        }

        if (!string.Equals(Name.Trim(), ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato do índice para confirmar a alteração.", nameof(ConfirmationName));
        }

        return this;
    }
}
