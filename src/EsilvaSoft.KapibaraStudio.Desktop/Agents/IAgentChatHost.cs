using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

/// <summary>A connection the Permissions window can offer for "Conexões acessíveis" (logical ID and display name only).</summary>
public sealed record AgentConnectionChoice(Guid Id, string Name);

/// <summary>
/// What the global agent panel needs from the workspace that hosts it (ADR-056). Implemented by the
/// <c>WorkspaceViewModel</c>; every member is called on the UI thread. The panel never reads the explorer selection:
/// the context is the active tab and the Files panel folder, captured synchronously before any await.
/// </summary>
public interface IAgentChatHost
{
    /// <summary>Immutable snapshot of the Files panel folder and the active tab (buffer included). Never throws.</summary>
    AgentWorkspaceContext CaptureWorkspace();

    /// <summary>The Files panel folder (null when none is open), read without capturing the buffer.</summary>
    string? WorkspaceFolder { get; }

    /// <summary>Connections known to the workspace (for the Permissions window).</summary>
    IReadOnlyList<AgentConnectionChoice> ListConnections();

    /// <summary>
    /// Activates the tab that shows <paramref name="targetPath"/> (by tab ID first), opening the file in a new tab when
    /// needed, and returns its editor buffer once the view shows it; null when the target cannot be opened.
    /// </summary>
    Task<IAgentBufferEditor?> OpenEditorAsync(string? targetPath, string? tabId);

    /// <summary>The panel state that is part of the workspace session changed (provider, model, mode, conversation).</summary>
    void OnPanelPreferencesChanged();

    /// <summary>Persists the executable override through the existing session owner before activating it.</summary>
    Task SaveCopilotCliExecutablePathAsync(string? executablePath) =>
        Task.FromException(new NotSupportedException("Persistência da CLI Copilot indisponível."));
}
