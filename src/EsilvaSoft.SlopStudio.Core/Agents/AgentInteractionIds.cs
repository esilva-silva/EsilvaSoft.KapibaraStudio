namespace EsilvaSoft.SlopStudio.Core.Agents;

public readonly record struct AgentToolCallId(string Value)
{
    public static AgentToolCallId New() => new(Guid.NewGuid().ToString("N"));

    public bool IsValid => Guid.TryParseExact(Value, "N", out _);
}

public readonly record struct AgentApprovalId(string Value)
{
    public static AgentApprovalId New() => new(Guid.NewGuid().ToString("N"));

    public bool IsValid => Guid.TryParseExact(Value, "N", out _);
}

/// <summary>
/// Opaque message identifier. Adapters may use one per native message for deduplication; the runtime publishes its
/// own identifier, so native IDs never become part of the public protocol.
/// </summary>
public readonly record struct AgentMessageId(string Value)
{
    public static AgentMessageId New() => new(Guid.NewGuid().ToString("N"));

    public bool IsValid => Guid.TryParseExact(Value, "N", out _);
}
