namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed class AgentRuntimeException : Exception
{
    public AgentRuntimeException(string code, string safeMessage)
        : base(safeMessage)
    {
        Code = code;
    }

    public string Code { get; }
}
