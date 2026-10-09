using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Creates a database user without persisting its password or role payload locally.</summary>
public sealed record DatabaseUserCreateRequest(
    string Database,
    string Username,
    string Password,
    string RolesJson,
    string ConfirmationUsername)
{
    public DatabaseUserCreateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Username) || Username.Trim().Length > 128)
        {
            throw new ArgumentException("O nome do usuário é obrigatório e limitado a 128 caracteres.", nameof(Username));
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length > 1024)
        {
            throw new ArgumentException("A senha é obrigatória e limitada a 1024 caracteres.", nameof(Password));
        }

        if (!string.Equals(Username.Trim(), ConfirmationUsername?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato do usuário para confirmar a criação.", nameof(ConfirmationUsername));
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
                || roles.RootElement.EnumerateArray().Any(role => !IsRoleDocument(role))
                || HasDuplicateRoles(roles.RootElement))
            {
                throw new ArgumentException("Os papéis precisam ser um array JSON não vazio sem duplicatas e com role e db em cada item.", nameof(RolesJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Os papéis não contêm JSON válido.", nameof(RolesJson), exception);
        }

        return this;
    }

    private static bool IsRoleDocument(JsonElement role) =>
        role.ValueKind == JsonValueKind.Object
        && role.TryGetProperty("role", out var roleName)
        && roleName.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(roleName.GetString())
        && role.TryGetProperty("db", out var database)
        && database.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(database.GetString());

    private static bool HasDuplicateRoles(JsonElement roles)
    {
        var identities = roles.EnumerateArray().Select(role =>
            role.GetProperty("db").GetString() + "\0" + role.GetProperty("role").GetString()).ToArray();
        return identities.Distinct(StringComparer.Ordinal).Count() != identities.Length;
    }
}
