namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Operation mode chosen by the user for a turn. The mode never authorizes anything by itself: the Application
/// <c>AgentModePolicy</c> intersects it with the persisted provider permissions and the platform facts to produce the
/// <see cref="AgentTurnPlan"/> that adapters obey. Persisted by name (append-only; never renumber).
/// </summary>
public enum AgentOperationMode
{
    /// <summary>Read tools plus <c>propose_file_edit</c> with human review of every proposal.</summary>
    Agent = 0,

    /// <summary>Read tools only; edit proposals are not exposed and the prompt asks for a textual plan.</summary>
    Planning = 1,

    /// <summary>Read tools without asking; proposals are applied to the editor buffer (reversible, never saved to disk).</summary>
    Automatic = 2,

    /// <summary>Every exposed tool, reads included, asks for confirmation through the product permission-prompt tool.</summary>
    AskConfirmations = 3,
}
