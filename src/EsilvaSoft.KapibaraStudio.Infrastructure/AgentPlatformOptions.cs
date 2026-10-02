using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Composition options of the agent platform shared by every ingress (native chat runtime and the opt-in MCP broker).
/// The registry is always composed. <see cref="ToolExposureStage"/> defaults to <see cref="AgentToolExposureStage.Metadata"/>
/// (ADR-056: metadata reads and the per-session tools of the integrated Claude Code agent); releasing a stage still
/// authorizes nothing by itself, and <see cref="AgentToolExposureStage.None"/> keeps everything closed. The shared
/// external and generic in-process stages stop at <see cref="AgentToolExposureStage.LiteralQueries"/>; the Copilot-only
/// native stage and Claude session-channel stage may reach <see cref="AgentToolExposureStage.DerivedReads"/> behind
/// individual opt-ins. Releasing a stage still grants nothing: every call needs a persisted grant for its principal.
/// Write tools stay closed independently of the stage: no <see cref="IAgentMongoWriteSource"/> is composed yet
/// (lote 10), so the registry never exposes them even though the approval chain is wired.
/// </summary>
public sealed record AgentPlatformOptions
{
    /// <summary>Catalog stage released to the single shared registry, for every ingress alike.</summary>
    public AgentToolExposureStage ToolExposureStage { get; init; } = AgentToolExposureStage.Metadata;

    /// <summary>
    /// General in-process provider stage. Defaults closed so unrelated in-process providers do not inherit grants.
    /// </summary>
    public AgentToolExposureStage InProcessToolExposureStage { get; init; } = AgentToolExposureStage.None;

    /// <summary>
    /// Additional stage available only to the GitHub Copilot subscription adapter. The tools still require explicit
    /// per-provider opt-ins and turn-scoped grants; the external MCP registry is unaffected. The default includes
    /// bounded derived reads; schema sampling remains absent until its separate local-consent UI is composed.
    /// </summary>
    public AgentToolExposureStage CopilotToolExposureStage { get; init; } = AgentToolExposureStage.DerivedReads;

    /// <summary>
    /// Additional stage available only to Claude Code's authenticated per-session MCP channel. Other providers and
    /// unscoped/external MCP clients continue to use <see cref="ToolExposureStage"/>. Every query still needs the
    /// provider opt-in, data consent, a turn plan, connection scope, and grants.
    /// </summary>
    public AgentToolExposureStage ClaudeToolExposureStage { get; init; } = AgentToolExposureStage.DerivedReads;

    /// <summary>Registry execution ceiling (the registry itself caps at 30 s).</summary>
    public TimeSpan ToolExecutionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum room between the registry execution ceiling and <see cref="AgentRuntimeOptions.ToolTimeout"/> for the
    /// durable intent/terminal appends, queueing and result mapping (defaults: 35 s ≥ 30 s + 5 s).
    /// </summary>
    public static readonly TimeSpan ToolPreparationMargin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runtime limits; validated by the runtime itself when it is created. This instance is the single source of the
    /// runtime budget: the runtime host, the write approval coordinator and the provider adapters' tool-result wait
    /// checks all use it.
    /// </summary>
    public AgentRuntimeOptions Runtime { get; init; } = AgentRuntimeOptions.Default;

    /// <exception cref="ArgumentException">Invalid option or unapproved stage.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(ToolExposureStage))
            throw new ArgumentException("Estágio de exposição desconhecido.", nameof(ToolExposureStage));
        if (ToolExposureStage > AgentToolExposureStage.LiteralQueries)
            throw new ArgumentException("Estágio de exposição ainda não liberado.", nameof(ToolExposureStage));
        if (!Enum.IsDefined(InProcessToolExposureStage) || InProcessToolExposureStage > AgentToolExposureStage.LiteralQueries)
            throw new ArgumentException("Estágio in-process inválido.", nameof(InProcessToolExposureStage));
        if (!Enum.IsDefined(CopilotToolExposureStage) || CopilotToolExposureStage > AgentToolExposureStage.DerivedReads)
            throw new ArgumentException("Estágio Copilot inválido.", nameof(CopilotToolExposureStage));
        if (!Enum.IsDefined(ClaudeToolExposureStage) || ClaudeToolExposureStage > AgentToolExposureStage.DerivedReads)
            throw new ArgumentException("Estágio Claude inválido.", nameof(ClaudeToolExposureStage));
        if (ToolExecutionTimeout <= TimeSpan.Zero || ToolExecutionTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Prazo de execução inválido.", nameof(ToolExecutionTimeout));
        if (Runtime is null) throw new ArgumentException("Opções do runtime ausentes.", nameof(Runtime));
        // The write approval coordinator is composed with the runtime's approval window (same deadline on both sides,
        // so an unanswered approval is audited as Expired, never as Rejected) and caps it at its own default.
        if (Runtime.ApprovalTimeout <= TimeSpan.Zero ||
            Runtime.ApprovalTimeout > AgentWriteApprovalCoordinator.DefaultApprovalTimeout)
            throw new ArgumentException("Prazo de aprovação inválido.", nameof(Runtime));
        // With the approval bridge a write has only Runtime.ToolTimeout for its durable intent plus the registry
        // preflight (capped at ToolExecutionTimeout), and again for execution after approval: keep room for both.
        if (Runtime.ToolTimeout < ToolExecutionTimeout + ToolPreparationMargin)
            throw new ArgumentException("O prazo de tool do runtime não cobre a execução do registry com folga.", nameof(Runtime));
    }
}
