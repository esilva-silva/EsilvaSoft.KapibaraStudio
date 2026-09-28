using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Implemented by the Desktop. Returns an immutable snapshot of the Files panel folder and the active tab (path, name,
/// tab ID, buffer text with unsaved changes, and the tab's own connection/database/collection). Synchronous on
/// purpose: callers capture it on the UI thread before any await, and nothing is read back from UI state later.
/// Never throws for "nothing open": missing parts are null.
/// </summary>
public interface IAgentWorkspaceContextSource
{
    AgentWorkspaceContext Capture();
}
