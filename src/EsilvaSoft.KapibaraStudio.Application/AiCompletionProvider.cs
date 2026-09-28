using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Autocomplete por IA pedido explicitamente pelo usuário (<c>Ctrl+;</c>).
/// </summary>
/// <remarks>
/// Contrato de fluxo, e não de resultado único: a interface devolve <see cref="IAsyncEnumerable{T}"/> porque a
/// prévia inline progressiva da Fase 4.3 consome as atualizações conforme chegam. Quem só quer o desfecho usa
/// <see cref="AiCompletionProviderExtensions.GetCandidateAsync"/>.
/// </remarks>
public interface IAiCompletionProvider
{
    /// <summary>Serviço de modelo compartilhado por todas as modalidades de IA.</summary>
    ILocalAiModelService Models { get; }

    /// <summary>Executa o pedido explícito e transmite prévias, candidato final ou recusa tipada.</summary>
    /// <param name="request">Pedido já capturado, com fatos e janela do editor.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo (<c>Esc</c>, nova edição, troca de aba).</param>
    IAsyncEnumerable<AiCompletionUpdate> RequestAsync(AiGenerationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// O provider da IA explícita: contexto rico sob orçamento, prioridade interativa e candidato seguro.
/// </summary>
/// <remarks>
/// <para><strong>Papel.</strong> É o par explícito do <see cref="AiAutocompleteProvider"/>, que continua atendendo o
/// ghost automático da Fase 5.1 pelo caminho v1 cru, em segundo plano. A divisão é deliberada: o automático é barato
/// e silencioso, o explícito pode pagar a montagem de contexto da Fase 3 (fatos, janela sintática, orçamento medido
/// pelo tokenizador real) porque o usuário pediu e está esperando.</para>
/// <para><strong>Prioridade.</strong> Todo pedido sai como <see cref="AiRequestPriority.Interactive"/>, seja qual for
/// a prioridade escrita no <see cref="AiGenerationRequest"/>. É o que faz <c>Ctrl+;</c> preemptar uma geração de
/// fundo em andamento, e não o contrário.</para>
/// <para><strong>Sem duplicar runtime.</strong> O provider não conhece <see cref="ILocalModelRuntime"/> e não
/// constrói serviço nenhum: ele delega ao <see cref="AiGenerationPipeline"/> recebido, que por sua vez usa o
/// <see cref="ILocalAiModelService"/> injetado. Construído a partir do mesmo serviço que o
/// <see cref="AiAutocompleteProvider"/>, os dois compartilham literalmente a mesma instância — e, com ela, a fila de
/// prioridade, o cooldown e o modelo carregado.</para>
/// </remarks>
/// <param name="pipeline">Camada de geração compartilhada.</param>
public sealed class AiCompletionProvider(AiGenerationPipeline pipeline) : IAiCompletionProvider
{
    private readonly AiGenerationPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <inheritdoc />
    public ILocalAiModelService Models => _pipeline.Models;

    /// <inheritdoc />
    public IAsyncEnumerable<AiCompletionUpdate> RequestAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // O pedido explícito pode carregar o modelo sob demanda: quem apertou o atalho aceita esperar a carga.
        return _pipeline.StreamAsync(request with { Priority = AiRequestPriority.Interactive }, cancellationToken);
    }
}

/// <summary>Conveniências sobre o fluxo de atualizações.</summary>
public static class AiCompletionProviderExtensions
{
    /// <summary>
    /// Consome o fluxo até o fim e devolve só o candidato final, ou <see langword="null"/> em qualquer recusa. As
    /// prévias são descartadas — é a forma certa para quem não desenha prévia progressiva.
    /// </summary>
    /// <param name="provider">Provider explícito.</param>
    /// <param name="request">Pedido já capturado.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo.</param>
    public static async Task<AiCompletionCandidate?> GetCandidateAsync(this IAiCompletionProvider provider,
        AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        await foreach (var update in provider.RequestAsync(request, cancellationToken).ConfigureAwait(false))
            if (update.IsFinal) return update.Candidate;
        return null;
    }
}
