using System.Collections.Concurrent;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Owns independent provider sessions and publishes a normalized, bounded stream per turn. The runtime is the only
/// consumer of each adapter session. Tool calls requested by a provider are executed only through the shared
/// <see cref="IAgentToolRegistry"/> (runtime dispatch) or answered by a trusted broker through
/// <see cref="SubmitToolResultAsync"/>; approvals only come from <see cref="IAgentInteractionAuthority"/>-verified
/// human decisions. No transcript is persisted.
/// </summary>
public sealed partial class AgentRuntime : IAgentRuntime, IAsyncDisposable
{
    private readonly Dictionary<string, IAgentProvider> _providers;
    private readonly IAgentInteractionAuthority? _interactionAuthority;
    private readonly IAgentToolRegistry? _toolRegistry;
    private readonly IAgentToolBindingProvider? _toolBindings;
    private readonly IAgentPrincipalAuthority? _principalAuthority;
    private readonly AgentRuntimeOptions _options;
    private readonly SemaphoreSlim _globalToolSlots;
    private readonly bool _writeApprovalsBridged;
    private readonly ConcurrentDictionary<AgentSessionId, SessionState> _sessions = new();
    private int _disposed;

    public AgentRuntime(
        IEnumerable<IAgentProvider> providers,
        IAgentInteractionAuthority? interactionAuthority = null,
        AgentRuntimeOptions? options = null,
        IAgentToolRegistry? toolRegistry = null,
        IAgentToolBindingProvider? toolBindings = null,
        IAgentPrincipalAuthority? principalAuthority = null,
        AgentRuntimeWriteApprovalBridge? writeApprovalBridge = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _options = options ?? AgentRuntimeOptions.Default;
        _options.Validate();
        if ((toolRegistry is null) != (toolBindings is null) || (toolRegistry is null) != (principalAuthority is null))
        {
            throw new ArgumentException(
                "Tool dispatch requires the registry, a trusted binding provider and the principal authority together.");
        }

        _interactionAuthority = interactionAuthority;
        _toolRegistry = toolRegistry;
        _toolBindings = toolBindings;
        _principalAuthority = principalAuthority;
        _globalToolSlots = new SemaphoreSlim(_options.MaxConcurrentToolsGlobal, _options.MaxConcurrentToolsGlobal);
        var byId = new Dictionary<string, IAgentProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.ProviderId) || provider.ProviderId.Length > 64 ||
                provider.ProviderId.Any(char.IsControl))
            {
                throw new ArgumentException("Provider ID is required.", nameof(providers));
            }

            if (!byId.TryAdd(provider.ProviderId, provider))
            {
                throw new AgentRuntimeException("DuplicateProviderId", "Provider ID is duplicated.");
            }
        }

        _providers = byId;
        // Registry write approvals are announced on this runtime's streams and decided through its authority.
        writeApprovalBridge?.Attach(this);
        _writeApprovalsBridged = writeApprovalBridge is not null;
    }

    /// <summary>Composition evidence (AC-14): the shared registry this runtime dispatches through, if any.</summary>
    internal IAgentToolRegistry? ToolRegistry => _toolRegistry;

    /// <summary>Composition evidence: the runtime budget this instance enforces.</summary>
    internal AgentRuntimeOptions Options => _options;

    /// <summary>Composition evidence: registry write approvals are announced on this runtime's streams.</summary>
    internal bool WriteApprovalsBridged => _writeApprovalsBridged;

    public async Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId) || !_providers.TryGetValue(options.ProviderId, out var provider))
        {
            throw new AgentRuntimeException("UnknownProvider", "Provider is unavailable.");
        }

        Task<IAgentSession> creation;
        try
        {
            creation = provider.CreateSessionAsync(options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AgentRuntimeException("ProviderUnavailable", "Provider session could not be started.");
        }

        IAgentSession? providerSession;
        try
        {
            providerSession = await creation.WaitAsync(_options.SessionStartTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = DisposeLateSessionAsync(creation);
            throw;
        }
        catch (Exception)
        {
            // A provider that hangs or throws never leaves a session behind; a late one is disposed when it appears.
            _ = DisposeLateSessionAsync(creation);
            throw new AgentRuntimeException("ProviderUnavailable", "Provider session could not be started.");
        }

        if (providerSession is null)
        {
            throw new AgentRuntimeException("ProviderUnavailable", "Provider session could not be started.");
        }

        var id = AgentSessionId.New();
        var destination = provider.IsLocal
            ? AgentOutputDestination.Local()
            : AgentOutputDestination.ProviderExternal(provider.ProviderId);
        if (!_sessions.TryAdd(id, new SessionState(providerSession, provider.ProviderId, destination,
                _options.MaxConcurrentToolsPerSession)
            {
                ObservableNativeTools = SnapshotObservableTools(providerSession),
                ProviderErrorCodes = SnapshotProviderErrorCodes(providerSession),
            }))
        {
            await providerSession.DisposeAsync().ConfigureAwait(false);
            throw new AgentRuntimeException("DuplicateSessionId", "Session ID is duplicated.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            await CloseSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(AgentRuntime));
        }

        return id;
    }

    public async Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        if (!turnId.IsValid)
        {
            throw new AgentRuntimeException("InvalidTurnId", "Turn ID is invalid.");
        }

        var session = FindSession(sessionId);
        TurnState turn;
        lock (session.Gate)
        {
            if (session.ActiveTurn is not { } active || active.TurnId != turnId)
            {
                throw new AgentRuntimeException("UnknownTurn", "Turn is not active in this session.");
            }

            turn = active;
        }

        // Only this turn's CTS is signalled. Anything already dispatched may have taken effect: no rollback is implied.
        turn.RequestCancel(TurnCancelReason.User);
        var confirmed = await InterruptProviderAsync(session, turnId).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            _ = BeginCloseSession(sessionId, session);
            throw new AgentRuntimeException("CancellationUnconfirmed", "Provider interruption could not be confirmed.");
        }
    }

    public async Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");
        }

        var shutdown = BeginCloseSession(sessionId, session);
        bool disposed;
        try
        {
            disposed = await shutdown.WaitAsync(_options.StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AgentRuntimeException("SessionShutdownUnconfirmed", "Provider session shutdown could not be confirmed.");
        }

        if (!disposed)
        {
            throw new AgentRuntimeException("SessionShutdownUnconfirmed", "Provider session shutdown could not be confirmed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var id in _sessions.Keys)
        {
            try
            {
                await CloseSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (AgentRuntimeException exception) when (exception.Code is "UnknownSession" or "SessionShutdownUnconfirmed")
            {
                // Concurrent close already removed it, or the adapter disposal is still pending in background.
            }
        }
    }

    private SessionState FindSession(AgentSessionId id) =>
        _sessions.TryGetValue(id, out var session)
            ? session
            : throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");

    private static void ValidateSessionId(AgentSessionId id)
    {
        if (!id.IsValid)
        {
            throw new AgentRuntimeException("InvalidSessionId", "Session ID is invalid.");
        }
    }

    /// <summary>
    /// Sanitized snapshot of the provider-native tools a session may report for display. A name must be a short ASCII
    /// token, outside the MCP namespace and different (ignoring case) from every registry tool, so an observation can
    /// never pass for a registry call. A failing declaration yields none.
    /// </summary>
    private HashSet<string> SnapshotObservableTools(IAgentSession session)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var registryNames = _toolRegistry?.GetDescriptors().Select(static descriptor => descriptor.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var name in session.ObservableNativeTools ?? [])
            {
                if (names.Count < 16 && IsObservableToolName(name) && !registryNames.Contains(name))
                {
                    names.Add(name);
                }
            }
        }
        catch (Exception)
        {
            names.Clear();
        }

        return names;
    }

    /// <summary>Only short ASCII identifiers (the same rule as tool codes), at most 64; a failing declaration yields none.</summary>
    private static HashSet<string> SnapshotProviderErrorCodes(IAgentSession session)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var code in session.ProviderErrorCodes ?? [])
            {
                if (codes.Count < 64 && code is not null && SafeCode(code) == code)
                {
                    codes.Add(code);
                }
            }
        }
        catch (Exception)
        {
            codes.Clear();
        }

        return codes;
    }

    private static bool IsObservableToolName(string? name) =>
        name is { Length: > 0 and <= 64 } && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') &&
        !name.StartsWith("mcp", StringComparison.OrdinalIgnoreCase);

    private static async Task DisposeLateSessionAsync(Task<IAgentSession> creation)
    {
        try
        {
            var late = await creation.ConfigureAwait(false);
            if (late is not null)
            {
                await late.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Nothing was published for this session; a failed late creation has nothing to release.
        }
    }

    private Task<bool> BeginCloseSession(AgentSessionId id, SessionState session)
    {
        lock (session.Gate)
        {
            if (session.ShutdownTask is not null)
            {
                return session.ShutdownTask;
            }

            session.Closed = true;
            _sessions.TryRemove(id, out _);
            session.LifetimeCancellationTask ??= session.Lifetime.CancelAsync();
            if (session.ActiveTurn is { } turn)
            {
                session.InterruptTask ??= StartInterruptProvider(session, turn.TurnId);
            }

            session.ShutdownTask = Task.Run(() => ShutdownSessionAsync(session));
            return session.ShutdownTask;
        }
    }

    private async Task<bool> ShutdownSessionAsync(SessionState session)
    {
        Task? lifetimeCancellation;
        lock (session.Gate)
        {
            lifetimeCancellation = session.LifetimeCancellationTask;
        }

        var clean = true;
        try
        {
            await ObserveCompletionAsync(lifetimeCancellation).ConfigureAwait(false);
            Task drained;
            Task? providerCancel;
            lock (session.Gate)
            {
                // Read after cancellation: a turn that was starting concurrently is visible here.
                drained = session.TurnDrained;
                providerCancel = session.ProviderCancelTask;
            }

            // Prefer disposing only after the stream, deliveries and interruption stopped, but never wait forever:
            // an adapter that ignores its token is disposed after the bound instead of keeping the session alive.
            try
            {
                await Task.WhenAll(drained, ObserveCompletionAsync(providerCancel))
                    .WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                clean = false;
            }
        }
        catch (Exception)
        {
            clean = false;
        }
        finally
        {
            session.Lifetime.Dispose();
        }

        try
        {
            await session.ProviderSession.DisposeAsync().AsTask()
                .WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
            return clean;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task ObserveCompletionAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A completed fault still permits one provider disposal attempt.
        }
    }

    private Task<bool> InterruptProviderAsync(SessionState session, AgentTurnId turnId)
    {
        lock (session.Gate)
        {
            return session.InterruptTask ??= StartInterruptProvider(session, turnId);
        }
    }

    private Task<bool> StartInterruptProvider(SessionState session, AgentTurnId turnId)
    {
        session.ProviderCancelTask = Task.Run(() => session.ProviderSession.CancelTurnAsync(turnId, CancellationToken.None));
        return AwaitInterruptionAsync(session.ProviderCancelTask, _options.StopTimeout);
    }

    private static async Task<bool> AwaitInterruptionAsync(Task cancelTask, TimeSpan timeout)
    {
        try
        {
            await cancelTask.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
