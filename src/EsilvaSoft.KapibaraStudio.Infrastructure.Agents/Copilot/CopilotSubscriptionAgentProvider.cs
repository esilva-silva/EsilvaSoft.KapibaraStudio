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
    private readonly Func<string?, CopilotClient> _clientFactory;
    private readonly Func<bool> _isCliInstalled;
    private readonly CopilotVolatileSessionFsStore _volatileSessionFs = new();
    private readonly CopilotPersistentSessionFsStore _persistentSessionFs;
    private readonly SemaphoreSlim _statusGate = new(1, 1);
    private AgentProviderStatus _status = AgentProviderStatus.NotReported;

    public CopilotSubscriptionAgentProvider(IAgentToolRegistry toolRegistry)
        : this(toolRegistry, _ => new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()),
            CopilotAccountCommands.IsCliInstalled)
    {
    }

    internal CopilotSubscriptionAgentProvider(
        IAgentToolRegistry toolRegistry,
        Func<string?, CopilotClient> clientFactory)
        : this(toolRegistry, clientFactory, CopilotAccountCommands.IsCliInstalled)
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
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _isCliInstalled = isCliInstalled ?? throw new ArgumentNullException(nameof(isCliInstalled));
        _persistentSessionFs = new CopilotPersistentSessionFsStore(
            persistentSessionRoot ?? CopilotRuntimeSettings.PersistentSessionDirectory());
    }

    public string ProviderId => Id;
    public bool IsLocal => false;

    // The real Windows runtime has verified message-only chat, streaming and session recovery. Tool plans stay
    // always use the shared product registry and persisted grants. Provider-native shell/file/network tools stay off.
    public AgentProviderDescriptor Describe() => new(Id, "GitHub Copilot",
        [AgentAuthenticationMethod.OfficialCliDelegated], MessageOnlyCapabilities);

    private static AgentProviderCapabilities MessageOnlyCapabilities => new()
    {
        Chat = true,
        Streaming = true,
        ToolCalling = true,
        Sessions = true,
        ModelSelection = true,
        TurnPlan = true,
        UsesNetwork = true,
        Evidence = AgentCapabilityEvidence.Homologated,
    };

    /// <summary>
    /// Returns only the last explicitly checked account/model snapshot. Listing never starts the runtime, checks the
    /// network or authenticates; users refresh it through the provider's explicit account action.
    /// </summary>
    public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Volatile.Read(ref _status));

    /// <summary>
    /// Explicit account check. It queries auth status first and contacts the model catalog only for an authenticated
    /// Copilot user account; it never creates a session or sends a prompt/context.
    /// </summary>
    public async Task<CopilotAccountStatus> CheckAccountAndModelsAsync(CancellationToken cancellationToken = default)
    {
        await _statusGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isCliInstalled())
            {
                SetUnavailable(AgentProviderAuthState.NotConfigured,
                    "CopilotCliNotInstalled");
                return new CopilotAccountStatus(CopilotAccountState.CliNotInstalled);
            }

            await using var client = _clientFactory(null);
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
            var models = (await client.ListModelsAsync(cancellationToken).ConfigureAwait(false))
                .Select(static model => model.Id)
                .Where(IsSafeModelId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (models.Length == 0)
            {
                Volatile.Write(ref _status, Unavailable(AgentProviderAuthState.Configured, "CopilotNoModels"));
                return new CopilotAccountStatus(accountState);
            }

            // Only the product registry is exposed for tool calls. Provider-native shell/file/network tools stay off;
            // the registry applies the persisted tool and data permissions to every invocation.
            Volatile.Write(ref _status, new AgentProviderStatus(true, AgentProviderAuthState.Configured,
                MessageOnlyCapabilities, models, models[0]));
            return new CopilotAccountStatus(accountState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetUnavailable(AgentProviderAuthState.Unknown,
                "CopilotProviderUnavailable");
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
        var state = await CopilotAccountCommands.RunVisibleAsync("login", cancellationToken).ConfigureAwait(false);
        var account = state == CopilotAccountCommandState.Completed
            ? await CheckAccountAndModelsAsync(cancellationToken).ConfigureAwait(false) : null;
        return new CopilotAccountCommandResult(state, account);
    }

    /// <summary>Logout affects the user's global Copilot CLI account and requires prior confirmation.</summary>
    public async Task<CopilotAccountCommandResult> LogoutAsync(
        bool userConfirmedGlobalLogout, CancellationToken cancellationToken = default)
    {
        if (!userConfirmedGlobalLogout)
        {
            throw new InvalidOperationException("O logout global do Copilot exige confirmação do usuário.");
        }

        var state = await CopilotAccountCommands.RunVisibleAsync("logout", cancellationToken).ConfigureAwait(false);
        var account = state == CopilotAccountCommandState.Completed
            ? await CheckAccountAndModelsAsync(cancellationToken).ConfigureAwait(false) : null;
        return new CopilotAccountCommandResult(state, account);
    }

    public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
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

        ICopilotSessionFsStore sessionFs = options.PersistProviderSession
            ? (ICopilotSessionFsStore)_persistentSessionFs
            : _volatileSessionFs;
        var sessionFsConfig = options.PersistProviderSession
            ? CopilotPersistentSessionFsStore.CreateConfiguration(CopilotRuntimeSettings.ResolveWorkingDirectory(options.WorkingDirectory))
            : CopilotVolatileSessionFsStore.CreateConfiguration(CopilotRuntimeSettings.ResolveWorkingDirectory(options.WorkingDirectory));
        var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(options.WorkingDirectory, sessionFsConfig));
        return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(_toolRegistry, options, client, sessionFs));
    }

    public async Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerSessionId) || providerSessionId.Length > 128 || providerSessionId.Any(char.IsControl))
            throw new ArgumentException("Identificador de sessão Copilot inválido.", nameof(providerSessionId));
        var isVolatile = _volatileSessionFs.ContainsSession(providerSessionId);
        var sessionFsConfig = isVolatile
            ? CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory)
            : CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        await using var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(sessionFs: sessionFsConfig));
        await client.StartAsync(cancellationToken).ConfigureAwait(false);
        // Deletion is local data hygiene; it must still work after logout and be idempotent if the LiteDB delete
        // failed after the runtime had already removed its session.
        if (isVolatile)
        {
            if (await client.GetSessionMetadataAsync(providerSessionId, cancellationToken).ConfigureAwait(false) is not null)
                await client.DeleteSessionAsync(providerSessionId, cancellationToken).ConfigureAwait(false);
            await _volatileSessionFs.DeleteSessionAsync(providerSessionId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _persistentSessionFs.DeleteSessionAsync(providerSessionId, async token =>
            {
                if (await client.GetSessionMetadataAsync(providerSessionId, token).ConfigureAwait(false) is not null)
                    await client.DeleteSessionAsync(providerSessionId, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private void SetUnavailable(AgentProviderAuthState authState, string code)
    {
        var caps = AgentProviderCapabilities.None with { UsesNetwork = true };
        Volatile.Write(ref _status, new AgentProviderStatus(false, authState, caps, unavailableCode: code));
    }

    private static AgentProviderStatus Unavailable(AgentProviderAuthState authState, string code) =>
        new(false, authState, AgentProviderCapabilities.None with { UsesNetwork = true }, unavailableCode: code);

    private static bool IsSafeModelId(string? id) => id is { Length: > 0 and <= 128 } &&
        !id.Any(char.IsControl) && id.All(static character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.' or ':' or '/');

    public void Dispose()
    {
        _volatileSessionFs.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _persistentSessionFs.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _statusGate.Dispose();
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
