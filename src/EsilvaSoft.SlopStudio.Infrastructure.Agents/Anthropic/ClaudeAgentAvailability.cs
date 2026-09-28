using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>Motivo seguro de indisponibilidade; nunca carrega texto do provider nem material da chave.</summary>
public enum ClaudeAgentUnavailableReason
{
    None,
    /// <summary>Nenhuma referência de API Key configurada.</summary>
    NotConfigured,
    /// <summary>A referência existe, mas o cofre não tem o item.</summary>
    CredentialMissing,
    /// <summary>Cofre ausente, bloqueado, negado ou prompt de desbloqueio dispensado.</summary>
    VaultUnavailable,
    /// <summary>A Claude API recusou a chave (401) nesta execução do aplicativo; exige nova versão da chave.</summary>
    CredentialRejected,
    /// <summary>O modelo pedido não está na lista permitida da configuração.</summary>
    ModelNotAllowed,
    /// <summary>A configuração não define modelo padrão e a sessão não escolheu um.</summary>
    NoModelSelected,
}

/// <summary>
/// Disponibilidade calculada sem rede. <see cref="Capabilities"/> são as efetivas neste estado (contrato comum
/// <see cref="AgentProviderCapabilities"/>): indisponível declara apenas uso de rede.
/// </summary>
public sealed record ClaudeAgentAvailability(
    bool IsAvailable,
    ClaudeAgentUnavailableReason Reason,
    AgentProviderCapabilities Capabilities,
    string? ModelId = null);

/// <summary>Falha ao criar sessão; a mensagem é fixa em pt-BR e não contém dados da conta, chave ou prompt.</summary>
public sealed class ClaudeProviderUnavailableException(ClaudeAgentUnavailableReason reason)
    : InvalidOperationException(Describe(reason))
{
    public ClaudeAgentUnavailableReason Reason { get; } = reason;

    public static string Describe(ClaudeAgentUnavailableReason reason) => reason switch
    {
        ClaudeAgentUnavailableReason.NotConfigured => "Claude indisponível: nenhuma API Key configurada.",
        ClaudeAgentUnavailableReason.CredentialMissing => "Claude indisponível: a API Key não foi encontrada no cofre.",
        ClaudeAgentUnavailableReason.VaultUnavailable => "Claude indisponível: o cofre de credenciais não está acessível.",
        ClaudeAgentUnavailableReason.CredentialRejected => "Claude indisponível: a API Key foi recusada; configure uma nova chave.",
        ClaudeAgentUnavailableReason.ModelNotAllowed => "Claude indisponível: o modelo escolhido não está permitido.",
        ClaudeAgentUnavailableReason.NoModelSelected => "Claude indisponível: nenhum modelo selecionado.",
        _ => "Claude indisponível.",
    };
}
