using System.Runtime.InteropServices;
using EsilvaSoft.KapibaraStudio.Application;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Configuração única do runtime headless oficial do SDK para consultas e sessões.</summary>
internal static class CopilotRuntimeSettings
{
    private static readonly string[] WindowsChildEnvironmentNames =
        ["PATH", "SystemRoot", "WINDIR", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "COMSPEC"];
    private static readonly string[] UnixChildEnvironmentNames =
        ["PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "LANG", "LC_ALL", "XDG_RUNTIME_DIR",
         "DBUS_SESSION_BUS_ADDRESS", "DISPLAY", "WAYLAND_DISPLAY", "XAUTHORITY", "TERM"];

    public static Dictionary<string, string> ChildEnvironment()
    {
        var platform = new LocalHostPlatformSnapshot();
        var source = new Dictionary<string, string?>(ChildEnvironmentComparer(platform));
        foreach (var name in ChildEnvironmentNames(platform))
        {
            source[name] = Environment.GetEnvironmentVariable(name);
        }

        return BuildChildEnvironment(source, platform);
    }

    internal static Dictionary<string, string> BuildChildEnvironment(IEnumerable<KeyValuePair<string, string?>> source,
        IHostPlatformSnapshot platform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(platform);
        var comparer = ChildEnvironmentComparer(platform);
        var environment = new Dictionary<string, string>(comparer);
        var allowed = ChildEnvironmentNames(platform).ToHashSet(comparer);
        foreach (var (name, value) in source)
        {
            if (allowed.Contains(name) && value is { Length: > 0 })
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    private static string[] ChildEnvironmentNames(IHostPlatformSnapshot platform) =>
        platform.IsWindows ? WindowsChildEnvironmentNames : UnixChildEnvironmentNames;

    private static StringComparer ChildEnvironmentComparer(IHostPlatformSnapshot platform) =>
        platform.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Client mode for explicit account/model discovery through the official CLI login.</summary>
    public static CopilotClientOptions AccountClientOptions() => CommonClientOptions(CopilotClientMode.CopilotCli);

    /// <summary>Uses the logged-in user's official CLI account; each product session still supplies an explicit tool allowlist.</summary>
    public static CopilotClientOptions SessionClientOptions(string? workingDirectory = null,
        SessionFsConfig? sessionFs = null) => CommonClientOptions(CopilotClientMode.CopilotCli, workingDirectory, sessionFs);

    /// <summary>
    /// Explicitly disables ambient session features that the CLI-compatible mode otherwise inherits.
    /// Product tools remain supplied by the caller through the session's exact allowlist.
    /// </summary>
#pragma warning disable GHCP001 // These experimental capability switches are used only to disable optional SDK features.
    public static void ApplyProductSessionDefaults(SessionConfigBase config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.EnableConfigDiscovery = false;
        config.EnableExperimentalMode = false;
        config.EnableSessionTelemetry = false;
        config.EnableFileChangeTracking = false;
        config.SkipEmbeddingRetrieval = true;
        config.EmbeddingCacheStorage = EmbeddingCacheStorageMode.InMemory;
        config.SkipCustomInstructions = true;
        config.EnableOnDemandInstructionDiscovery = false;
        config.EnableFileHooks = false;
        config.EnableHostGitOperations = false;
        config.EnableSkills = false;
        config.EnableSessionStore = false;
        config.Memory = new MemoryConfiguration { Enabled = false };
        config.ToolSearch = new ToolSearchConfig { Enabled = false };
        config.CustomAgentsLocalOnly = true;
        config.EnableMcpApps = false;
        config.RequestCanvasRenderer = false;
        config.RequestExtensions = false;
        config.CoauthorEnabled = false;
        config.ManageScheduleEnabled = false;
        config.PluginDirectories = [];
        config.InstructionDirectories = [];
        config.SkillDirectories = [];
        config.CustomAgents = [];
        config.Commands = [];
        config.Canvases = [];
    }
#pragma warning restore GHCP001

    private static CopilotClientOptions CommonClientOptions(CopilotClientMode mode,
        string? workingDirectory = null, SessionFsConfig? sessionFs = null) => new()
    {
        Mode = mode,
        // Caminho explícito impede COPILOT_CLI_PATH herdado no host de substituir o runtime empacotado.
        Connection = RuntimeConnection.ForStdio(path: BundledRuntimePath()),
        UseLoggedInUser = true,
        SessionFs = sessionFs,
        BaseDirectory = mode == CopilotClientMode.CopilotCli
            ? OfficialCliHomeDirectory()
            : mode == CopilotClientMode.Empty && sessionFs is null ? PersistentSessionDirectory() : null,
        Environment = ChildEnvironment(),
        WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
    };

    internal static string PersistentSessionDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            throw new InvalidOperationException("Diretório local do usuário indisponível para sessões Copilot.");
        return Path.Combine(appData, "EsilvaSoft", "KapibaraStudio", "Copilot", "sessions");
    }

    internal static string OfficialCliHomeDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("Diretório do usuário indisponível para compartilhar a autenticação oficial do Copilot.");
        return Path.Combine(userProfile, ".copilot");
    }

    public static string ResolveWorkingDirectory(string? workingDirectory) =>
        workingDirectory is { } directory && Directory.Exists(directory)
            ? Path.GetFullPath(directory) : Environment.CurrentDirectory;

    public static string BundledRuntimePath() => BundledExecutablePath(AppContext.BaseDirectory, interactive: false);

    internal static string BundledAccountCliPath() => BundledExecutablePath(AppContext.BaseDirectory, interactive: true);

    internal static string BundledExecutablePath(string baseDirectory, bool interactive)
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Arquitetura sem runtime Copilot homologado."),
        };
        var system = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" :
            throw new PlatformNotSupportedException("Sistema sem runtime Copilot homologado.");
        var executable = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        var path = Path.Combine(baseDirectory, "runtimes", system + "-" + architecture,
            interactive ? "copilot-cli" : "native", executable);
        if (!File.Exists(path)) throw new FileNotFoundException("Runtime oficial do Copilot indisponível.", path);
        return path;
    }
}
