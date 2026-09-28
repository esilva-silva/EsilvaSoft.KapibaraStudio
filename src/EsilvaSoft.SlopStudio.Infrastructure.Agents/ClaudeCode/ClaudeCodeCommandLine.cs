using System.Globalization;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>Onde o turno roda: pasta de workspace do usuário ou pasta dedicada vazia do app.</summary>
public enum ClaudeCodeWorkingDirectoryKind
{
    /// <summary>Pasta dedicada vazia; toda leitura é <c>ask</c> e, sem ferramenta de aprovação, é negada.</summary>
    Dedicated,

    /// <summary>Pasta de workspace do usuário; Read/Glob/Grep dentro dela não pedem aprovação (ADR-054 revisada).</summary>
    Workspace,
}

/// <summary>Configuração de execução fixada na criação da sessão (cwd, regras e argv globais).</summary>
internal sealed record ClaudeCodeLaunchProfile(
    string ExecutablePath,
    ClaudeCodeVersion Version,
    string WorkingDirectory,
    ClaudeCodeWorkingDirectoryKind WorkingDirectoryKind,
    string SettingsJson,
    string Model)
{
    /// <summary>
    /// Regras <c>deny</c> fixas do app (diretório de dados, LiteDB, <c>~/.claude</c>, <c>~/.ssh</c>), somadas às do plano
    /// em cada turno. <see cref="SettingsJson"/> é a forma sem plano (consultas de estado); o turno usa a do plano.
    /// </summary>
    public IReadOnlyList<string> FixedDenyRules { get; init; } = [];

    /// <summary>
    /// Flags globais compartilhadas pelo <c>auth status</c> preventivo e pelo turno: são as que alteram a
    /// autenticação observada (spike: <c>auth status</c> reflete <c>--settings</c>/<c>--setting-sources</c>).
    /// </summary>
    public IReadOnlyList<string> GlobalArguments => ["--setting-sources", "user", "--settings", SettingsJson];
}

/// <summary>
/// Argv do Claude Code montado só com valores validados (nunca texto do usuário/modelo sem validação) e passado por
/// <c>ArgumentList</c>, sem shell. Sem <c>--bare</c> (nunca usa a assinatura) e sem <c>--console</c>.
/// </summary>
internal static class ClaudeCodeCommandLine
{
    /// <summary>Limite do prompt de sistema em argv (o builder do produto gera menos de 2 KB; folga para evolução).</summary>
    public const int MaxSystemPromptChars = 4096;

    public static IReadOnlyList<string> VersionArguments { get; } = ["--version"];

    public static IReadOnlyList<string> LoginArguments { get; } = ["auth", "login"];

    public static IReadOnlyList<string> LogoutArguments { get; } = ["auth", "logout"];

    /// <summary>Nome da ferramenta de aprovação do produto no namespace MCP da CLI (<c>mcp__slopstudio__approve</c>).</summary>
    public static string PermissionPromptToolName { get; } = McpServerLaunchSpec.ToolName(AgentToolRegistry.ApproveToolName);

    public static IReadOnlyList<string> AuthStatusArguments(ClaudeCodeLaunchProfile? profile) =>
        profile is null ? ["auth", "status"] : [.. profile.GlobalArguments, "auth", "status"];

    /// <summary>
    /// Turno: um processo por mensagem, stream-json nos dois sentidos e <c>--permission-mode default</c> sempre. As
    /// ferramentas nativas são exatamente as do plano (<c>--tools ""</c> desliga todas, forma documentada da CLI); o MCP
    /// é só o servidor do produto (<c>--strict-mcp-config</c> sempre, <c>--mcp-config</c> inline só quando o plano precisa
    /// do canal); a ferramenta de aprovação entra só quando o plano exige confirmação; o prompt de sistema do produto vai
    /// em todo turno, inclusive com <c>--resume</c>. Hooks desligados e fontes só do usuário (flags globais).
    /// </summary>
    public static IReadOnlyList<string> TurnArguments(
        ClaudeCodeLaunchProfile profile, int maxTurns, string sessionId, bool resume, ClaudeCodeTurnSetup setup, string systemPrompt)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(setup);
        if (!Guid.TryParseExact(sessionId, "D", out _))
        {
            throw new ArgumentException("Sessão do Claude Code inválida.", nameof(sessionId));
        }

        if (!IsSafeSystemPrompt(systemPrompt))
        {
            throw new ArgumentException("Prompt de sistema inválido.", nameof(systemPrompt));
        }

        List<string> arguments =
        [
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--include-partial-messages",
            "--verbose",
            "--permission-mode", "default",
            "--strict-mcp-config",
        ];
        if (setup.McpConfigJson is { } mcpConfig)
        {
            arguments.Add("--mcp-config");
            arguments.Add(mcpConfig);
        }

        arguments.Add("--tools");
        arguments.Add(string.Join(',', setup.NativeTools));
        if (setup.PermissionPromptTool is { } promptTool)
        {
            arguments.Add("--permission-prompt-tool");
            arguments.Add(promptTool);
        }

        arguments.AddRange(
        [
            "--append-system-prompt", systemPrompt,
            "--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture),
            "--model", profile.Model,
            resume ? "--resume" : "--session-id", sessionId,
            .. profile.GlobalArguments,
        ]);
        return arguments;
    }

    /// <summary>
    /// Prompt de sistema aceito em argv: não vazio, até <see cref="MaxSystemPromptChars"/>, sem controle além de
    /// tab/CR/LF, sem surrogate isolado nem U+FFFD, e sem começar por <c>-</c> (nunca confundido com uma opção).
    /// </summary>
    public static bool IsSafeSystemPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxSystemPromptChars || prompt[0] == '-')
        {
            return false;
        }

        for (var index = 0; index < prompt.Length; index++)
        {
            var c = prompt[index];
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 >= prompt.Length || !char.IsLowSurrogate(prompt[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(c) || c == '\uFFFD' || (char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>--mcp-config</c> inline com um único servidor STDIO, o proxy do produto. Os argumentos são identificadores sem
    /// segredo (o proof do canal fica no cofre do SO e é lido pelo próprio proxy).
    /// </summary>
    public static string BuildMcpConfigJson(McpServerLaunchSpec launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("mcpServers");
            writer.WriteStartObject(launch.ServerName);
            writer.WriteString("type", "stdio");
            writer.WriteString("command", launch.Command);
            writer.WriteStartArray("args");
            foreach (var argument in launch.Args)
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Alias ou ID de modelo: letras, dígitos, <c>.</c>, <c>-</c>, <c>_</c>, <c>[</c>, <c>]</c>; nunca começa com <c>-</c>.</summary>
    public static bool IsSafeModelId(string? model) =>
        model is { Length: > 0 and <= 128 } && char.IsAsciiLetterOrDigit(model[0]) &&
        model.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '[' or ']');

    /// <summary>
    /// <c>--settings</c> do Slop sem plano (consultas de estado): hooks desligados, <c>deny</c> determinístico para o
    /// diretório de dados do app, o LiteDB, <c>~/.claude</c> e <c>~/.ssh</c>, e <c>ask</c> para toda leitura quando não há
    /// pasta de workspace. As regras de <c>Read</c> também valem para Glob/Grep (documentação; confirmado para Glob no spike).
    /// </summary>
    public static string BuildSettingsJson(ClaudeCodeWorkingDirectoryKind kind, IReadOnlyList<string> denyRules) =>
        BuildSettingsJson(denyRules,
            kind == ClaudeCodeWorkingDirectoryKind.Dedicated ? ClaudeCodeAgentProviderOptions.NativeToolAllowlist : [], []);

    /// <summary>
    /// <c>--settings</c> com as listas já decididas. Na CLI <c>deny</c> prevalece sobre <c>ask</c>, que prevalece sobre
    /// <c>allow</c>: as regras <c>ask</c> do plano valem inclusive contra um <c>allow</c> das configurações do usuário.
    /// <c>ask</c>/<c>allow</c> vazios são omitidos; <c>deny</c> sempre aparece.
    /// </summary>
    public static string BuildSettingsJson(IReadOnlyList<string> denyRules, IReadOnlyList<string> askRules, IReadOnlyList<string> allowRules)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("disableAllHooks", true);
            writer.WriteStartObject("permissions");
            WriteRules(writer, "deny", denyRules, always: true);
            WriteRules(writer, "ask", askRules, always: false);
            WriteRules(writer, "allow", allowRules, always: false);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());

        static void WriteRules(Utf8JsonWriter writer, string name, IReadOnlyList<string> rules, bool always)
        {
            if (rules.Count == 0 && !always)
            {
                return;
            }

            writer.WriteStartArray(name);
            foreach (var rule in rules.Distinct(StringComparer.Ordinal))
            {
                writer.WriteStringValue(rule);
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Regras <c>Read(...)</c> para caminhos protegidos. Dentro do perfil usa <c>~/</c> (independe de caracteres do nome
    /// de usuário); fora dele usa a forma absoluta <c>//</c> (no Windows, <c>C:\x</c> vira <c>//c/x</c>, normalização
    /// POSIX documentada). Caminho com caractere especial de padrão é recusado em vez de gerar regra ambígua.
    /// </summary>
    public static IReadOnlyList<string> BuildDenyRules(string appDataDirectory, string databasePath, string homeDirectory)
    {
        List<string> rules =
        [
            ReadRule(appDataDirectory, homeDirectory, directory: true),
            ReadRule(databasePath, homeDirectory, directory: false),
            "Read(~/.claude/**)",
            "Read(~/.ssh/**)",
        ];
        return [.. rules.Distinct(StringComparer.Ordinal)];
    }

    private static string ReadRule(string path, string home, bool directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var homeFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        string pattern;
        if (ClaudeCodeWorkspacePolicy.IsSameOrInside(full, homeFull) && !ClaudeCodeWorkspacePolicy.PathEquals(full, homeFull))
        {
            pattern = "~/" + Path.GetRelativePath(homeFull, full).Replace('\\', '/');
        }
        else if (OperatingSystem.IsWindows() && full.Length >= 2 && full[1] == ':')
        {
            pattern = "//" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
        }
        else
        {
            pattern = "/" + full.Replace('\\', '/');
        }

        if (pattern.Any(static c => c is '(' or ')' or '*' or '?' or '[' or ']' or '{' or '}' or '!' or '"' || char.IsControl(c)))
        {
            throw new InvalidOperationException("Caminho protegido com caractere não suportado em regra de permissão.");
        }

        return "Read(" + pattern + (directory ? "/**)" : ")");
    }
}
