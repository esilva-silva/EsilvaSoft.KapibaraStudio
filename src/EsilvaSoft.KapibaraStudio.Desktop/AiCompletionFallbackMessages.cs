using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Traduz a recusa tipada da IA explícita na linha de estado que acompanha a lista tradicional.
/// </summary>
/// <remarks>
/// <para><strong>A mensagem deriva do motivo, nunca o contrário</strong> ([DEC-R41-REASONS]). Nada aqui inspeciona o
/// texto de <see cref="AiCompletionUpdate.Message"/> para decidir: o texto do serviço só é usado quando não há motivo
/// tipado algum, caso em que ele é a única informação existente.</para>
/// <para><strong>Uma linha, nenhum diálogo.</strong> É o que a seção de riscos da Fase 4 pede; o destino desta
/// string é o rodapé da própria lista tradicional, junto dos atalhos dela.</para>
/// </remarks>
public static class AiCompletionFallbackMessages
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    /// <summary>A IA explícita está desligada nas preferências (modo Básico ou autocomplete desabilitado).</summary>
    public static string Disabled => T("aiDisabled");

    /// <summary>Esta aba não recebeu provider de IA explícita (aba isolada de projeto ou de teste).</summary>
    public static string NotConfigured => T("aiNotConfigured");

    /// <summary>Falha inesperada ao consumir o fluxo de geração; a mensagem nunca carrega erro nativo cru.</summary>
    public static string Failed => T("aiFailed");

    /// <summary>
    /// A lista tradicional explícita está desligada nas preferências e por isso não foi aberta. O fallback informa a
    /// indisponibilidade e nada mais: não reabre uma apresentação desligada nem altera a preferência para mostrá-la.
    /// </summary>
    public static string TraditionalDisabled => T("traditionalDisabled");

    /// <summary>Linha de estado para uma atualização final que não trouxe candidato.</summary>
    /// <param name="update">Atualização final recusada.</param>
    /// <param name="now">Relógio de quem exibe, para o tempo restante de uma janela de recusa.</param>
    public static string Describe(AiCompletionUpdate update, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(update);
        var text = update.Failure switch
        {
            AiCompletionFailure.Privacy => T("aiPrivacyFailure"),
            AiCompletionFailure.BudgetExhausted => T("aiBudgetFailure"),
            AiCompletionFailure.Rejected => T("aiRejectedFailure"),
            // Prazo vencido sem nada aproveitável. Com prévia parcial válida o pedido nem chega aqui: ele termina em
            // sucesso parcial e a prévia fica na tela.
            AiCompletionFailure.Timeout => T("aiTimeoutFailure"),
            // Descarte silencioso; existe para completude do mapeamento, e quem chama não exibe esta linha.
            AiCompletionFailure.Preempted => T("aiPreemptedFailure"),
            AiCompletionFailure.ModelUnavailable => Describe(update.Reason, update.Message),
            _ => Failed
        };
        if (update.RetryAfter is not { } retry || retry <= now) return text;
        var seconds = Math.Max(1, (int)Math.Ceiling((retry - now).TotalSeconds));
        return text + F("aiRetry", seconds);
    }

    private static string Describe(LocalModelUnavailableReason? reason, string message) => reason switch
    {
        LocalModelUnavailableReason.NoModelConfigured => T("noAiModelSelected"),
        LocalModelUnavailableReason.ModelInvalid => T("aiModelInvalid"),
        LocalModelUnavailableReason.CapabilityMissing => T("aiCapabilityMissing"),
        LocalModelUnavailableReason.ProviderUnavailable => T("aiProviderUnavailable"),
        LocalModelUnavailableReason.Cooldown => T("aiCooldown"),
        LocalModelUnavailableReason.NotLoaded => T("aiNotLoaded"),
        LocalModelUnavailableReason.DifferentConfiguration => T("aiDifferentConfiguration"),
        LocalModelUnavailableReason.ContextOverflow => T("aiContextOverflow"),
        LocalModelUnavailableReason.RuntimeFailure => T("aiRuntimeFailure"),
        // Sem motivo tipado, a mensagem do serviço é a única informação que existe — e ela já vem segura da camada
        // de aplicação (sem texto do editor e sem erro nativo cru).
        _ => message.Length > 0 ? message : Failed
    };
}
