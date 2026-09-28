using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>
/// Configuração do provider Claude por API Key. Contém apenas a referência opaca do cofre (nunca a chave), a lista
/// de modelos permitidos e os limites. IDs de modelo são configuração, não domínio: nada fora deste adapter depende
/// de um modelo comercial específico.
/// </summary>
public sealed record ClaudeAgentProviderOptions
{
    /// <summary>
    /// IDs de modelo validados contra a referência oficial da Claude API consultada em 24/09/2026 (tabela de
    /// modelos atuais). A disponibilidade real depende da conta: um 404 torna o modelo indisponível na sessão.
    /// </summary>
    public static IReadOnlyList<string> DefaultModelIds { get; } = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"];

    public const string DefaultModelId = "claude-opus-5";

    /// <summary>Referência da API Key no <c>ISecretStore</c>. Null: provider indisponível (<c>NotConfigured</c>).</summary>
    public SecretReference? ApiKeyReference { get; init; }

    public IReadOnlyList<string> AllowedModelIds { get; init; } = DefaultModelIds;

    /// <summary>Modelo usado quando a sessão não escolhe um; precisa estar em <see cref="AllowedModelIds"/>.</summary>
    public string? DefaultModel { get; init; } = DefaultModelId;

    /// <summary>Endpoint oficial. Alterável apenas por composição confiável (testes offline); nunca por variável de ambiente.</summary>
    public Uri BaseUrl { get; init; } = new("https://api.anthropic.com");

    public ClaudeAgentBudget Budget { get; init; } = ClaudeAgentBudget.Default;

    internal void Validate()
    {
        if (AllowedModelIds is null || AllowedModelIds.Count == 0 || AllowedModelIds.Any(static id => !IsValidModelId(id)) ||
            AllowedModelIds.Distinct(StringComparer.Ordinal).Count() != AllowedModelIds.Count)
        {
            throw new ArgumentException("A lista de modelos Claude permitidos é inválida.", nameof(AllowedModelIds));
        }

        if (DefaultModel is not null && !AllowedModelIds.Contains(DefaultModel, StringComparer.Ordinal))
        {
            throw new ArgumentException("O modelo padrão precisa estar na lista permitida.", nameof(DefaultModel));
        }

        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri || (BaseUrl.Scheme != Uri.UriSchemeHttps && !BaseUrl.IsLoopback) ||
            !string.IsNullOrEmpty(BaseUrl.UserInfo) || !string.IsNullOrEmpty(BaseUrl.Query))
        {
            throw new ArgumentException("O endpoint da Claude API precisa ser HTTPS, sem credenciais ou query.", nameof(BaseUrl));
        }

        ArgumentNullException.ThrowIfNull(Budget);
        Budget.Validate();
    }

    internal static bool IsValidModelId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or ':' or '@');
}

/// <summary>
/// Orçamento por turno e por sessão. Valores iniciais de produto, a validar por homologação real; política pode
/// apenas reduzi-los. Estourar um limite encerra o turno com código seguro, sem truncar contexto silenciosamente.
/// </summary>
public sealed record ClaudeAgentBudget
{
    public static ClaudeAgentBudget Default { get; } = new();

    /// <summary><c>max_tokens</c> de cada requisição ao modelo.</summary>
    public int MaxOutputTokensPerRequest { get; init; } = 16_000;

    /// <summary>Requisições ao modelo por turno (cada ciclo de tool gera uma nova requisição).</summary>
    public int MaxRequestsPerTurn { get; init; } = 8;

    /// <summary>Soma de tokens de entrada e saída informados pelo provider em um turno.</summary>
    public long MaxTokensPerTurn { get; init; } = 400_000;

    /// <summary>Tokens de entrada e saída acumulados pela sessão lógica.</summary>
    public long MaxTokensPerSession { get; init; } = 2_000_000;

    /// <summary>Caracteres da mensagem do usuário somada ao contexto autorizado.</summary>
    public int MaxUserInputChars { get; init; } = 64 * 1024;

    /// <summary>Histórico retido em memória (aproximação por caracteres de texto, argumentos e resultados).</summary>
    public int MaxHistoryChars { get; init; } = 2 * 1024 * 1024;

    public int MaxHistoryMessages { get; init; } = 100;

    /// <summary>Texto gerado aceito em um turno; acima disso o turno é encerrado.</summary>
    public int MaxOutputCharsPerTurn { get; init; } = 1024 * 1024;

    /// <summary>JSON de argumentos de uma tool; maior que isso a chamada é recusada antes de chegar ao runtime.</summary>
    public int MaxToolArgumentsChars { get; init; } = 256 * 1024;

    /// <summary>Resultado de tool devolvido ao modelo; maior que isso vira erro seguro, sem cortar o JSON.</summary>
    public int MaxToolResultChars { get; init; } = 1024 * 1024;

    public int MaxToolCallsPerTurn { get; init; } = 32;

    /// <summary>Prazo até o primeiro evento do stream (inclui conexão e cabeçalhos).</summary>
    public TimeSpan FirstEventTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Intervalo máximo sem eventos durante o stream.</summary>
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Espera pelos resultados de tool de uma rodada. Precisa cobrir o pior caso de uma chamada do runtime
    /// (<c>AgentRuntimeOptions.MaxToolCallDuration</c>: preparo 35 s + aprovação 120 s + 2×5 s de parada + execução
    /// 35 s = 200 s) mais a folga de 30 s; a composição recusa valores menores. O watchdog de inatividade do stream
    /// (<see cref="StreamIdleTimeout"/>) vale só durante a leitura de cada rodada, não durante esta espera.
    /// </summary>
    public TimeSpan ToolResultTimeout { get; init; } = TimeSpan.FromSeconds(240);

    /// <summary>Duração máxima do turno no adapter; o runtime aplica seu próprio orçamento.</summary>
    public TimeSpan MaxTurnDuration { get; init; } = TimeSpan.FromMinutes(10);

    internal void Validate()
    {
        if (MaxOutputTokensPerRequest is < 1 or > 128_000 || MaxRequestsPerTurn < 1 || MaxTokensPerTurn < 1 ||
            MaxTokensPerSession < MaxTokensPerTurn || MaxUserInputChars < 1 || MaxHistoryChars < MaxUserInputChars ||
            MaxHistoryMessages < 2 || MaxOutputCharsPerTurn < 1 || MaxToolArgumentsChars < 2 || MaxToolResultChars < 2 ||
            MaxToolCallsPerTurn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ClaudeAgentBudget), "Limites do provider Claude inválidos.");
        }

        foreach (var value in new[] { FirstEventTimeout, StreamIdleTimeout, ToolResultTimeout, MaxTurnDuration })
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromHours(2))
            {
                throw new ArgumentOutOfRangeException(nameof(ClaudeAgentBudget), "Prazos do provider Claude inválidos.");
            }
        }
    }
}
