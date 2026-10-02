using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>Método efetivo de autenticação observado na CLI oficial, que controla a credencial e a cobrança.</summary>
public enum ClaudeCodeAuthKind
{
    /// <summary>Conta claude.ai com assinatura, sem chave de API em uso.</summary>
    Subscription,

    NotLoggedIn,

    /// <summary><c>apiKeySource</c> presente (ex.: <c>ANTHROPIC_API_KEY</c>): cobrança pela API.</summary>
    ApiKey,

    /// <summary><c>apiKeyHelper</c> de configuração: cobrança pela API.</summary>
    ApiKeyHelper,

    /// <summary>Token no ambiente (<c>ANTHROPIC_AUTH_TOKEN</c> ou <c>CLAUDE_CODE_OAUTH_TOKEN</c>), indistinguíveis por status.</summary>
    EnvironmentToken,

    /// <summary>Provedor de nuvem (Bedrock, Vertex, Foundry) ou outro <c>apiProvider</c>.</summary>
    CloudProvider,

    /// <summary>Variável de ambiente que muda cobrança/destino ou indica outra sessão Claude Code hospedeira.</summary>
    BlockedEnvironment,

    /// <summary>Logado por método desconhecido ou conta sem assinatura.</summary>
    UnsupportedMethod,

    /// <summary>Saída ausente, grande demais, não JSON ou sem os campos obrigatórios.</summary>
    Unreadable,
}

/// <summary>
/// Resultado de <c>claude auth status</c> filtrado por allowlist de campos: nunca contém e-mail, organização, IDs de
/// conta, diretórios ou tokens. <see cref="EnvironmentVariableName"/> e <see cref="ApiKeySource"/> são somente nomes.
/// </summary>
public sealed record ClaudeCodeAuthStatus(
    ClaudeCodeAuthKind Kind,
    string? AuthMethod = null,
    string? ApiProvider = null,
    string? SubscriptionType = null,
    string? ApiKeySource = null,
    string? EnvironmentVariableName = null)
{
    public bool IsSubscription => Kind == ClaudeCodeAuthKind.Subscription;

    /// <summary>Somente nomes conhecidos de variáveis de autenticação presentes; nunca seus valores.</summary>
    public string CredentialEnvironment { get; init; } = string.Empty;

    internal static string CaptureCredentialEnvironment(Func<string, bool> isSet) =>
        string.Join(',', CredentialEnvironmentVariables.Where(isSet));

    private static readonly string[] CredentialEnvironmentVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
        "ANTHROPIC_PROFILE", "ANTHROPIC_FEDERATION_RULE_ID", "ANTHROPIC_ORGANIZATION_ID",
    ];

    private static readonly string[] AuthenticationFields = ["authMethod", "apiProvider", "apiKeySource", "subscriptionType"];

    /// <summary>CLI reports an authenticated native method; the product does not choose or emulate its billing mode.</summary>
    public bool IsAuthenticated => Kind is not (ClaudeCodeAuthKind.NotLoggedIn or ClaudeCodeAuthKind.BlockedEnvironment or ClaudeCodeAuthKind.Unreadable);

    /// <summary>Opaque, allowlisted effective auth identity for a session; never contains secret material.</summary>
    public bool SameEffectiveAuthentication(ClaudeCodeAuthStatus? other) => other is not null &&
        Kind == other.Kind &&
        string.Equals(AuthMethod, other.AuthMethod, StringComparison.Ordinal) &&
        string.Equals(ApiProvider, other.ApiProvider, StringComparison.Ordinal) &&
        string.Equals(ApiKeySource, other.ApiKeySource, StringComparison.Ordinal) &&
        string.Equals(SubscriptionType, other.SubscriptionType, StringComparison.Ordinal) &&
        string.Equals(CredentialEnvironment, other.CredentialEnvironment, StringComparison.Ordinal);

    /// <summary>
    /// Variáveis bloqueadas por NOME (o app não as remove, injeta nem lê o valor; presença com valor vazio conta):
    /// destino e transporte (endpoint, cabeçalhos,
    /// proxy, CAs/TLS do runtime Node), troca silenciosa de modelo, diretório de configuração alternativo e sessão
    /// Claude Code hospedeira (risco R-CL-02). <c>http_proxy</c>/<c>https_proxy</c> minúsculas contam no Linux.
    /// Limitação registrada: o bloco <c>env</c> do <c>~/.claude/settings.json</c> do usuário (fonte <c>user</c>) não é
    /// verificável por nome sem ler esse arquivo, o que o app não faz. A detecção efetiva cobre o que a CLI informa:
    /// <c>apiKeySource</c>/<c>apiProvider</c>/<c>authMethod</c> no <c>auth status</c> e <c>apiKeySource</c>/<c>model</c>
    /// no <c>init</c>; um <c>ANTHROPIC_BASE_URL</c> ou proxy definido nesse bloco NÃO aparece em nenhum campo observado
    /// (risco residual, ver M5–M7 pendentes de decisão).
    /// </summary>
    public static IReadOnlyList<string> BlockingEnvironmentVariables { get; } =
    [
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_CUSTOM_HEADERS",
        "ANTHROPIC_MODEL",
        "ANTHROPIC_DEFAULT_SONNET_MODEL",
        "ANTHROPIC_DEFAULT_OPUS_MODEL",
        "ANTHROPIC_DEFAULT_HAIKU_MODEL",
        "ANTHROPIC_SMALL_FAST_MODEL",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "http_proxy",
        "https_proxy",
        "NODE_EXTRA_CA_CERTS",
        "NODE_TLS_REJECT_UNAUTHORIZED",
        "CLAUDE_CONFIG_DIR",
        "CLAUDECODE",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    /// <summary>Primeira variável bloqueante presente (só o nome), ou nulo. Valor vazio conta como presente.</summary>
    internal static string? FindBlockingEnvironmentVariable(Func<string, bool> isSet)
    {
        return BlockingEnvironmentVariables.FirstOrDefault(isSet);
    }

    /// <summary>Definida conta como presente mesmo vazia (no Linux <c>VAR=</c> existe); o valor é descartado na hora.</summary>
    internal static bool IsPresent(string? value) => value is not null;

    /// <summary>
    /// Classifica apenas os campos allowlistados de status. Todos os métodos suportados pela CLI oficial são delegados;
    /// o painel não infere tier nem substitui a decisão de autenticação/cobrança do processo.
    /// </summary>
    public static ClaudeCodeAuthStatus Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output.Length > 64 * 1024)
        {
            return new(ClaudeCodeAuthKind.Unreadable);
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new(ClaudeCodeAuthKind.Unreadable);
            }

            // Allowlist: somente estes campos são lidos; qualquer outro (email, orgId, orgName, diretórios) é ignorado.
            var authMethod = SafeToken(root, "authMethod");
            var apiProvider = SafeToken(root, "apiProvider");
            var apiKeySource = root.TryGetProperty("apiKeySource", out var source) && source.ValueKind != JsonValueKind.Null
                ? SafeToken(root, "apiKeySource") ?? "unknown"
                : null;
            if (string.Equals(apiKeySource, "none", StringComparison.Ordinal)) apiKeySource = null;
            var subscriptionType = SafeToken(root, "subscriptionType");

            if (!loggedIn.GetBoolean())
            {
                return new(ClaudeCodeAuthKind.NotLoggedIn, authMethod, apiProvider);
            }

            // loggedIn sozinho não identifica o método ou a cobrança. Métodos futuros são delegados quando a CLI
            // informa um identificador público válido; campos quebrados não podem virar uma autenticação genérica.
            if (AuthenticationFields.Any(name =>
                    root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && SafeToken(root, name) is null) ||
                (authMethod is null && apiKeySource is null && apiProvider is null or "firstParty"))
            {
                return new(ClaudeCodeAuthKind.Unreadable);
            }

            var kind = Classify(authMethod, apiProvider, apiKeySource, subscriptionType);
            return new(kind, authMethod, apiProvider, subscriptionType, apiKeySource);
        }
        catch (JsonException)
        {
            return new(ClaudeCodeAuthKind.Unreadable);
        }
    }

    private static ClaudeCodeAuthKind Classify(string? authMethod, string? apiProvider, string? apiKeySource, string? subscriptionType)
    {
        if (apiProvider is not null && !string.Equals(apiProvider, "firstParty", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.CloudProvider;
        }

        if (string.Equals(authMethod, "api_key_helper", StringComparison.Ordinal) ||
            string.Equals(apiKeySource, "apiKeyHelper", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.ApiKeyHelper;
        }

        if (apiKeySource is not null)
        {
            return ClaudeCodeAuthKind.ApiKey;
        }

        if (string.Equals(authMethod, "oauth_token", StringComparison.Ordinal))
        {
            return ClaudeCodeAuthKind.EnvironmentToken;
        }

        return string.Equals(authMethod, "claude.ai", StringComparison.Ordinal) && subscriptionType is not null
            ? ClaudeCodeAuthKind.Subscription
            : ClaudeCodeAuthKind.UnsupportedMethod;
    }

    /// <summary>Valor curto só com <c>[A-Za-z0-9._-]</c>; qualquer outro conteúdo é descartado.</summary>
    private static string? SafeToken(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 and <= 64 } text &&
        text.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? text
            : null;
}
