using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Literal, bounded Mongo read used only after agent authorization.</summary>
public interface IAgentMongoFindSource
{
    Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
        CancellationToken cancellationToken);

    Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
        CancellationToken cancellationToken);
}

public sealed record AgentMongoFindQuery(string Database, string Collection, string FilterEjson,
    string? ProjectionEjson, string? SortEjson, int Limit, int Skip, int MaxTimeMs);

public sealed record AgentMongoFindPage(IReadOnlyList<string> DocumentsEjson, bool HasMore,
    bool Truncated, bool TargetVerified, bool ResultTooLarge);

public sealed record AgentMongoFindByIdQuery(string Database, string Collection, string IdEjson,
    int MaxTimeMs);
