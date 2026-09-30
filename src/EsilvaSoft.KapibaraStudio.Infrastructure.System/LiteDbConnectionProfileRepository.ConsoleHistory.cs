using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    public Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        if (!IsValidExecutionHistory(entry)) throw new ArgumentException("Histórico de execução inválido.");
        _database.GetCollection("consoleHistory").Upsert(new BsonDocument {
            ["_id"] = entry.Id, ["time"] = entry.ExecutedAt.UtcTicks,
            ["json"] = System.Text.Json.JsonSerializer.Serialize(entry)
        });
        var old = _database.GetCollection("consoleHistory").Query().OrderByDescending("time").Offset(500).ToArray();
        foreach (var item in old) _database.GetCollection("consoleHistory").Delete(item["_id"]);
    }, cancellationToken);

    public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<ConsoleHistoryEntry>>(() =>
        _database.GetCollection("consoleHistory").Query().OrderByDescending("time").Limit(Math.Clamp(maximum, 1, 500)).ToArray()
            .Select(document => {
                var entry = System.Text.Json.JsonSerializer.Deserialize<ConsoleHistoryEntry>(document["json"].AsString);
                return entry is not null && IsValidExecutionHistory(entry) ? entry : throw new InvalidDataException("Histórico de execução ilegível ou de versão não suportada.");
            }).ToArray(), cancellationToken);

    private static bool IsValidExecutionHistory(ConsoleHistoryEntry entry) => entry.Version == 1 && entry.Id != Guid.Empty &&
        entry.Mode is "Console" or "Agregação" && entry.Collection is not null && entry.DocumentLimit is >= 1 and <= 10000;
}
