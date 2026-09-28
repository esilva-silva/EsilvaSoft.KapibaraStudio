namespace EsilvaSoft.SlopStudio.Application.Agents.Broker;

/// <summary>Sanitized IPC failure. <see cref="Code"/> is one of <see cref="AgentBrokerProtocol.ErrorCodes"/>.</summary>
public sealed class AgentBrokerProtocolException : Exception
{
    public AgentBrokerProtocolException() : this(AgentBrokerProtocol.ErrorCodes.ProtocolViolation)
    {
    }

    public AgentBrokerProtocolException(string code) : base("Falha no protocolo local do broker de agentes: " + code) =>
        Code = code ?? throw new ArgumentNullException(nameof(code));

    public AgentBrokerProtocolException(string code, Exception innerException)
        : base("Falha no protocolo local do broker de agentes: " + code, innerException) =>
        Code = code ?? throw new ArgumentNullException(nameof(code));

    public string Code { get; }
}
