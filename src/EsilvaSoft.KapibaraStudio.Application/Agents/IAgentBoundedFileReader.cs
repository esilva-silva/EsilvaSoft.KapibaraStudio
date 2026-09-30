namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public enum AgentFileReadState { Read, NotFound, TooLarge }
public sealed record AgentFileReadResult(AgentFileReadState State, byte[] Bytes);

/// <summary>Reads at most the requested byte limit. I/O/security failures propagate; cancellation stays observable.</summary>
public interface IAgentBoundedFileReader
{
    AgentFileReadResult Read(string fullPath, int maximumBytes);
    Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken);
}
