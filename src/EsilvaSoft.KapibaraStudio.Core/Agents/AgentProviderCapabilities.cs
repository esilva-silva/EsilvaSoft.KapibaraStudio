namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>
/// Capabilities a provider has proven (adapter and model), before product policy and session permissions are
/// intersected. Every flag defaults to false. File editing, command execution and sub-agents are disabled in the
/// phase 7 baseline for every provider and cannot be declared; enabling them requires a new contract and ADR.
/// Capabilities never authorize a tool: authorization always comes from the shared registry.
/// </summary>
public sealed record AgentProviderCapabilities
{
    public static AgentProviderCapabilities None { get; } = new();

    /// <summary>Conversational turns (not only code proposals).</summary>
    public bool Chat { get; init; }

    /// <summary>Incremental delivery of text, proven for the adapter/model.</summary>
    public bool Streaming { get; init; }

    /// <summary>Structured tool calls translated to the shared registry.</summary>
    public bool ToolCalling { get; init; }

    /// <summary>Provider-managed native file, command, or network tools; false by default.</summary>
    public bool NativeTools { get; init; }

    /// <summary>Logical in-memory session that keeps authorized history across turns (never persisted by Slop).</summary>
    public bool Sessions { get; init; }

    public bool ModelSelection { get; init; }

    /// <summary>A turn produces code proposal text for user review (local FIM models); nothing is applied or executed.</summary>
    public bool CodeProposals { get; init; }

    /// <summary>The provider itself speaks MCP. False for every baseline provider; MCP is a separate channel.</summary>
    public bool Mcp { get; init; }

    /// <summary>Officially published reasoning summary; never private chain-of-thought.</summary>
    public bool ThinkingSummary { get; init; }

    /// <summary>Raw local reasoning text, separate from a provider's published summary.</summary>
    public bool Reasoning { get; init; }

    /// <summary>Data leaves the machine. Presentation still derives the destination from <c>IsLocal</c>.</summary>
    public bool UsesNetwork { get; init; }

    /// <summary>
    /// The adapter honors <c>AgentTurnRequest.Plan</c>, <c>SystemPrompt</c> and <c>Attachments</c> (tools, rules, mode and
    /// chips). False by default: callers must refuse to send a plan or attachments to a provider without it, instead of
    /// letting them be silently ignored. Requires <see cref="Chat"/>.
    /// </summary>
    public bool TurnPlan { get; init; }

    public AgentCapabilityEvidence Evidence { get; init; }

    // Kept as instance properties (not static) so they stay part of the same reflection/serialization
    // surface as the other capability flags on this record; a provider descriptor consumer reads them
    // uniformly through an instance, and static members would silently drop out of that surface.
#pragma warning disable CA1822 // Deliberately instance members: see comment above.
    public bool FileEditing => false;

    public bool CommandExecution => false;

    public bool SubAgents => false;
#pragma warning restore CA1822

    /// <summary>
    /// Intersection with another declaration (e.g. dynamic status ∩ static descriptor): a flag survives only when both
    /// declare it, evidence is the weaker one, and <see cref="UsesNetwork"/> is kept when either declares it.
    /// </summary>
    public AgentProviderCapabilities IntersectWith(AgentProviderCapabilities other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var left = Normalize();
        var right = other.Normalize();
        return new AgentProviderCapabilities
        {
            Chat = left.Chat && right.Chat,
            Streaming = left.Streaming && right.Streaming,
            ToolCalling = left.ToolCalling && right.ToolCalling,
            NativeTools = left.NativeTools && right.NativeTools,
            Sessions = left.Sessions && right.Sessions,
            ModelSelection = left.ModelSelection && right.ModelSelection,
            CodeProposals = left.CodeProposals && right.CodeProposals,
            Mcp = left.Mcp && right.Mcp,
            ThinkingSummary = left.ThinkingSummary && right.ThinkingSummary,
            Reasoning = left.Reasoning && right.Reasoning,
            TurnPlan = left.TurnPlan && right.TurnPlan,
            UsesNetwork = left.UsesNetwork || right.UsesNetwork,
            Evidence = (AgentCapabilityEvidence)Math.Min((int)left.Evidence, (int)right.Evidence),
        }.Normalize();
    }

    /// <summary>
    /// Effective form: without evidence nothing is declared (only <see cref="UsesNetwork"/> is kept, since it restricts),
    /// and dependent flags require their base (tool calling and sessions require chat).
    /// </summary>
    public AgentProviderCapabilities Normalize()
    {
        if (!Enum.IsDefined(Evidence) || Evidence == AgentCapabilityEvidence.None)
        {
            return UsesNetwork ? None with { UsesNetwork = true } : None;
        }

        return this with
        {
            Streaming = Streaming && (Chat || CodeProposals),
            ToolCalling = ToolCalling && Chat,
            NativeTools = NativeTools && Chat,
            Sessions = Sessions && Chat,
            TurnPlan = TurnPlan && Chat,
            Reasoning = Reasoning && Chat,
        };
    }
}
