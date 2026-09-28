namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record QueryHistoryEntry(
    Guid Id,
    Guid? ProfileId,
    string Database,
    string Collection,
    string FilterJson,
    string? ProjectionJson,
    string? SortJson,
    string? HintJson,
    int Limit,
    int Skip,
    int? MaxTimeMs,
    DateTimeOffset ExecutedAt,
    string? Comment = null,
    int? BatchSize = null,
    string? CollationJson = null)
{
    public string DisplayText => $"{ExecutedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Database}.{Collection}";

    public QueryHistoryEntry Validate()
    {
        _ = new MongoQuery(Database, Collection, FilterJson, ProjectionJson, SortJson, Limit, Skip, HintJson, MaxTimeMs, Comment, BatchSize, CollationJson).Validate();

        if (Id == Guid.Empty)
        {
            throw new ArgumentException("O identificador do histórico é obrigatório.", nameof(Id));
        }

        if (ExecutedAt == default)
        {
            throw new ArgumentException("A data de execução do histórico é obrigatória.", nameof(ExecutedAt));
        }

        return this;
    }

    public static QueryHistoryEntry Create(Guid? profileId, MongoQuery query, DateTimeOffset? executedAt = null)
    {
        query.Validate();
        return new QueryHistoryEntry(
            Guid.NewGuid(),
            profileId,
            query.Database,
            query.Collection,
            query.FilterJson,
            query.ProjectionJson,
            query.SortJson,
            query.HintJson,
            query.Limit,
            query.Skip,
            query.MaxTimeMs,
            executedAt ?? DateTimeOffset.UtcNow,
            query.Comment,
            query.BatchSize,
            query.CollationJson);
    }

    public MongoQuery ToQuery() => new(Database, Collection, FilterJson, ProjectionJson, SortJson, Limit, Skip, HintJson, MaxTimeMs, Comment, BatchSize, CollationJson);
}
