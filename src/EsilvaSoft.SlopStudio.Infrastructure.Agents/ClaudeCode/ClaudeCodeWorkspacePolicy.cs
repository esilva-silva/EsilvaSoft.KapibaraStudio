namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>Por que a pasta candidata não virou o cwd (a pasta dedicada vazia é usada no lugar).</summary>
public enum ClaudeCodeWorkspaceRejection
{
    /// <summary>A candidata foi aceita (<see cref="ClaudeCodeWorkingDirectoryKind.Workspace"/>).</summary>
    None,

    /// <summary>Nenhuma pasta de workspace definida no painel "Arquivos".</summary>
    NotProvided,

    /// <summary>Caminho relativo, vazio ou com caractere de controle.</summary>
    InvalidPath,

    NotFound,

    /// <summary>A pasta (ou o destino do link) não pôde ser verificada por permissão/E/S.</summary>
    Unreadable,

    /// <summary>Raiz de um volume (ex.: <c>C:\</c>, <c>/</c>).</summary>
    VolumeRoot,

    /// <summary>O perfil inteiro do usuário.</summary>
    UserProfile,

    /// <summary>A pasta é, contém ou está dentro de uma área protegida (<see cref="ClaudeCodeWorkingDirectoryPreview.ProtectedArea"/>).</summary>
    ProtectedArea,
}

/// <summary>Área protegida envolvida numa recusa.</summary>
public enum ClaudeCodeProtectedArea
{
    None,

    /// <summary>Diretório de dados do app (inclui o LiteDB, modelos e exports).</summary>
    AppData,

    /// <summary>Arquivo LiteDB do app.</summary>
    Database,

    /// <summary><c>~/.claude</c> (configuração, transcripts e credencial do próprio Claude Code).</summary>
    ClaudeConfig,

    /// <summary><c>~/.ssh</c>.</summary>
    SshKeys,
}

/// <summary>Relação entre a pasta candidata e a área protegida que causou a recusa.</summary>
public enum ClaudeCodeProtectedRelation
{
    None,

    /// <summary>A candidata é a própria área ou fica dentro dela.</summary>
    SameOrInside,

    /// <summary>A candidata contém a área (ex.: a pasta pai do diretório de dados).</summary>
    Contains,
}

/// <summary>
/// Pasta de trabalho efetiva que uma nova sessão usaria para uma pasta candidata, sem efeito colateral: nada é criado,
/// nenhum processo é iniciado e nenhum conteúdo de arquivo é lido (só existência/links da candidata são verificados).
/// <see cref="Directory"/> é a candidata aceita (links resolvidos) ou a pasta dedicada vazia do app, que pode ainda não
/// existir (é criada só ao abrir a sessão). <see cref="DedicatedDirectoryUsable"/> é falso quando a própria pasta
/// dedicada configurada está em área protegida: nesse caso a sessão não abre (<c>InvalidConfiguration</c>).
/// </summary>
public sealed record ClaudeCodeWorkingDirectoryPreview(
    string Directory,
    ClaudeCodeWorkingDirectoryKind Kind,
    ClaudeCodeWorkspaceRejection Rejection,
    ClaudeCodeProtectedArea ProtectedArea = ClaudeCodeProtectedArea.None,
    ClaudeCodeProtectedRelation Relation = ClaudeCodeProtectedRelation.None,
    bool DedicatedDirectoryUsable = true)
{
    /// <summary>Com pasta dedicada, toda leitura nativa é <c>ask</c> (negada sem ferramenta de aprovação).</summary>
    public bool ReadsRequireApproval => Kind == ClaudeCodeWorkingDirectoryKind.Dedicated;
}

/// <summary>
/// Escolha do cwd do turno (ADR-054 revisada): a pasta de workspace do usuário quando ela é segura; senão a pasta
/// dedicada vazia do app. Nunca o diretório de dados, o LiteDB, <c>~/.claude</c>, <c>~/.ssh</c>, o perfil inteiro ou
/// a raiz de um volume — nem uma pasta que contenha algum deles.
/// </summary>
internal static class ClaudeCodeWorkspacePolicy
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Mesma decisão de <see cref="Resolve"/>, sem criar a pasta dedicada nem lançar: usada pela UI para mostrar, antes
    /// de abrir a sessão, qual pasta a CLI poderá ler e por que uma candidata foi recusada.
    /// </summary>
    public static ClaudeCodeWorkingDirectoryPreview Preview(ClaudeCodeAgentProviderOptions options, string homeDirectory, string? requested)
    {
        var home = Normalize(homeDirectory);
        var areas = ProtectedAreas(options, home);
        var evaluation = Evaluate(requested, home, areas);
        if (evaluation.Rejection == ClaudeCodeWorkspaceRejection.None)
        {
            return new(evaluation.Directory!, ClaudeCodeWorkingDirectoryKind.Workspace, ClaudeCodeWorkspaceRejection.None);
        }

        var dedicated = Normalize(options.ResolveDedicatedWorkingDirectory());
        var dedicatedUsable = Check(dedicated, home, areas).Rejection == ClaudeCodeWorkspaceRejection.None;
        return new(dedicated, ClaudeCodeWorkingDirectoryKind.Dedicated, evaluation.Rejection, evaluation.Area, evaluation.Relation,
            dedicatedUsable);
    }

    /// <summary>
    /// Resolve o cwd de uma sessão a partir da pasta de workspace já capturada pelo chamador (<paramref name="requested"/>;
    /// esta classe nunca lê estado de UI). Cria a pasta dedicada só quando <paramref name="createDedicated"/> é verdadeiro
    /// (criação de sessão); consultas de estado usam <see cref="ProbeWorkingDirectory"/> e não criam nada.
    /// </summary>
    public static (string Directory, ClaudeCodeWorkingDirectoryKind Kind) Resolve(
        ClaudeCodeAgentProviderOptions options, string homeDirectory, string? requested, bool createDedicated = true)
    {
        var preview = Preview(options, homeDirectory, requested);
        if (preview.Kind == ClaudeCodeWorkingDirectoryKind.Workspace)
        {
            return (preview.Directory, preview.Kind);
        }

        if (!preview.DedicatedDirectoryUsable)
        {
            throw new InvalidOperationException("A pasta dedicada do Claude Code não pode ficar em área protegida.");
        }

        if (createDedicated)
        {
            Directory.CreateDirectory(preview.Directory);
        }

        return (preview.Directory, preview.Kind);
    }

    /// <summary>
    /// cwd de consultas curtas (<c>--version</c>, <c>auth status</c>, login/logout): a pasta dedicada se já existir, senão
    /// a pasta temporária do usuário. Não cria diretórios nem lê a pasta de workspace. Com <c>--setting-sources user</c>
    /// o cwd não altera a autenticação observada (spike: fontes de projeto excluídas).
    /// </summary>
    public static string ProbeWorkingDirectory(ClaudeCodeAgentProviderOptions options)
    {
        try
        {
            var dedicated = Normalize(options.ResolveDedicatedWorkingDirectory());
            if (Directory.Exists(dedicated))
            {
                return dedicated;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
        }

        return Normalize(Path.GetTempPath());
    }

    /// <summary>Compatibilidade de testes: áreas na ordem dados, LiteDB, <c>.claude</c>, <c>.ssh</c>.</summary>
    internal static string? TryAcceptWorkspace(string? requested, string home, IReadOnlyList<string> protectedPaths)
    {
        ClaudeCodeProtectedArea[] order = [ClaudeCodeProtectedArea.AppData, ClaudeCodeProtectedArea.Database,
            ClaudeCodeProtectedArea.ClaudeConfig, ClaudeCodeProtectedArea.SshKeys];
        var areas = protectedPaths.Select((path, index) => (path, index < order.Length ? order[index] : ClaudeCodeProtectedArea.AppData)).ToArray();
        var evaluation = Evaluate(requested, Normalize(home), areas);
        return evaluation.Rejection == ClaudeCodeWorkspaceRejection.None ? evaluation.Directory : null;
    }

    private static (string Path, ClaudeCodeProtectedArea Area)[] ProtectedAreas(ClaudeCodeAgentProviderOptions options, string home) =>
    [
        (Normalize(options.ResolveAppDataDirectory()), ClaudeCodeProtectedArea.AppData),
        (Normalize(options.ResolveDatabasePath()), ClaudeCodeProtectedArea.Database),
        (Path.Combine(home, ".claude"), ClaudeCodeProtectedArea.ClaudeConfig),
        (Path.Combine(home, ".ssh"), ClaudeCodeProtectedArea.SshKeys),
    ];

    private readonly record struct Evaluation(
        string? Directory, ClaudeCodeWorkspaceRejection Rejection, ClaudeCodeProtectedArea Area = ClaudeCodeProtectedArea.None,
        ClaudeCodeProtectedRelation Relation = ClaudeCodeProtectedRelation.None);

    private static Evaluation Evaluate(string? requested, string home, IReadOnlyList<(string Path, ClaudeCodeProtectedArea Area)> areas)
    {
        if (requested is null || string.IsNullOrWhiteSpace(requested))
        {
            return new(null, ClaudeCodeWorkspaceRejection.NotProvided);
        }

        if (!Path.IsPathFullyQualified(requested) || requested.Any(char.IsControl))
        {
            return new(null, ClaudeCodeWorkspaceRejection.InvalidPath);
        }

        string full;
        try
        {
            full = Normalize(requested);
            if (PathEquals(full, Path.GetPathRoot(full) ?? string.Empty))
            {
                // Antes de resolver links: a raiz de um volume não é um link e a consulta pode falhar por permissão.
                return new(null, ClaudeCodeWorkspaceRejection.VolumeRoot);
            }

            if (!Directory.Exists(full))
            {
                return new(null, ClaudeCodeWorkspaceRejection.NotFound);
            }

            // Um link simbólico é avaliado pelo destino final: o apelido não contorna as áreas protegidas.
            if (new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                full = Normalize(target.FullName);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(null, ClaudeCodeWorkspaceRejection.Unreadable);
        }

        return Check(full, home, areas);
    }

    private static Evaluation Check(string full, string home, IReadOnlyList<(string Path, ClaudeCodeProtectedArea Area)> areas)
    {
        if (PathEquals(full, Path.GetPathRoot(full) ?? string.Empty))
        {
            return new(null, ClaudeCodeWorkspaceRejection.VolumeRoot);
        }

        if (PathEquals(full, home))
        {
            return new(null, ClaudeCodeWorkspaceRejection.UserProfile);
        }

        foreach (var (path, area) in areas)
        {
            if (IsSameOrInside(full, path))
            {
                return new(null, ClaudeCodeWorkspaceRejection.ProtectedArea, area, ClaudeCodeProtectedRelation.SameOrInside);
            }

            if (IsSameOrInside(path, full))
            {
                return new(null, ClaudeCodeWorkspaceRejection.ProtectedArea, area, ClaudeCodeProtectedRelation.Contains);
            }
        }

        return new(full, ClaudeCodeWorkspaceRejection.None);
    }

    internal static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), Comparison);

    /// <summary>True quando <paramref name="path"/> é <paramref name="container"/> ou está dentro dele.</summary>
    internal static bool IsSameOrInside(string path, string container)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var normalizedContainer = Path.TrimEndingDirectorySeparator(container);
        if (PathEquals(normalizedPath, normalizedContainer))
        {
            return true;
        }

        var prefix = normalizedContainer.EndsWith(Path.DirectorySeparatorChar) ? normalizedContainer : normalizedContainer + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, Comparison);
    }
}
