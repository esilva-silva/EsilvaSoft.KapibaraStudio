using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string SavedQueriesCollectionName = "savedQueries";

    public Task<IReadOnlyList<SavedQuery>> GetAllAsync(Guid? profileId, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var queries = _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName)
                .FindAll()
                .Where(query => query.ProfileId == profileId)
                .OrderByDescending(query => query.IsFavorite)
                .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<SavedQuery>)queries;
        }, cancellationToken);

    public Task SaveAsync(SavedQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate();
        return RunAsync(() => _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName).Upsert(FromDomain(query)), cancellationToken);
    }

    public Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default) =>
        RunAsync(() => _database.GetCollection<SavedQueryDocument>(SavedQueriesCollectionName).Delete(id), cancellationToken);

    private static SavedQuery ToDomain(SavedQueryDocument query) =>
        new(query.Id, query.Name, query.ProfileId, query.Database, query.Collection, query.FilterJson, query.ProjectionJson, query.SortJson, query.HintJson, query.Limit, query.Skip, query.MaxTimeMs, query.IsFavorite, new DateTimeOffset(new DateTime(query.UpdatedAtUtcTicks, DateTimeKind.Utc)), query.Comment, query.BatchSize, query.CollationJson);

    private static SavedQueryDocument FromDomain(SavedQuery query) =>
        new()
        {
            Id = query.Id,
            Name = query.Name,
            ProfileId = query.ProfileId,
            Database = query.Database,
            Collection = query.Collection,
            FilterJson = query.FilterJson,
            ProjectionJson = query.ProjectionJson,
            SortJson = query.SortJson,
            HintJson = query.HintJson,
            Limit = query.Limit,
            Skip = query.Skip,
            MaxTimeMs = query.MaxTimeMs,
            Comment = query.Comment,
            BatchSize = query.BatchSize,
            CollationJson = query.CollationJson,
            IsFavorite = query.IsFavorite,
            UpdatedAtUtcTicks = query.UpdatedAt.UtcDateTime.Ticks
        };

    private sealed class SavedQueryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Guid? ProfileId { get; init; }

        public string Database { get; init; } = string.Empty;

        public string Collection { get; init; } = string.Empty;

        public string FilterJson { get; init; } = "{}";

        public string? ProjectionJson { get; init; }

        public string? SortJson { get; init; }

        public string? HintJson { get; init; }

        public int Limit { get; init; }

        public int Skip { get; init; }

        public int? MaxTimeMs { get; init; }

        public string? Comment { get; init; }

        public int? BatchSize { get; init; }

        public string? CollationJson { get; init; }

        public bool IsFavorite { get; init; }

        public long UpdatedAtUtcTicks { get; init; }
    }
}
