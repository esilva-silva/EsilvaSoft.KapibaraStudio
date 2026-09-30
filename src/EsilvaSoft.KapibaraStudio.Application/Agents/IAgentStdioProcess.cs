namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Owned stdio endpoint for an agent CLI. The launcher must isolate the process tree before returning it.
/// Protocol consumers own this endpoint and dispose it after stopping their readers and writers.
/// Implementations drain stderr privately and terminate the tree if its configured limit is exceeded.
/// </summary>
public interface IAgentStdioProcess : IAsyncDisposable
{
    int Id { get; }
    Stream StandardOutput { get; }
    TextWriter StandardInput { get; }
    /// <summary>Best-effort, idempotent termination of this endpoint and its descendants.</summary>
    void KillTree();
}
