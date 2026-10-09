using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public enum DatabaseRoleMutationKind
{
    Create,
    Update,
    Drop
}

/// <summary>Describes a custom role mutation, including the role definition captured by the preview.</summary>
public sealed record DatabaseRoleMutationRequest(
    string Database,
    string RoleName,
    DatabaseRoleMutationKind Kind,
    string PrivilegesJson,
    string InheritedRolesJson,
    string ConfirmationRoleName,
    string ExpectedRoleDefinitionJson)
{
    public DatabaseRoleMutationRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database) || Database.Trim().Length > 128)
            throw new ArgumentException("O banco de dados é obrigatório e limitado a 128 caracteres.", nameof(Database));

        if (string.IsNullOrWhiteSpace(RoleName) || RoleName.Trim().Length > 128)
            throw new ArgumentException("O nome do papel é obrigatório e limitado a 128 caracteres.", nameof(RoleName));

        if (!string.Equals(RoleName.Trim(), ConfirmationRoleName?.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Digite o nome exato do papel para confirmar a alteração.", nameof(ConfirmationRoleName));

        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));

        ValidateExpectedDefinition();
        using (var expected = JsonDocument.Parse(ExpectedRoleDefinitionJson))
        {
            var roleExists = expected.RootElement.ValueKind == JsonValueKind.Object;
            if ((Kind == DatabaseRoleMutationKind.Create && roleExists)
                || ((Kind is DatabaseRoleMutationKind.Update or DatabaseRoleMutationKind.Drop) && !roleExists))
            {
                throw new ArgumentException("A existência capturada do papel não corresponde à operação solicitada.", nameof(ExpectedRoleDefinitionJson));
            }
        }
        if (Kind == DatabaseRoleMutationKind.Drop)
            return this;

        ValidatePrivileges();
        ValidateInheritedRoles();
        return this;
    }

    private void ValidateExpectedDefinition()
    {
        if (string.IsNullOrWhiteSpace(ExpectedRoleDefinitionJson) || ExpectedRoleDefinitionJson.Length > 64 * 1024)
            throw new ArgumentException("A definição capturada na prévia é obrigatória e limitada a 64 KiB.", nameof(ExpectedRoleDefinitionJson));

        try
        {
            using var document = JsonDocument.Parse(ExpectedRoleDefinitionJson);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                throw new ArgumentException("A definição capturada precisa ser um objeto ou null.", nameof(ExpectedRoleDefinitionJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A definição capturada não contém JSON válido.", nameof(ExpectedRoleDefinitionJson), exception);
        }
    }

    private void ValidatePrivileges()
    {
        using var privileges = ParseArray(PrivilegesJson, nameof(PrivilegesJson), "Os privilégios");
        foreach (var privilege in privileges.RootElement.EnumerateArray())
        {
            if (privilege.ValueKind != JsonValueKind.Object
                || !HasOnlyProperties(privilege, "resource", "actions")
                || !privilege.TryGetProperty("resource", out var resource)
                || resource.ValueKind != JsonValueKind.Object
                || !HasOnlyProperties(resource, "db", "collection")
                || !resource.TryGetProperty("db", out var database)
                || database.ValueKind != JsonValueKind.String
                || !string.Equals(database.GetString(), Database.Trim(), StringComparison.Ordinal)
                || !resource.TryGetProperty("collection", out var collection)
                || collection.ValueKind != JsonValueKind.String
                || !privilege.TryGetProperty("actions", out var actions)
                || actions.ValueKind != JsonValueKind.Array
                || actions.GetArrayLength() == 0
                || actions.EnumerateArray().Any(action => action.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(action.GetString())))
            {
                throw new ArgumentException("Cada privilégio deve conter resource.db igual ao banco selecionado, collection textual e actions com nomes não vazios.", nameof(PrivilegesJson));
            }
        }
    }

    private void ValidateInheritedRoles()
    {
        using var roles = ParseArray(InheritedRolesJson, nameof(InheritedRolesJson), "Os papéis herdados");
        foreach (var role in roles.RootElement.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.Object
                || !HasOnlyProperties(role, "role", "db")
                || !role.TryGetProperty("role", out var name)
                || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString())
                || !role.TryGetProperty("db", out var database)
                || database.ValueKind != JsonValueKind.String
                || !string.Equals(database.GetString(), Database.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Cada papel herdado deve identificar role e db no banco selecionado.", nameof(InheritedRolesJson));
            }
        }
    }

    private static JsonDocument ParseArray(string json, string parameterName, string label)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32 * 1024)
            throw new ArgumentException($"{label} são obrigatórios e limitados a 32 KiB.", parameterName);

        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                document.Dispose();
                throw new ArgumentException($"{label} precisam ser um array JSON.", parameterName);
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"{label} não contêm JSON válido.", parameterName, exception);
        }
    }

    private static bool HasOnlyProperties(JsonElement element, string firstName, string secondName) =>
        element.EnumerateObject().All(property => property.NameEquals(firstName) || property.NameEquals(secondName));
}

public sealed record DatabaseRoleMutationResult(string? Before, string? After);

public enum MongoRoleAdministrationFailureKind
{
    PermissionDenied,
    Unsupported,
    ReadbackFailed,
    InvalidInput,
    CommandFailed
}

/// <summary>A sanitized, classified server refusal from a custom-role command.</summary>
public sealed class MongoRoleAdministrationException(MongoRoleAdministrationFailureKind kind) : Exception
{
    public MongoRoleAdministrationFailureKind Kind { get; } = kind;
}
