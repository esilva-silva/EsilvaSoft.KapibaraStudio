using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Application;

public enum AdministrationMetricSource
{
    ServerStatus,
    Topology,
    DatabaseStats,
    CollectionStats,
    ExactCount,
    FilteredExactCount,
    EstimatedCount
}

public sealed record AdministrationMetric(string Name, decimal Value);

public sealed record AdministrationMetricSample(
    Guid ProfileId,
    Guid? SourceGenerationId,
    AdministrationMetricSource Source,
    DateTimeOffset ObservedAt,
    string? Database,
    string? Collection,
    IReadOnlyList<AdministrationMetric> Metrics);

/// <summary>
/// Bounded, process-local history of selected MongoDB command values. Raw command replies,
/// connection strings, credentials and query filters are never retained.
/// </summary>
public sealed class AdministrationMetricHistory
{
    public const int Capacity = 64;
    public const int MaximumResponseCharacters = 1_000_000;

    private static readonly Dictionary<AdministrationMetricSource, string[]> Paths =
        new Dictionary<AdministrationMetricSource, string[]>
        {
            [AdministrationMetricSource.ServerStatus] =
                ["uptime", "connections.current", "connections.available", "opcounters.insert", "opcounters.query", "opcounters.update", "opcounters.delete", "mem.resident"],
            [AdministrationMetricSource.Topology] = ["maxWireVersion", "minWireVersion"],
            [AdministrationMetricSource.DatabaseStats] =
                ["collections", "objects", "dataSize", "storageSize", "indexes", "indexSize"],
            [AdministrationMetricSource.CollectionStats] =
                ["count", "size", "storageSize", "totalIndexSize", "nindexes"]
        };

    private readonly object _gate = new();
    private readonly Queue<AdministrationMetricSample> _samples = new();

    public void RecordCommand(
        Guid profileId,
        Guid? sourceGenerationId,
        AdministrationMetricSource source,
        string responseJson,
        string? database = null,
        string? collection = null,
        DateTimeOffset? observedAt = null)
    {
        if (!Paths.TryGetValue(source, out var paths))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (string.IsNullOrWhiteSpace(responseJson) || responseJson.Length > MaximumResponseCharacters)
            return;

        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var metrics = new List<AdministrationMetric>(paths.Length);
            foreach (var path in paths)
            {
                var value = response.RootElement;
                var found = true;
                foreach (var segment in path.Split('.'))
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                    {
                        found = false;
                        break;
                    }
                }

                if (found && TryReadNumber(value, out var number))
                    metrics.Add(new AdministrationMetric(path, number));
            }

            Add(new AdministrationMetricSample(profileId, sourceGenerationId, source,
                observedAt ?? DateTimeOffset.UtcNow, database, collection, metrics.ToArray()));
        }
        catch (JsonException)
        {
            // An unrecognized response remains available to the caller, but is not kept in history.
        }
    }

    public void RecordCount(
        Guid profileId,
        Guid? sourceGenerationId,
        long count,
        bool estimated,
        bool filtered,
        string database,
        string collection,
        DateTimeOffset? observedAt = null)
    {
        var source = estimated ? AdministrationMetricSource.EstimatedCount
            : filtered ? AdministrationMetricSource.FilteredExactCount
            : AdministrationMetricSource.ExactCount;
        Add(new AdministrationMetricSample(profileId, sourceGenerationId, source,
            observedAt ?? DateTimeOffset.UtcNow, database, collection,
            [new AdministrationMetric("count", count)]));
    }

    public IReadOnlyList<AdministrationMetricSample> GetSnapshot(Guid profileId, Guid? sourceGenerationId)
    {
        lock (_gate)
        {
            return _samples.Where(sample => sample.ProfileId == profileId
                    && sample.SourceGenerationId == sourceGenerationId)
                .Reverse().ToArray();
        }
    }

    private void Add(AdministrationMetricSample sample)
    {
        lock (_gate)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > Capacity)
                _samples.Dequeue();
        }
    }

    private static bool TryReadNumber(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Object
            && (value.TryGetProperty("$numberLong", out var wrapped)
                || value.TryGetProperty("$numberInt", out wrapped)
                || value.TryGetProperty("$numberDouble", out wrapped)))
            value = wrapped;

        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number)
                || value.ValueKind == JsonValueKind.String
                    && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
