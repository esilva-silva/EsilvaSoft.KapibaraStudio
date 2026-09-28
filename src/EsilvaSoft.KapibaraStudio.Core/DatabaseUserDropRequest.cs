namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires exact confirmation before removing a database user.</summary>
public sealed record DatabaseUserDropRequest(string Database, string Username, string ConfirmationUsername)
{
    public DatabaseUserDropRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Username) || Username.Trim().Length > 128)
        {
            throw new ArgumentException("O nome do usuário é obrigatório e limitado a 128 caracteres.", nameof(Username));
        }

        if (!string.Equals(Username.Trim(), ConfirmationUsername?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato do usuário para confirmar a remoção.", nameof(ConfirmationUsername));
        }

        return this;
    }
}
