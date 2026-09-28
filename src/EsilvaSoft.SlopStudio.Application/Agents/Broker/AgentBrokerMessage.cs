using System.Text.Json;
using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Application.Agents.Broker;

/// <summary>
/// Closed envelope of every IPC frame. Unknown members are rejected. <see cref="Arguments"/> and
/// <see cref="StructuredContent"/> travel as raw JSON so Extended JSON strings and large integers are forwarded
/// byte-for-byte instead of being re-typed. <see cref="Proof"/> exists only in the authenticate frame and must never be
/// logged or echoed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentBrokerMessage
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("protocol")] public string? Protocol { get; init; }
    [JsonPropertyName("major")] public int? Major { get; init; }
    [JsonPropertyName("minor")] public int? Minor { get; init; }
    [JsonPropertyName("nonce")] public string? Nonce { get; init; }
    [JsonPropertyName("peerNonce")] public string? PeerNonce { get; init; }
    [JsonPropertyName("channelId")] public Guid? ChannelId { get; init; }
    [JsonPropertyName("proof")] public string? Proof { get; init; }
    [JsonPropertyName("id")] public long? Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("arguments")] public JsonElement? Arguments { get; init; }
    [JsonPropertyName("timeoutMs")] public int? TimeoutMilliseconds { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("errorCode")] public string? ErrorCode { get; init; }
    [JsonPropertyName("dispatched")] public bool? Dispatched { get; init; }
    [JsonPropertyName("reauthenticate")] public bool? Reauthenticate { get; init; }
    [JsonPropertyName("structuredContent")] public JsonElement? StructuredContent { get; init; }
    [JsonPropertyName("tools")] public IReadOnlyList<AgentBrokerToolDescriptor>? Tools { get; init; }
    [JsonPropertyName("supportedMajor")] public int? SupportedMajor { get; init; }

    public const string SucceededStatus = "succeeded";
    public const string FailedStatus = "failed";
}
