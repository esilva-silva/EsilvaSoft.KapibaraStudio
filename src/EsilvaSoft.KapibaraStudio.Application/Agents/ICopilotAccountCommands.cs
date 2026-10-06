namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Only-read CLI discovery; no path, process output or account data is returned.</summary>
public enum CopilotCliAvailability
{
    Available, NotFound, InvalidPath, UnsupportedExecutable, NotExecutable, ProbeFailed,
}

public enum CopilotAccountCommandState
{
    Completed, CommandFailed, StillRunning, RuntimeUnavailable, NoVisibleTerminal, StartFailed,
}

/// <summary>Official interactive CLI account commands; no token, credential or output is exposed.</summary>
public interface ICopilotAccountCommands
{
    bool IsCliInstalled();
    CopilotCliAvailability ProbeCli() => IsCliInstalled() ? CopilotCliAvailability.Available : CopilotCliAvailability.NotFound;
    Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken);
}
