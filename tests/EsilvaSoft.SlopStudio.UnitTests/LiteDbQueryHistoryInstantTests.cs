using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// The query history is mapped by LiteDB's global mapper, which returns BSON dates converted to local time. Relabelling
/// that value as UTC shifted every reloaded execution instant by the machine's offset (east or west of UTC).
/// </summary>
[TestFixture]
public sealed class LiteDbQueryHistoryInstantTests
{
    [Test]
    public async Task ExecutedAtRoundTripsAsTheSameInstantAcrossReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "workspace.db");
        try
        {
            var executedAt = new DateTimeOffset(2026, 7, 4, 22, 15, 30, 250, TimeSpan.Zero);
            var withOffset = new DateTimeOffset(2026, 7, 5, 1, 2, 3, 4, TimeSpan.FromHours(5.5));
            var utcEntry = QueryHistoryEntry.Create(null, new MongoQuery("catalogo", "clientes"), executedAt);
            var offsetEntry = QueryHistoryEntry.Create(null, new MongoQuery("catalogo", "pedidos"), withOffset);
            using (var owner = new LiteDbConnectionProfileRepository(path))
            {
                await owner.SaveAsync(utcEntry);
                await owner.SaveAsync(offsetEntry);
            }

            using var reopened = new LiteDbConnectionProfileRepository(path);
            var loaded = (await reopened.GetRecentAsync(null)).ToDictionary(entry => entry.Id);
            Assert.Multiple(() =>
            {
                Assert.That(loaded[utcEntry.Id].ExecutedAt.UtcTicks, Is.EqualTo(executedAt.UtcTicks));
                Assert.That(loaded[offsetEntry.Id].ExecutedAt.UtcTicks, Is.EqualTo(withOffset.UtcTicks));
                Assert.That(loaded[utcEntry.Id].ExecutedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            });
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
