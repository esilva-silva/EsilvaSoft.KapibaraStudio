using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string ScriptHistoryCollectionName = "scriptHistory";

    public Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentAsync(int maximum = 50, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "O histórico deve conter entre 1 e 200 itens.");
        }

        return RunAsync(() =>
        {
            var entries = _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName)
                .Query()
                .OrderByDescending(entry => entry.LastAccessedUtcTicks)
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<ScriptHistoryEntry>)entries;
        }, cancellationToken);
    }

    public Task SaveAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection<ScriptHistoryDocument>(ScriptHistoryCollectionName);
            var existing = collection.FindOne(item => item.Path == entry.Path);
            var id = existing?.Id ?? entry.Id;
            collection.Upsert(FromDomain(entry) with { Id = id });
        }, cancellationToken);
    }

    private static ScriptHistoryEntry ToDomain(ScriptHistoryDocument entry) =>
        new(entry.Id, entry.Path, new DateTimeOffset(new DateTime(entry.LastAccessedUtcTicks, DateTimeKind.Utc)), entry.InputJson);

    private static ScriptHistoryDocument FromDomain(ScriptHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            Path = entry.Path,
            LastAccessedUtcTicks = entry.LastAccessedAt.UtcDateTime.Ticks,
            InputJson = entry.InputJson
        };

    private sealed record ScriptHistoryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Path { get; init; } = string.Empty;

        public long LastAccessedUtcTicks { get; init; }

        public string? InputJson { get; init; }
    }
}
