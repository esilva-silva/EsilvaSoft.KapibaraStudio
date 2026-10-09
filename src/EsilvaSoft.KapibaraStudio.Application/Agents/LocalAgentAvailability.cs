using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public enum LocalAgentUnavailableReason
{
    None,
    /// <summary>Modo básico ou assistente local desabilitado nas preferências: nenhuma inferência é permitida.</summary>
    Disabled,
    NoModelConfigured,
    /// <summary>A pasta selecionada não existe, não é um pacote utilizável ou não declara o contrato de proposta FIM.</summary>
    ModelMissingOrInvalid,
}

/// <summary>
/// Capacidades do agente local, limitadas ao que o adaptador e o modelo comprovam. Pacotes FIM continuam oferecendo
/// propostas de código (FimCodeProposals); pacotes de agente precisam declarar Chat/Tools e passar pela validação estrutural.
/// </summary>
public sealed record LocalAgentCapabilities(bool FimCodeProposals, bool Chat = false, bool ToolCalling = false,
    bool Sessions = false, bool TurnPlan = false, bool Reasoning = false, bool Mcp = false,
    bool ThinkingSummary = false, bool UsesNetwork = false, bool RequiresAccount = false,
    bool FileEditing = false, bool CommandExecution = false, bool SubAgents = false)
{
    public bool Streaming => Chat || FimCodeProposals;
    public bool ModelSelection => Chat;

    public static LocalAgentCapabilities None { get; } = new(false);

    /// <summary>
    /// Mapeamento para o contrato neutro sem ampliar nada: só <c>CodeProposals</c> quando comprovado (modelo FIM válido,
    /// coberto por testes automatizados), sem rede; todo o resto permanece falso.
    /// </summary>
    public AgentProviderCapabilities ToProviderCapabilities() => FimCodeProposals || Chat
        ? new AgentProviderCapabilities
        {
            Chat = Chat, Streaming = Streaming, ToolCalling = ToolCalling, Sessions = Sessions,
            ModelSelection = ModelSelection, TurnPlan = TurnPlan, Reasoning = Reasoning,
            Mcp = Mcp, ThinkingSummary = ThinkingSummary, UsesNetwork = UsesNetwork,
            CodeProposals = FimCodeProposals, Evidence = AgentCapabilityEvidence.AutomatedContract,
        }
        : AgentProviderCapabilities.None;
}

public sealed record LocalAgentAvailability(
    bool IsAvailable, LocalAgentUnavailableReason Reason, LocalAgentCapabilities Capabilities, string? ModelName = null,
    LocalModelDefinition? Model = null);
