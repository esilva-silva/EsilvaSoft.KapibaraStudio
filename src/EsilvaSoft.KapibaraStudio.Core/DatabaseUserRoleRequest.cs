using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes a grant or revoke operation for a database user's roles.</summary>
public sealed record DatabaseUserRoleRequest(
    string Database,
    string Username,
    string RolesJson,
    string ConfirmationUsername,
    bool Revoke,
    string? ExpectedRolesJson = null)
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

        if (string.IsNullOrWhiteSpace(ExpectedRolesJson) || ExpectedRolesJson.Length > 32 * 1024)
        {
            throw new ArgumentException("A prévia atual dos papéis é obrigatória e limitada a 32 KiB.", nameof(ExpectedRolesJson));
        }

        try
        {
            using var roles = JsonDocument.Parse(RolesJson);
            if (roles.RootElement.ValueKind != JsonValueKind.Array
                || roles.RootElement.GetArrayLength() == 0
                || roles.RootElement.EnumerateArray().Any(role => !IsRoleDocument(role))
                || HasDuplicateRoles(roles.RootElement))
            {
                throw new ArgumentException("Os papéis precisam ser um array JSON não vazio com role e db em cada item.", nameof(RolesJson));
            }

            using var expected = JsonDocument.Parse(ExpectedRolesJson);
            if (expected.RootElement.ValueKind != JsonValueKind.Array
                || expected.RootElement.EnumerateArray().Any(role => !IsRoleDocument(role))
                || HasDuplicateRoles(expected.RootElement))
                throw new ArgumentException("A prévia precisa ser um array JSON de papéis com role e db.", nameof(ExpectedRolesJson));
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
            role.GetProperty("db").GetString() + "\0" + role.GetProperty("role").GetString());
        var all = identities.ToArray();
        return all.Distinct(StringComparer.Ordinal).Count() != all.Length;
    }
}

/// <summary>Classifies a user administration command whose resulting state could not be verified.</summary>
public enum MongoUserAdministrationFailureKind
{
    ReadbackFailed,
    PermissionDenied,
    Unsupported,
    InvalidInput,
    Conflict,
    CommandFailed
}

/// <summary>Sanitized status for a user command whose resulting state could not be verified.</summary>
public sealed class MongoUserAdministrationException(MongoUserAdministrationFailureKind kind) : Exception
{
    public MongoUserAdministrationFailureKind Kind { get; } = kind;
}
