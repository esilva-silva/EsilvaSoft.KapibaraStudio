using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes a grant or revoke operation for a database user's roles.</summary>
public sealed record DatabaseUserRoleRequest(
    string Database,
    string Username,
    string RolesJson,
    string ConfirmationUsername,
    bool Revoke)
{
    public DatabaseUserRoleRequest Validate()
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
            throw new ArgumentException("Digite o nome exato do usuário para confirmar a alteração.", nameof(ConfirmationUsername));
        }

        if (string.IsNullOrWhiteSpace(RolesJson) || RolesJson.Length > 32 * 1024)
        {
            throw new ArgumentException("Os papéis são obrigatórios e limitados a 32 KiB.", nameof(RolesJson));
        }

        try
        {
            using var roles = JsonDocument.Parse(RolesJson);
            if (roles.RootElement.ValueKind != JsonValueKind.Array
                || roles.RootElement.GetArrayLength() == 0
                || roles.RootElement.EnumerateArray().Any(role => role.ValueKind != JsonValueKind.Object))
            {
                throw new ArgumentException("Os papéis precisam ser um array JSON não vazio de documentos.", nameof(RolesJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Os papéis não contêm JSON válido.", nameof(RolesJson), exception);
        }

        return this;
    }
}
