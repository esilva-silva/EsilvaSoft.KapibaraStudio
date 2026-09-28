using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

/// <summary>
/// Desktop implementation of <see cref="IAgentWorkspaceContextSource"/> (ADR-056). The registry calls
/// <see cref="Capture"/> on a broker thread; the snapshot is taken synchronously on the UI thread through the capture
/// delegate attached by the workspace (<see cref="Attach"/>) with a short deadline. Without an attached workspace, or
/// when the UI thread does not answer in time, it returns an empty snapshot (every part null) instead of blocking or
/// guessing. The snapshot is immutable: nothing is read back from UI state later.
/// </summary>
public sealed class DesktopAgentWorkspaceContextSource(TimeProvider? clock = null) : IAgentWorkspaceContextSource
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private volatile Func<AgentWorkspaceContext>? _capture;

    /// <summary>Deadline of the synchronous read of the UI state.</summary>
    public TimeSpan Timeout { get; init; } = AgentUiDispatch.DefaultReadTimeout;

    /// <summary>Attaches the UI-thread capture of the workspace (the last attached wins). Returns a detach token.</summary>
    public IDisposable Attach(Func<AgentWorkspaceContext> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
        return new Detacher(this, capture);
    }

    public AgentWorkspaceContext Capture()
    {
        var empty = new AgentWorkspaceContext(_clock.GetUtcNow());
        if (_capture is not { } capture)
        {
            return empty;
        }

        return AgentUiDispatch.ReadOnUi(capture, empty, Timeout) ?? empty;
    }

    private sealed class Detacher(DesktopAgentWorkspaceContextSource owner, Func<AgentWorkspaceContext> capture) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(owner._capture, capture))
            {
                owner._capture = null;
            }
        }
    }
}
