namespace EsilvaSoft.SlopStudio.Core;

/// <summary>An exact MongoDB namespace, with database and collection represented as distinct fields.</summary>
public sealed record AgentNamespaceScope
{
    private AgentNamespaceScope(Guid connectionId, string? databaseName, string? collectionName)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("A conexão precisa ter um identificador.", nameof(connectionId));
        ValidateName(databaseName, nameof(databaseName));
        ValidateName(collectionName, nameof(collectionName));
        if (collectionName is not null && databaseName is null)
            throw new ArgumentException("Uma coleção exige um banco explícito.", nameof(collectionName));
        ConnectionId = connectionId;
        DatabaseName = databaseName;
        CollectionName = collectionName;
    }

    public Guid ConnectionId { get; }
    public string? DatabaseName { get; }
    public string? CollectionName { get; }

    public static AgentNamespaceScope ForConnection(Guid connectionId) => new(connectionId, null, null);
    public static AgentNamespaceScope ForDatabase(Guid connectionId, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        return new AgentNamespaceScope(connectionId, databaseName, null);
    }

    public static AgentNamespaceScope ForCollection(Guid connectionId, string databaseName, string collectionName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(collectionName);
        return new AgentNamespaceScope(connectionId, databaseName, collectionName);
    }

    /// <summary>Whether this explicitly granted scope contains the requested scope.</summary>
    public bool Covers(AgentNamespaceScope requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (ConnectionId != requested.ConnectionId) return false;
        if (DatabaseName is null) return true;
        if (!string.Equals(DatabaseName, requested.DatabaseName, StringComparison.Ordinal)) return false;
        return CollectionName is null || string.Equals(CollectionName, requested.CollectionName, StringComparison.Ordinal);
    }

    private static void ValidateName(string? value, string parameterName)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("O nome do namespace é inválido.", parameterName);
    }
}
