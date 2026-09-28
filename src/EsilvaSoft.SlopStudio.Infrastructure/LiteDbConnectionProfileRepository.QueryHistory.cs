using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string QueryHistoryCollectionName = "queryHistory";

    public Task<IReadOnlyList<QueryHistoryEntry>> GetRecentAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "O histórico deve conter entre 1 e 200 itens.");
        }

        return RunAsync(() =>
        {
            var query = _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName)
                .Query()
                .OrderByDescending(entry => entry.ExecutedAtUtc);
            if (profileId is not null)
            {
                query = query.Where(entry => entry.ProfileId == profileId);
            }

            return (IReadOnlyList<QueryHistoryEntry>)query
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
        }, cancellationToken);
    }

    public Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() => _database.GetCollection<QueryHistoryDocument>(QueryHistoryCollectionName).Upsert(FromDomain(entry)), cancellationToken);
    }

    // The former SpecifyKind(Utc) relabelled LiteDB's local-time value and shifted the instant by the machine offset.
    private static QueryHistoryEntry ToDomain(QueryHistoryDocument entry) =>
        new(entry.Id, entry.ProfileId, entry.Database, entry.Collection, entry.FilterJson, entry.ProjectionJson, entry.SortJson, entry.HintJson, entry.Limit, entry.Skip, entry.MaxTimeMs, LiteDbDates.ToUtcInstant(entry.ExecutedAtUtc), entry.Comment, entry.BatchSize, entry.CollationJson);

    private static QueryHistoryDocument FromDomain(QueryHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            ProfileId = entry.ProfileId,
            Database = entry.Database,
            Collection = entry.Collection,
            FilterJson = entry.FilterJson,
            ProjectionJson = entry.ProjectionJson,
            SortJson = entry.SortJson,
            HintJson = entry.HintJson,
            Limit = entry.Limit,
            Skip = entry.Skip,
            MaxTimeMs = entry.MaxTimeMs,
            Comment = entry.Comment,
            BatchSize = entry.BatchSize,
            CollationJson = entry.CollationJson,
            ExecutedAtUtc = entry.ExecutedAt.UtcDateTime
        };

    private sealed class QueryHistoryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

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

        public DateTime ExecutedAtUtc { get; init; }
    }
}
