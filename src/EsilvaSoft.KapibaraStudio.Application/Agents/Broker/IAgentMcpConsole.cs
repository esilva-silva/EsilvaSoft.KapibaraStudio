namespace EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

/// <summary>Host console effects used by the standalone STDIO MCP proxy.</summary>
public interface IAgentMcpConsole
{
    TextWriter StandardError { get; }
    Stream OpenStandardInput();
    Stream OpenStandardOutput();
    void SuppressStandardTextOutput();
}

/// <summary>Opens protocol streams only after ordinary text output has been suppressed.</summary>
public sealed record AgentMcpConsoleSession(Stream Input, Stream Output)
{
    /// <summary>The caller owns the returned streams and their protocol lifetime.</summary>
    public static AgentMcpConsoleSession Open(IAgentMcpConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        var output = console.OpenStandardOutput();
        console.SuppressStandardTextOutput();
        return new AgentMcpConsoleSession(console.OpenStandardInput(), output);
    }
}
