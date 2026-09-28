using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    /// <summary>
    /// Logical scan of every document of every collection held by this owner, through the single LiteDB
    /// connection. Each canary is also searched percent-encoded and JSON-escaped. Returns only the names of the
    /// collections with a hit — never the matched text, the document or its identifier.
    /// </summary>
    /// <remarks>
    /// This proves absence in live documents only. LiteDB does not zero freed pages, so a value that was persisted
    /// before a migration may survive physically in the file or in old backups until they are rewritten.
    /// </remarks>
    internal Task<IReadOnlyList<string>> FindCollectionsContainingAsync(IReadOnlyCollection<string> canaries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canaries);
        var needles = canaries
            .Where(canary => !string.IsNullOrEmpty(canary))
            .SelectMany(canary => new[]
            {
                canary, Uri.EscapeDataString(canary), JsonSerializer.Serialize(new BsonValue(canary)).Trim('"')
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (needles.Length == 0) throw new ArgumentException("Informe ao menos um marcador.", nameof(canaries));
        return RunAsync(() =>
        {
            var hits = new List<string>();
            foreach (var name in _database.GetCollectionNames().OrderBy(name => name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var document in _database.GetCollection(name).FindAll())
                {
                    var json = JsonSerializer.Serialize(document);
                    if (needles.Any(needle => json.Contains(needle, StringComparison.Ordinal)))
                    {
                        hits.Add(name);
                        break;
                    }
                }
            }
            return (IReadOnlyList<string>)hits.AsReadOnly();
        }, cancellationToken);
    }
}
