using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Configuração do modo "Claude (assinatura)", que executa o binário oficial do Claude Code instalado pelo usuário
/// (ADR-053). Nada aqui contém credencial: a autenticação pertence inteiramente ao processo oficial.
/// </summary>
public sealed class ClaudeCodeAgentProviderOptions
{
    /// <summary>
    /// Versão mínima aceita. As versões mínimas documentadas das funções usadas vão até 2.1.223 (entrada stream-json no
    /// Windows 2.1.211, <c>capabilities</c> no <c>init</c> 2.1.205, <c>--resume</c> entre diretórios 2.1.223), mas
    /// <c>--tools</c>, <c>--setting-sources</c> e <c>--strict-mcp-config</c> não têm versão publicada e só a 2.1.268 foi
    /// exercitada no spike P7-CL0-01. Até existir matriz de versões, a mínima é a única comprovada.
    /// </summary>
    public static ClaudeCodeVersion DefaultMinimumVersion { get; } = new(2, 1, 268);

    /// <summary>
    /// Allowlist exata de ferramentas nativas admitidas pela política do produto. Subagents e ferramentas não listadas
    /// permanecem indisponíveis. Ferramentas de comando, escrita e rede começam desabilitadas nas permissões.
    /// </summary>
    public static IReadOnlyList<string> NativeToolAllowlist { get; } =
        ["Read", "Glob", "Grep", "Bash", "WebSearch", "WebFetch"];

    /// <summary>Caminho absoluto de <c>claude</c> escolhido pelo usuário; nulo procura no PATH e em locais conhecidos.</summary>
    public string? ExecutablePath { get; init; }

    public ClaudeCodeVersion MinimumVersion { get; init; } = DefaultMinimumVersion;

    /// <summary>Aliases oficiais aceitos em <c>--model</c>; texto do usuário ou do modelo nunca escolhe outro valor.</summary>
    public IReadOnlyList<string> AllowedModelIds { get; init; } = ["sonnet", "opus", "haiku"];

    public string? DefaultModel { get; init; } = "sonnet";

    /// <summary>Limite de turnos internos por mensagem (<c>--max-turns</c>), evita loop de ferramentas sem fim.</summary>
    public int MaxTurns { get; init; } = 8;

    /// <summary>Prazo de cada consulta curta (<c>--version</c>, <c>auth status</c>).</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Prazo total de um turno; esgotado encerra a árvore de processos.</summary>
    public TimeSpan MaxTurnDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Espera pelo término do processo depois do <c>result</c>, antes de encerrar a árvore.</summary>
    public TimeSpan ExitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Prazo do login/logout em janela visível; esgotado, o app só reconsulta o estado.</summary>
    public TimeSpan AccountCommandTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public int MaxLineBytes { get; init; } = 4 * 1024 * 1024;

    public long MaxTurnOutputBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Linhas descartadas (grandes demais ou JSON inválido) toleradas por turno antes de abortar.</summary>
    public int MaxDiscardedLines { get; init; } = 8;

    public int MaxStderrBytes { get; init; } = 16 * 1024;

    public int MaxProbeOutputBytes { get; init; } = 64 * 1024;

    public int MaxUserInputChars { get; init; } = 100_000;

    /// <summary>
    /// Pasta de diagnósticos do Claude Code em compilações Debug. Nula desliga o registro (incluindo testes que não
    /// optam por ele). A composição Desktop aponta para logs/ na raiz do repositório quando ela existe.
    /// </summary>
    public string? DebugLogDirectory { get; init; }

    /// <summary>
    /// Compatibilidade: fonte da pasta de workspace quando o chamador não informa
    /// <c>AgentSessionOptions.WorkingDirectory</c> (o contrato preferido, capturado na thread de UI). Só é invocada de
    /// forma síncrona no início de <c>CreateSessionAsync</c>, antes do primeiro await; consultas de estado nunca a leem.
    /// Pasta nula ou recusada (protegida, raiz, perfil do usuário) usa a pasta dedicada, onde toda leitura exige aprovação.
    /// </summary>
    public Func<string?>? WorkspaceDirectory { get; init; }

    /// <summary>Pasta vazia do app usada como cwd sem workspace. Nula usa o padrão (irmã, fora do diretório de dados).</summary>
    public string? DedicatedWorkingDirectory { get; init; }

    /// <summary>Diretório de dados do app (LiteDB, modelos, exports). Nulo usa <see cref="LocalWorkspacePaths"/>.</summary>
    public string? AppDataDirectory { get; init; }

    /// <summary>Arquivo LiteDB do app. Nulo usa <see cref="LocalWorkspacePaths.GetDatabasePath"/>.</summary>
    public string? DatabasePath { get; init; }

    /// <summary>
    /// Ponto de teste para decidir se uma variável de ambiente está definida (somente o nome é consultado; o valor
    /// nunca é guardado). Nulo usa o ambiente do processo, o mesmo herdado pelo filho.
    /// </summary>
    internal Func<string, bool>? IsEnvironmentVariableSet { get; init; }

    internal string ResolveAppDataDirectory() =>
        AppDataDirectory ?? Path.GetDirectoryName(ResolveDatabasePath())!;

    internal string ResolveDatabasePath() => DatabasePath ?? LocalWorkspacePaths.GetDatabasePath();

    /// <summary>
    /// Padrão: <c>…/EsilvaSoft/KapibaraStudio.ClaudeCode/empty</c>, irmã do diretório de dados (nunca dentro dele).
    /// </summary>
    internal string ResolveDedicatedWorkingDirectory()
    {
        if (DedicatedWorkingDirectory is { } configured)
        {
            return configured;
        }

        var data = Path.TrimEndingDirectorySeparator(ResolveAppDataDirectory());
        var parent = Path.GetDirectoryName(data) ?? data;
        return Path.Combine(parent, Path.GetFileName(data) + ".ClaudeCode", "empty");
    }

    public void Validate()
    {
        if (ExecutablePath is not null && !Path.IsPathFullyQualified(ExecutablePath))
        {
            throw new ArgumentException("O caminho do Claude Code precisa ser absoluto.", nameof(ExecutablePath));
        }

        if (AllowedModelIds is null || AllowedModelIds.Count == 0 || AllowedModelIds.Any(static id => !ClaudeCodeCommandLine.IsSafeModelId(id)) ||
            AllowedModelIds.Distinct(StringComparer.Ordinal).Count() != AllowedModelIds.Count)
        {
            throw new ArgumentException("Modelos permitidos inválidos.", nameof(AllowedModelIds));
        }

        if (DefaultModel is not null && !AllowedModelIds.Contains(DefaultModel, StringComparer.Ordinal))
        {
            throw new ArgumentException("O modelo padrão não está na lista permitida.", nameof(DefaultModel));
        }

        if (MaxTurns is < 1 or > 100 || MaxLineBytes < 4096 || MaxTurnOutputBytes < MaxLineBytes || MaxDiscardedLines < 0 ||
            MaxStderrBytes < 256 || MaxProbeOutputBytes < 1024 || MaxUserInputChars < 1)
        {
            throw new ArgumentException("Limites do Claude Code inválidos.");
        }

        if (ProbeTimeout <= TimeSpan.Zero || MaxTurnDuration <= TimeSpan.Zero || ExitTimeout <= TimeSpan.Zero ||
            AccountCommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Prazos do Claude Code inválidos.");
        }

        foreach (var path in new[] { DedicatedWorkingDirectory, AppDataDirectory, DatabasePath, DebugLogDirectory })
        {
            if (path is not null && !Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("Caminhos configurados do Claude Code precisam ser absolutos.");
            }
        }
    }
}
