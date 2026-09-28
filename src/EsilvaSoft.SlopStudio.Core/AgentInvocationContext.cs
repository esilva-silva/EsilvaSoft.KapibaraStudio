namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Non-authorizing identity metadata for the invocation that carried a request.</summary>
public sealed class AgentInvocationContext
{
    internal AgentInvocationContext(string? providerId, Guid? clientId, Guid? sessionId, Guid? turnId)
    {
        if (providerId is { Length: > 64 } || providerId?.Any(char.IsControl) == true)
            throw new ArgumentException("O identificador do provider é inválido.", nameof(providerId));
        if (clientId == Guid.Empty || sessionId == Guid.Empty || turnId == Guid.Empty)
            throw new ArgumentException("Identificadores de invocação opcionais não podem ser vazios.");
        ProviderId = providerId;
        ClientId = clientId;
        SessionId = sessionId;
        TurnId = turnId;
    }

    public string? ProviderId { get; }
    public Guid? ClientId { get; }
    public Guid? SessionId { get; }
    public Guid? TurnId { get; }
}
