using GitHub.Copilot;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

internal sealed class LocalCopilotRuntimeResources : ICopilotRuntimeResources
{
    internal sealed record SessionParts(ICopilotRuntimeClient Client, ICopilotSessionFsStore SessionFsStore);

    internal static SessionParts CreateStandaloneSession(AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ICopilotSessionFsStore store = options.PersistProviderSession
            ? new CopilotPersistentSessionFsStore(CopilotRuntimeSettings.PersistentSessionDirectory())
            : new CopilotVolatileSessionFsStore();
        var working = CopilotRuntimeSettings.ResolveWorkingDirectory(options.WorkingDirectory);
        var config = options.PersistProviderSession ? CopilotPersistentSessionFsStore.CreateConfiguration(working)
            : CopilotVolatileSessionFsStore.CreateConfiguration(working);
        return new SessionParts(new SdkCopilotRuntimeClient(new CopilotClient(
            CopilotRuntimeSettings.SessionClientOptions(options.WorkingDirectory, config))), store);
    }

    private readonly Func<string?, CopilotClient>? _injectedClient;
    private readonly ICopilotCliConfiguration _configuration;
    private readonly Lock _disposeGate = new();
    private Task? _disposal;
    public ICopilotSessionFsStore VolatileStore { get; }
    public ICopilotSessionFsStore PersistentStore { get; }

    public LocalCopilotRuntimeResources(Func<string?, CopilotClient>? injectedClient = null, string? persistentRoot = null,
        ICopilotCliConfiguration? configuration = null)
    {
        _injectedClient = injectedClient;
        _configuration = configuration ?? new LocalCopilotCliConfiguration();
        VolatileStore = new CopilotVolatileSessionFsStore();
        PersistentStore = new CopilotPersistentSessionFsStore(persistentRoot ?? CopilotRuntimeSettings.PersistentSessionDirectory());
    }

    internal LocalCopilotRuntimeResources(ICopilotSessionFsStore volatileStore, ICopilotSessionFsStore persistentStore)
    {
        _configuration = new LocalCopilotCliConfiguration();
        VolatileStore = volatileStore ?? throw new ArgumentNullException(nameof(volatileStore));
        PersistentStore = persistentStore ?? throw new ArgumentNullException(nameof(persistentStore));
    }

    public ICopilotRuntimeClient CreateAccountClient() => new SdkCopilotRuntimeClient(
        _injectedClient is { } factory ? factory(null) : new CopilotClient(CopilotRuntimeSettings.AccountClientOptions(ResolveCli())));

    private string ResolveCli() => _configuration.ResolveExecutablePath() ??
        throw new FileNotFoundException("Instale a CLI oficial do GitHub Copilot ou configure seu executável nativo.");

    public ICopilotRuntimeClient CreateSessionClient(string? workingDirectory, bool persistent)
    {
        if (_injectedClient is { } factory) return new SdkCopilotRuntimeClient(factory(workingDirectory));
        var working = CopilotRuntimeSettings.ResolveWorkingDirectory(workingDirectory);
        var config = persistent ? CopilotPersistentSessionFsStore.CreateConfiguration(working)
            : CopilotVolatileSessionFsStore.CreateConfiguration(working);
        return new SdkCopilotRuntimeClient(new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(workingDirectory, config, ResolveCli())));
    }

    public ICopilotRuntimeClient CreateCleanupClient(bool volatileSession) => CreateSessionClient(null, !volatileSession);

    public void Dispose()
    {
        Task disposal;
        lock (_disposeGate)
        {
            _disposal ??= CopilotResourceCleanup.DisposeAllAsync([VolatileStore, PersistentStore]);
            disposal = _disposal;
        }
        disposal.GetAwaiter().GetResult();
    }
}
