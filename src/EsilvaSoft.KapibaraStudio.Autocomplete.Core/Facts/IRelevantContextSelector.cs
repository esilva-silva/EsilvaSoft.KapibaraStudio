using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Tudo o que a seleção de fatos pode olhar, capturado antes de sair da thread do editor. A aba é parte do pedido
/// (<see cref="DocumentId"/>) justamente para que nenhum fato de um documento possa ser atribuído a outro.
/// </summary>
public sealed record AiFactRequest
{
    public AiFactRequest(string documentId, CompletionContext context)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentNullException.ThrowIfNull(context);
        DocumentId = documentId;
        Context = context;
    }

    /// <summary>Identidade da aba/documento que originou o pedido.</summary>
    public string DocumentId { get; }

    /// <summary>Contexto do cursor; <see cref="CompletionContext.Scope"/> define a coleção alvo.</summary>
    public CompletionContext Context { get; }

    /// <summary>Perfil capturado, sem credenciais resolvidas; sem ele o catálogo não responde metadados.</summary>
    public ConnectionProfile? Connection { get; init; }

    /// <summary>Estágios já decodificados antes do cursor; é daqui que sai a exceção de escopo do <c>$lookup</c>.</summary>
    public IReadOnlyList<PipelineStage> Pipeline { get; init; } = [];

    /// <summary>Teto de fatos devolvidos; o corte fino por tokens é de quem monta o prompt.</summary>
    public int MaximumFacts { get; init; } = 200;

    /// <summary>Teto de coleções estrangeiras aceitas pela exceção de <c>$lookup</c>/<c>$unionWith</c>/<c>$graphLookup</c>.</summary>
    public int MaximumForeignCollections { get; init; } = 4;
}

/// <summary>
/// Fonte adicional de fatos. É o ponto de extensão do schema aprendido: quem tiver acesso a ele implementa esta
/// interface na sua própria camada, e o seletor continua sem conhecer a origem além do <see cref="Origin"/> que a
/// fonte declara. Implementações precisam ser síncronas, offline e determinísticas, como o resto de <c>Facts/</c>.
/// </summary>
public interface IAiFactSource
{
    /// <summary>Proveniência que esta fonte declara; o seletor descarta fatos que não a respeitem.</summary>
    AiFactOrigin Origin { get; }

    /// <summary>Categorias que a fonte sabe produzir.</summary>
    AiFactKind ProvidedKinds { get; }

    /// <summary>
    /// Acrescenta fatos de <paramref name="scope"/> a <paramref name="facts"/>. Chamada uma vez por escopo permitido;
    /// a fonte nunca decide sozinha quais coleções entram.
    /// </summary>
    void Collect(AiFactRequest request, AiFactScope scope, ICollection<AiFact> facts, CancellationToken cancellationToken);
}

/// <summary>
/// Transforma um <see cref="AiFactRequest"/> no conjunto de fatos relevantes e dentro do escopo. Síncrono e sem I/O:
/// só enxerga o que já está em memória.
/// </summary>
public interface IRelevantContextSelector
{
    AiFactSet SelectFacts(AiFactRequest request, CancellationToken cancellationToken = default);
}
