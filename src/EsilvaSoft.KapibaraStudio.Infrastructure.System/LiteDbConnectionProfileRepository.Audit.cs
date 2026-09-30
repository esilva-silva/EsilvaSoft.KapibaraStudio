using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string AuditCollectionName = "auditEvents";

    public Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "A auditoria deve conter entre 1 e 500 itens.");
        }

        return RunAsync(() =>
        {
            var entries = _database.GetCollection<AuditEntryDocument>(AuditCollectionName)
                .Query()
                .OrderByDescending(entry => entry.OccurredAtUtcTicks)
                .Limit(maximum)
                .ToEnumerable()
                .Select(ToDomain)
                .ToArray();
            return (IReadOnlyList<AuditEntry>)entries;
        }, cancellationToken);
    }

    public Task SaveAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection<AuditEntryDocument>(AuditCollectionName);
            collection.Upsert(FromDomain(entry));
            var obsoleteIds = collection.Query()
                .OrderByDescending(item => item.OccurredAtUtcTicks)
                .Skip(500)
                .ToEnumerable()
                .Select(item => item.Id)
                .ToArray();
            foreach (var obsoleteId in obsoleteIds)
            {
                collection.Delete(obsoleteId);
            }
        }, cancellationToken);
    }

    private static AuditEntry ToDomain(AuditEntryDocument entry) =>
        new(entry.Id, new DateTimeOffset(new DateTime(entry.OccurredAtUtcTicks, DateTimeKind.Utc)), entry.Action, entry.ProfileId, entry.Database, entry.Collection, entry.Summary);

    private static AuditEntryDocument FromDomain(AuditEntry entry) =>
        new()
        {
            Id = entry.Id,
            OccurredAtUtcTicks = entry.OccurredAt.UtcDateTime.Ticks,
            Action = entry.Action,
            ProfileId = entry.ProfileId,
            Database = entry.Database,
            Collection = entry.Collection,
            Summary = entry.Summary
        };

    private sealed class AuditEntryDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public long OccurredAtUtcTicks { get; init; }

        public string Action { get; init; } = string.Empty;

        public Guid? ProfileId { get; init; }

        public string? Database { get; init; }

        public string? Collection { get; init; }

        public string Summary { get; init; } = string.Empty;
    }
}
