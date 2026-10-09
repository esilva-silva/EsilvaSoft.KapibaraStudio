namespace EsilvaSoft.KapibaraStudio.Core;

public enum DatabaseDefinitionKind
{
    CollectionOptions,
    Validator,
    Index,
    View
}

public enum DatabaseDefinitionRestoreStatus
{
    Restored,
    Omitted,
    Failed,
    Blocked
}

public enum DatabaseDefinitionCollision
{
    None,
    Identical,
    Conflicting,
    Unknown
}

/// <summary>
/// Sanitized result for one independently applied definition. SafeMessage must never contain raw server
/// exceptions, credentials, connection strings, document values or command output.
/// </summary>
public sealed record DatabaseDefinitionRestoreItem(
    string Id,
    DatabaseDefinitionKind Kind,
    string Target,
    IReadOnlyList<string> DependsOn,
    DatabaseDefinitionCollision Collision,
    DatabaseDefinitionRestoreStatus Status,
    string? ErrorCode = null,
    string? SafeMessage = null)
{
    public DatabaseDefinitionRestoreItem Validate()
    {
        if (!IsIdentifier(Id) || string.IsNullOrWhiteSpace(Target) || Target.Contains('\0') ||
            Target.StartsWith("system.", StringComparison.OrdinalIgnoreCase) || Target.Contains('.') || Target.Contains('$'))
        {
            throw new ArgumentException("O item de restauração precisa de identificador e destino válidos, fora de namespaces protegidos.");
        }

        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Collision) || !Enum.IsDefined(Status))
        {
            throw new ArgumentException("O item de restauração contém um estado não suportado.");
        }

        ArgumentNullException.ThrowIfNull(DependsOn);
        if (DependsOn.Any(dependency => !IsIdentifier(dependency)) || DependsOn.Contains(Id, StringComparer.Ordinal) ||
            DependsOn.Distinct(StringComparer.Ordinal).Count() != DependsOn.Count)
        {
            throw new ArgumentException("As dependências do item são inválidas.", nameof(DependsOn));
        }

        if (Status is DatabaseDefinitionRestoreStatus.Failed or DatabaseDefinitionRestoreStatus.Blocked &&
            string.IsNullOrWhiteSpace(ErrorCode))
        {
            throw new ArgumentException("Itens com falha ou bloqueio precisam de um código de erro estável.", nameof(ErrorCode));
        }

        if (Status is DatabaseDefinitionRestoreStatus.Restored or DatabaseDefinitionRestoreStatus.Omitted &&
            ErrorCode is not null)
        {
            throw new ArgumentException("Itens restaurados ou omitidos não devem carregar código de falha.", nameof(ErrorCode));
        }

        if (SafeMessage is not null && string.IsNullOrWhiteSpace(ErrorCode))
        {
            throw new ArgumentException("Uma mensagem segura precisa estar associada a um código de erro.", nameof(SafeMessage));
        }

        return this;
    }

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('\0') && !value.Any(char.IsWhiteSpace);
}

/// <summary>Per-definition outcomes, including collisions and dependency failures.</summary>
public sealed record DatabaseDefinitionRestoreReport(IReadOnlyList<DatabaseDefinitionRestoreItem> Items)
{
    public DatabaseDefinitionRestoreReport Validate()
    {
        ArgumentNullException.ThrowIfNull(Items);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            item.Validate();
            if (!ids.Add(item.Id))
            {
                throw new ArgumentException("O relatório contém identificadores de item duplicados.", nameof(Items));
            }
        }

        var byId = Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var item in Items)
        {
            foreach (var dependencyId in item.DependsOn)
            {
                if (!byId.ContainsKey(dependencyId))
                {
                    throw new ArgumentException("O relatório referencia uma dependência ausente.", nameof(Items));
                }
            }
        }

        EnsureAcyclicDependencies(byId);
        return this;
    }

    private static void EnsureAcyclicDependencies(IReadOnlyDictionary<string, DatabaseDefinitionRestoreItem> items)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in items.Keys)
        {
            Visit(id);
        }

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!active.Add(id))
            {
                throw new ArgumentException("O relatório contém dependências cíclicas.", nameof(items));
            }

            foreach (var dependency in items[id].DependsOn)
            {
                Visit(dependency);
            }

            active.Remove(id);
            visited.Add(id);
        }
    }
}
