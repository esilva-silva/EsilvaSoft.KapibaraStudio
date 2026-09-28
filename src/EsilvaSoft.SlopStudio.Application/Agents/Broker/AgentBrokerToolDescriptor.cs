using System.Text.Json;
using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Application.Agents.Broker;

/// <summary>
/// Static descriptor published by the shared registry. It contains no connection, database or collection names and
/// its hints are informative only: authorization always happens at invocation time.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentBrokerToolDescriptor
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("readOnly")] public required bool ReadOnly { get; init; }
    [JsonPropertyName("destructive")] public required bool Destructive { get; init; }
    [JsonPropertyName("inputSchema")] public required JsonElement InputSchema { get; init; }
    [JsonPropertyName("outputSchema")] public required JsonElement OutputSchema { get; init; }
}
