namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public enum CopilotAccountCommandState
{
    Completed, CommandFailed, StillRunning, RuntimeUnavailable, NoVisibleTerminal, StartFailed,
}

/// <summary>Official interactive CLI account commands; no token, credential or output is exposed.</summary>
public interface ICopilotAccountCommands
{
    bool IsCliInstalled();
    Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken);
}
