using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

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
/// Capacidades do agente local, limitadas ao que o adaptador e o modelo comprovam. O produto só tem modelos FIM
/// (Qwen/DeepSeek): não há template conversacional, protocolo estruturado de tools nem entrega incremental garantida
/// pela abstração do serviço, então Chat, ToolCalling e Streaming são sempre falsas aqui.
/// </summary>
/// <param name="FimCodeProposals">Turno produz texto de proposta de código a partir de um modelo FIM, para revisão do usuário.</param>
public sealed record LocalAgentCapabilities(bool FimCodeProposals)
{
    public bool Chat { get; }
    public bool Streaming { get; }
    public bool ToolCalling { get; }
    public bool Mcp { get; }
    public bool Sessions { get; }
    public bool ModelSelection { get; }
    public bool FileEditing { get; }
    public bool CommandExecution { get; }
    public bool SubAgents { get; }
    public bool ThinkingSummary { get; }
    public bool UsesNetwork { get; }
    public bool RequiresAccount { get; }

    public static LocalAgentCapabilities None { get; } = new(false);

    /// <summary>
    /// Mapeamento para o contrato neutro sem ampliar nada: só <c>CodeProposals</c> quando comprovado (modelo FIM válido,
    /// coberto por testes automatizados), sem rede; todo o resto permanece falso.
    /// </summary>
    public AgentProviderCapabilities ToProviderCapabilities() => FimCodeProposals
        ? new AgentProviderCapabilities { CodeProposals = true, Evidence = AgentCapabilityEvidence.AutomatedContract }
        : AgentProviderCapabilities.None;
}

public sealed record LocalAgentAvailability(
    bool IsAvailable, LocalAgentUnavailableReason Reason, LocalAgentCapabilities Capabilities, string? ModelName = null);
