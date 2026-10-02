using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

/// <summary>
/// Adapter for the user's GitHub Copilot subscription. Authentication remains owned by the official CLI; this
/// process never receives or stores a GitHub token.
/// </summary>
public sealed class CopilotSubscriptionAgentProvider : IAgentProvider, IAgentProviderSessionCleanup, IDisposable
{
    public const string Id = AgentProviderIds.GitHubCopilotSubscription;

    private readonly IAgentToolRegistry _toolRegistry;
    private readonly ICopilotRuntimeResources _resources;
    private readonly ICopilotAccountCommands _commands;
    private readonly AgentCapabilityEvidence _capabilityEvidence;
    private ICopilotSessionFsStore VolatileStore => _resources.VolatileStore;
    private ICopilotSessionFsStore PersistentStore => _resources.PersistentStore;
    private readonly SemaphoreSlim _statusGate = new(1, 1);
    private readonly Lock _lifecycleGate = new();
    private AgentProviderStatus _status = AgentProviderStatus.NotReported;
    private int _disposed;

    public CopilotSubscriptionAgentProvider(IAgentToolRegistry toolRegistry)
        : this(toolRegistry, new LocalCopilotRuntimeResources(), new LocalCopilotAccountCommands(), new LocalHostPlatformSnapshot())
    {
    }

    internal CopilotSubscriptionAgentProvider(
        IAgentToolRegistry toolRegistry,
        Func<string?, CopilotClient> clientFactory)
        : this(toolRegistry, clientFactory, new LocalCopilotAccountCommands().IsCliInstalled)
    {
    }

    internal CopilotSubscriptionAgentProvider(
        IAgentToolRegistry toolRegistry,
        Func<string?, CopilotClient> clientFactory,
        Func<bool> isCliInstalled)
        : this(toolRegistry, clientFactory, isCliInstalled, persistentSessionRoot: null)
    {
    }

    internal CopilotSubscriptionAgentProvider(
        IAgentToolRegistry toolRegistry,
        Func<string?, CopilotClient> clientFactory,
        Func<bool> isCliInstalled,
        string? persistentSessionRoot)
        : this(toolRegistry, new LocalCopilotRuntimeResources(clientFactory ?? throw new ArgumentNullException(nameof(clientFactory)),
            persistentSessionRoot), new InjectedAccountCommands(isCliInstalled)) { }

    internal CopilotSubscriptionAgentProvider(IAgentToolRegistry toolRegistry, ICopilotRuntimeResources resources,
        ICopilotAccountCommands commands, IHostPlatformSnapshot? platform = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _capabilityEvidence = platform?.IsWindows == true
            ? AgentCapabilityEvidence.Homologated : AgentCapabilityEvidence.AutomatedContract;
    }

    private sealed class InjectedAccountCommands(Func<bool> installed) : ICopilotAccountCommands
    {
        private readonly Func<bool> _installed = installed ?? throw new ArgumentNullException(nameof(installed));
        public bool IsCliInstalled() => _installed();
        public Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inject account commands explicitly for an injected runtime.");
    }

    public string ProviderId => Id;
    public bool IsLocal => false;

    // The returned capability evidence is scoped to this host OS. Product tool calls use the shared registry and
    // persisted grants; provider-native shell, file and network tools remain disabled.
    public AgentProviderDescriptor Describe() => new(Id, "GitHub Copilot",
        [AgentAuthenticationMethod.OfficialCliDelegated], SupportedCapabilities);

    private AgentProviderCapabilities SupportedCapabilities => new()
    {
        Chat = true,
        Streaming = true,
        ToolCalling = true,
        Sessions = true,
        ModelSelection = true,
        TurnPlan = true,
        UsesNetwork = true,
        Evidence = _capabilityEvidence,
    };

    /// <summary>
    /// Returns only the last explicitly checked account/model snapshot. Listing never starts the runtime, checks the
    /// network or authenticates; users refresh it through the provider's explicit account action.
    /// </summary>
    public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return Task.FromResult(Volatile.Read(ref _status));
    }

    /// <summary>
    /// Explicit account check. It queries auth status first and contacts the model catalog only for an authenticated
    /// Copilot user account; it never creates a session or sends a prompt/context.
    /// </summary>
    public async Task<CopilotAccountStatus> CheckAccountAndModelsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _statusGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_commands.IsCliInstalled())
            {
                SetUnavailable(AgentProviderAuthState.NotConfigured,
                    "CopilotCliNotInstalled");
                return new CopilotAccountStatus(CopilotAccountState.CliNotInstalled);
            }

            await using var client = _resources.CreateAccountClient();
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            var auth = await client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
            var accountState = ClassifyAuthentication(auth.IsAuthenticated, auth.AuthType).State;
            if (accountState != CopilotAccountState.Subscription)
            {
                var authState = accountState == CopilotAccountState.NotLoggedIn
                    ? AgentProviderAuthState.NotConfigured
                    : accountState == CopilotAccountState.OtherAuthentication
                        ? AgentProviderAuthState.Invalid : AgentProviderAuthState.Unknown;
                SetUnavailable(authState, accountState switch
                {
                    CopilotAccountState.NotLoggedIn => "CopilotLoginRequired",
                    CopilotAccountState.OtherAuthentication => "CopilotSubscriptionRequired",
                    _ => "CopilotProviderUnavailable",
                });
                return new CopilotAccountStatus(accountState);
            }

            // ListModelsAsync is an explicit account check, never an implicit catalog/startup operation. It returns
            // the models this signed-in account can select and does not send user text or workspace data.
            var models = (await client.ListModelIdsAsync(cancellationToken).ConfigureAwait(false))
                .Where(IsSafeModelId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (models.Length == 0)
            {
                PublishStatus(Unavailable(AgentProviderAuthState.Configured, "CopilotNoModels"));
                return new CopilotAccountStatus(accountState);
            }

            // Only the product registry is exposed for tool calls. Provider-native shell/file/network tools stay off;
            // the registry applies the persisted tool and data permissions to every invocation.
            PublishStatus(new AgentProviderStatus(true, AgentProviderAuthState.Configured,
                SupportedCapabilities, models, models[0]));
            return new CopilotAccountStatus(accountState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ThrowIfDisposed();
            SetUnavailable(AgentProviderAuthState.Unknown,
                IsProtocolMismatch(exception) ? "CopilotCliProtocolIncompatible" : "CopilotProviderUnavailable");
            return new CopilotAccountStatus(CopilotAccountState.Unavailable);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    internal static CopilotAccountStatus ClassifyAuthentication(bool isAuthenticated, string? authType) =>
        new(!isAuthenticated
            ? CopilotAccountState.NotLoggedIn
            : string.Equals(authType, "user", StringComparison.Ordinal)
                ? CopilotAccountState.Subscription
                : CopilotAccountState.OtherAuthentication);

    public async Task<CopilotAccountCommandResult> LoginAsync(CancellationToken cancellationToken = default)
    {
        Task<CopilotAccountCommandState> command;
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            command = _commands.RunVisibleAsync("login", cancellationToken);
        }
        var state = await command.ConfigureAwait(false);
        var account = state == CopilotAccountCommandState.Completed
            ? await CheckAccountAndModelsAsync(cancellationToken).ConfigureAwait(false) : null;
        return new CopilotAccountCommandResult(state, account);
    }

    /// <summary>Logout affects the user's global Copilot CLI account and requires prior confirmation.</summary>
    public async Task<CopilotAccountCommandResult> LogoutAsync(
        bool userConfirmedGlobalLogout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!userConfirmedGlobalLogout)
        {
            throw new InvalidOperationException("O logout global do Copilot exige confirmação do usuário.");
        }

        Task<CopilotAccountCommandState> command;
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            command = _commands.RunVisibleAsync("logout", cancellationToken);
        }
        var state = await command.ConfigureAwait(false);
        var account = state == CopilotAccountCommandState.Completed
            ? await CheckAccountAndModelsAsync(cancellationToken).ConfigureAwait(false) : null;
        return new CopilotAccountCommandResult(state, account);
    }

    public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(options.ProviderId, Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("A sessão não pertence ao GitHub Copilot.", nameof(options));
            }

            var status = Volatile.Read(ref _status);
            if (!status.IsAvailable || options.ModelId is not { Length: > 0 } model ||
                !status.Models.Contains(model, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("CopilotAccountOrModelUnavailable");
            }

            if (options.PersistProviderSession && string.IsNullOrWhiteSpace(options.ReservedProviderSessionId))
                throw new InvalidOperationException("CopilotHistoryReservationRequired");

            var sessionFs = options.PersistProviderSession ? PersistentStore : VolatileStore;
            var client = _resources.CreateSessionClient(options.WorkingDirectory, options.PersistProviderSession);
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(_toolRegistry, options, client, sessionFs));
        }
    }

    public async Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(providerSessionId) || providerSessionId.Length > 128 || providerSessionId.Any(char.IsControl))
            throw new ArgumentException("Identificador de sessão Copilot inválido.", nameof(providerSessionId));
        await _statusGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var isVolatile = VolatileStore.ContainsSession(providerSessionId);
            await using var client = _resources.CreateCleanupClient(isVolatile);
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            // Deletion is local data hygiene; it must still work after logout and be idempotent if the LiteDB delete
            // failed after the runtime had already removed its session.
            if (isVolatile)
            {
                if (await client.HasSessionAsync(providerSessionId, cancellationToken).ConfigureAwait(false))
                    await client.DeleteSessionAsync(providerSessionId, cancellationToken).ConfigureAwait(false);
                await VolatileStore.DeleteSessionAsync(providerSessionId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await PersistentStore.DeleteSessionAsync(providerSessionId, async token =>
                {
                    if (await client.HasSessionAsync(providerSessionId, token).ConfigureAwait(false))
                        await client.DeleteSessionAsync(providerSessionId, token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _statusGate.Release(); }
    }

    private void SetUnavailable(AgentProviderAuthState authState, string code)
    {
        var caps = AgentProviderCapabilities.None with { UsesNetwork = true };
        PublishStatus(new AgentProviderStatus(false, authState, caps, unavailableCode: code));
    }

    private void PublishStatus(AgentProviderStatus status)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            Volatile.Write(ref _status, status);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static AgentProviderStatus Unavailable(AgentProviderAuthState authState, string code) =>
        new(false, authState, AgentProviderCapabilities.None with { UsesNetwork = true }, unavailableCode: code);

    private static bool IsSafeModelId(string? id) => id is { Length: > 0 and <= 128 } &&
        !id.Any(char.IsControl) && id.All(static character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.' or ':' or '/');

    private static bool IsProtocolMismatch(Exception exception) => exception is InvalidOperationException &&
        exception.Message.Contains("SDK protocol version mismatch", StringComparison.Ordinal);

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        }

        // Account checks and cleanup own this gate until their clients have closed. Marking terminal first
        // prevents pending waiters from starting new work or publishing a stale availability snapshot.
        _statusGate.Wait();
        try { _resources.Dispose(); }
        finally { _statusGate.Release(); }
        // Do not dispose the semaphore: callers that were already queued must still acquire/release it safely.
    }
}

public enum CopilotAccountState
{
    Subscription,
    NotLoggedIn,
    OtherAuthentication,
    CliNotInstalled,
    Unavailable,
}

/// <summary>Somente classificação fixa; nunca inclui usuário, token ou mensagem nativa.</summary>
public sealed record CopilotAccountStatus(CopilotAccountState State);
