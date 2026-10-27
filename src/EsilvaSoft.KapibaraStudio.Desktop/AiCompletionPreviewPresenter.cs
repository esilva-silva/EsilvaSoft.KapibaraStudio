namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>Em que ponto está a prévia da IA explícita (<c>Ctrl+;</c>) deste editor.</summary>
public enum AiCompletionPreviewState
{
    /// <summary>Nada pedido, nada à mostra.</summary>
    Idle,

    /// <summary>
    /// Pedido aberto, mas o que está acontecendo é a carga do modelo, não a geração. Estado próprio porque a linha
    /// "carga em andamento" da matriz de fallback pede um indicador distinto — e porque desistir aqui abandona a
    /// espera, e não a carga, que pertence ao serviço de modelo.
    /// </summary>
    Loading,

    /// <summary>Pedido em andamento; o indicador está visível e o texto pode estar crescendo.</summary>
    Generating,

    /// <summary>Candidato final recebido e esperando confirmação explícita do usuário.</summary>
    Preview
}

/// <summary>
/// Estado da prévia da IA explícita, separado dos controles do Avalonia — o par de
/// <see cref="CompletionWindowPresenter"/> para a outra modalidade.
/// </summary>
/// <remarks>
/// <para><strong>Por que não reaproveitar o <see cref="CompletionWindowPresenter"/>.</strong> Aquele guarda uma
/// lista de <c>CompletionItem</c> filtrada por prefixo e uma seleção estável por identidade de símbolo; aqui não há
/// lista, não há item, não há filtro e não há seleção — há um texto que cresce por pedaços e que só vale enquanto o
/// documento não mudou. Reusar a classe significaria esvaziar metade dela. O que <em>não</em> é duplicado é a
/// superfície: a prévia da IA, a lista tradicional e o ghost automático nunca aparecem juntos, porque
/// <c>Ctrl+;</c> começa invalidando os outros dois e enquanto esta prévia está ativa o ghost não é pedido.</para>
/// <para><strong>Resultado obsoleto.</strong> <see cref="Generation"/> avança a cada pedido <em>e a cada</em>
/// <see cref="Reset"/>. Quem consome o fluxo carrega o número que recebeu em <see cref="Begin"/> e só pode desenhar
/// enquanto <see cref="IsCurrent"/> for verdadeiro — um provedor lento que ignore o cancelamento continua entregando
/// texto para ninguém.</para>
/// </remarks>
public sealed class AiCompletionPreviewPresenter
{
    private long _generation;

    /// <summary>Ponto do ciclo de vida da prévia.</summary>
    public AiCompletionPreviewState State { get; private set; } = AiCompletionPreviewState.Idle;

    /// <summary>Texto acumulado até aqui; vazio enquanto o modelo não entregou nada.</summary>
    public string Text { get; private set; } = "";

    /// <summary>Documento capturado no instante do pedido; qualquer divergência invalida a prévia.</summary>
    public string Document { get; private set; } = "";

    /// <summary>Posição do cursor capturada no instante do pedido.</summary>
    public int Caret { get; private set; }

    /// <summary>Identidade do pedido corrente.</summary>
    public long Generation => _generation;

    /// <summary>Há indicador ou prévia à mostra.</summary>
    public bool IsActive => State != AiCompletionPreviewState.Idle;

    /// <summary>Há candidato final esperando <c>Tab</c>.</summary>
    public bool HasPreview => State == AiCompletionPreviewState.Preview && Text.Length > 0;

    /// <summary>Abre um pedido novo e devolve a identidade que o acompanha até o fim.</summary>
    /// <param name="document">Texto completo do editor, capturado antes de qualquer await.</param>
    /// <param name="caret">Cursor capturado junto com o texto.</param>
    public long Begin(string document, int caret)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Caret = Math.Clamp(caret, 0, document.Length);
        Text = "";
        State = AiCompletionPreviewState.Generating;
        return ++_generation;
    }

    /// <summary>
    /// O pedido está esperando a carga do modelo. Falso quando já foi substituído ou descartado.
    /// </summary>
    /// <param name="generation">Identidade devolvida por <see cref="Begin"/>.</param>
    public bool MarkLoading(long generation)
    {
        if (!IsCurrent(generation) || State == AiCompletionPreviewState.Preview) return false;
        State = AiCompletionPreviewState.Loading;
        return true;
    }

    /// <summary>Prévia parcial; falso quando o pedido já foi substituído ou descartado.</summary>
    /// <param name="generation">Identidade devolvida por <see cref="Begin"/>.</param>
    /// <param name="text">Texto acumulado até aqui.</param>
    public bool Update(long generation, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsCurrent(generation)) return false;
        // O primeiro pedaço encerra a espera pela carga: o que está acontecendo agora é geração.
        if (State == AiCompletionPreviewState.Loading) State = AiCompletionPreviewState.Generating;
        Text = text;
        return true;
    }

    /// <summary>Candidato final; falso quando o pedido já foi substituído ou descartado.</summary>
    /// <param name="generation">Identidade devolvida por <see cref="Begin"/>.</param>
    /// <param name="text">Texto do candidato.</param>
    public bool Complete(long generation, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsCurrent(generation)) return false;
        Text = text;
        State = AiCompletionPreviewState.Preview;
        return true;
    }

    /// <summary>Descarta tudo e invalida a identidade corrente.</summary>
    public void Reset()
    {
        State = AiCompletionPreviewState.Idle;
        Text = ""; Document = ""; Caret = 0;
        _generation++;
    }

    /// <summary>Se este pedido ainda é o desta prévia.</summary>
    /// <param name="generation">Identidade devolvida por <see cref="Begin"/>.</param>
    public bool IsCurrent(long generation) => IsActive && generation == _generation;
}
