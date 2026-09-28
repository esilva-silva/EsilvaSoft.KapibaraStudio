namespace EsilvaSoft.SlopStudio.Core.Agents;

public readonly record struct AgentSessionId(string Value)
{
    public static AgentSessionId New() => new(Guid.NewGuid().ToString("N"));

    public bool IsValid => Guid.TryParseExact(Value, "N", out _);

    public override string ToString() => Value;
}

public readonly record struct AgentTurnId(string Value)
{
    public static AgentTurnId New() => new(Guid.NewGuid().ToString("N"));

    public bool IsValid => Guid.TryParseExact(Value, "N", out _);

    public override string ToString() => Value;
}
