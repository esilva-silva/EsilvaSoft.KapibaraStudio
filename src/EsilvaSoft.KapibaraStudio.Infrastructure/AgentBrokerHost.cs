using System.Collections.Concurrent;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Local broker hosted by the IDE for MCP proxies. It listens on a per-user, per-workspace endpoint (named pipe with
/// the current-user ACL on Windows, Unix domain socket in a 0700 directory on Linux), authenticates each connection
/// with the enrolled channel proof and forwards discovery/calls to the same <see cref="IAgentToolRegistry"/> used by
/// the native runtime. It never opens the workspace database itself and never sends a MongoDB URI or secret.
/// </summary>
/// <remarks>
/// Same-user trust boundary: another process of the same OS user can read the vault and reach the endpoint. The ACL
/// and owner checks keep other users out; they do not defend against malware in the user session.
/// </remarks>
public sealed class AgentBrokerHost : IAsyncDisposable
{
    private readonly IAgentToolRegistry _registry;
    private readonly IAgentPrincipalAuthority _authority;
    private readonly AgentBrokerOptions _options;
    private readonly AgentBrokerAuthenticationLimiter _limiter;
    private readonly AgentBrokerCallAdmission _admission;
    private readonly IAgentMcpSessionScopes? _sessionScopes;
    private readonly IAgentBrokerLocalTransport _transport;
    private readonly ConcurrentDictionary<AgentBrokerConnection, Task> _connections = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SemaphoreSlim? _slots;
    private CancellationTokenSource? _stop;
    private Task? _acceptLoop;
    private IReadOnlyList<AgentBrokerToolDescriptor>? _tools;

    /// <remarks>
    /// <c>sessionScopes</c>: per-session channels of integrated providers (ADR-056). They are internal channels of the
    /// IDE: accepted even when <see cref="AgentBrokerOptions.Enabled"/> (the user's opt-in for external MCP clients) is
    /// off, and they see only their turn plan. With external clients disabled, any other channel fails authentication.
    /// </remarks>
    public AgentBrokerHost(IAgentToolRegistry registry, IAgentPrincipalAuthority authority, AgentBrokerOptions options,
        TimeProvider? timeProvider = null, IAgentMcpSessionScopes? sessionScopes = null,
        IAgentBrokerLocalTransport? transport = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _sessionScopes = sessionScopes;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _limiter = new AgentBrokerAuthenticationLimiter(options.MaximumAuthenticationFailuresPerChannel,
            options.MaximumAuthenticationFailuresGlobal, options.AuthenticationFailureWindow,
            timeProvider ?? TimeProvider.System);
        // Host-wide, keyed by channel: reconnecting never refills a channel's budget.
        _admission = new AgentBrokerCallAdmission(options.CallBurstPerChannel, options.CallsPerMinutePerChannel,
            AgentToolRegistry.MaximumConcurrentCallsPerSession, timeProvider ?? TimeProvider.System);
        Endpoint = _transport.GetEndpoint(options.WorkspaceId);
    }

    public AgentBrokerEndpoint Endpoint { get; }

    /// <summary>Workspace identifier of the endpoint (not a credential); the proxy receives it as <c>--workspace-id</c>.</summary>
    public Guid WorkspaceId => _options.WorkspaceId;

    /// <summary>Whether external MCP clients (user opt-in) are accepted besides per-session channels.</summary>
    public bool AcceptsExternalClients => _options.Enabled;

    /// <summary>Composition evidence (AC-14): the shared registry this broker forwards to.</summary>
    internal IAgentToolRegistry Registry => _registry;
    public bool IsRunning => _acceptLoop is { IsCompleted: false };
    public int ActiveConnectionCount => _connections.Count;

    /// <summary>
    /// Opens the endpoint. Fails visibly when neither external clients (user opt-in) nor per-session channels were
    /// composed, or the endpoint is already owned.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not enabled, already running, or endpoint in use.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled && _sessionScopes is null)
            throw new InvalidOperationException("O servidor MCP local não foi habilitado pelo usuário.");
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A faulted accept loop does not keep the host "running": release its resources and allow a new start.
            if (_acceptLoop is { IsCompleted: true }) await ReleaseCompletedLoopAsync().ConfigureAwait(false);
            if (_acceptLoop is not null) throw new InvalidOperationException("O broker local já está em execução.");
            _tools = BuildDescriptors(_registry);
            // Session channels left by a crash, kill or cancellation are revoked before anything is accepted.
            await RevokeOrphanSessionChannelsAsync(cancellationToken).ConfigureAwait(false);
            _transport.PrepareServerEndpoint(Endpoint);
            var slots = new SemaphoreSlim(_options.MaximumConnections, _options.MaximumConnections);
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            IAgentBrokerServerInstance first;
            try
            {
                first = CreateInstance(firstInstance: true);
            }
            catch (IOException exception)
            {
                slots.Dispose();
                throw new InvalidOperationException("O endpoint local do broker já está em uso.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                slots.Dispose();
                throw new InvalidOperationException("O endpoint local do broker já está em uso.", exception);
            }
            _slots = slots;
            _stop = new CancellationTokenSource();
            var stop = _stop.Token;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(first, slots, stop), CancellationToken.None);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task RevokeOrphanSessionChannelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _authority.RevokeOrphanSessionChannelsAsync(_sessionScopes?.ChannelIds ?? [], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Still fail-closed: a session principal without an active scope is denied by the broker and the registry.
        }
    }

    /// <summary>Stops accepting, cancels every connection's calls and closes their pipes. Idempotent.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stop is null) return;
            await _stop.CancelAsync().ConfigureAwait(false);
            try { await _acceptLoop!.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            var running = _connections.Values.ToArray();
            try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            _stop.Dispose();
            _stop = null;
            _acceptLoop = null;
            _slots?.Dispose();
            _slots = null;
            _transport.RemoveServerEndpoint(Endpoint);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task ReleaseCompletedLoopAsync()
    {
        try { await _acceptLoop!.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        var running = _connections.Values.ToArray();
        try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        _stop?.Dispose();
        _stop = null;
        _acceptLoop = null;
        _slots?.Dispose();
        _slots = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private async Task AcceptLoopAsync(IAgentBrokerServerInstance first, SemaphoreSlim slots, CancellationToken stop)
    {
        var instance = first;
        while (true)
        {
            try
            {
                await instance.WaitForConnectionAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await instance.DisposeAsync().ConfigureAwait(false);
                slots.Release();
                return;
            }
            catch (IOException)
            {
                // A client that disconnected during accept does not consume the slot.
                await instance.DisposeAsync().ConfigureAwait(false);
                instance = await NextInstanceAsync(slots, acquireSlot: false, stop).ConfigureAwait(false);
                if (instance is null) return;
                continue;
            }

            var connection = new AgentBrokerConnection(instance.Stream, _registry, _authority, _options, _limiter,
                _admission, _tools!, _sessionScopes);
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _connections[connection] = ServeAsync(connection, slots, registered.Task, stop);
            registered.SetResult();
            instance = await NextInstanceAsync(slots, acquireSlot: true, stop).ConfigureAwait(false);
            if (instance is null) return;
        }
    }

    private async Task ServeAsync(AgentBrokerConnection connection, SemaphoreSlim slots, Task registered,
        CancellationToken stop)
    {
        // Wait until the connection is tracked, so shutdown always observes it and removal never precedes insertion.
        await registered.ConfigureAwait(false);
        try
        {
            await connection.RunAsync(stop).ConfigureAwait(false);
        }
        finally
        {
            connection.Dispose();
            _connections.TryRemove(connection, out _);
            try { slots.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task<IAgentBrokerServerInstance?> NextInstanceAsync(SemaphoreSlim slots, bool acquireSlot, CancellationToken stop)
    {
        if (acquireSlot)
        {
            try { await slots.WaitAsync(stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }
        }

        var delay = TimeSpan.FromMilliseconds(50);
        while (true)
        {
            try
            {
                return CreateInstance(firstInstance: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try { await Task.Delay(delay, stop).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    slots.Release();
                    return null;
                }
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2_000));
            }
        }
    }

    private IAgentBrokerServerInstance CreateInstance(bool firstInstance) =>
        _transport.CreateServerInstance(Endpoint, _options.MaximumConnections, firstInstance);

    private static AgentBrokerToolDescriptor[] BuildDescriptors(IAgentToolRegistry registry) =>
        registry.GetChannelDescriptors()
            .Concat(registry.GetSessionChannelDescriptors(AgentProviderIds.ClaudeCodeSubscription))
            .DistinctBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .Where(descriptor => descriptor.Risk == AgentToolRisk.ReadOnly)
            .Select(descriptor => new AgentBrokerToolDescriptor
            {
                Name = descriptor.Name,
                Version = descriptor.Version,
                ReadOnly = true,
                Destructive = false,
                InputSchema = ParseSchema(registry.GetSessionChannelInputSchemaJson(
                    AgentProviderIds.ClaudeCodeSubscription, descriptor.Name)),
                OutputSchema = ParseSchema(registry.GetSessionChannelOutputSchemaJson(
                    AgentProviderIds.ClaudeCodeSubscription, descriptor.Name))
            })
            .ToArray();

    private static JsonElement ParseSchema(string? json)
    {
        using var document = JsonDocument.Parse(json ?? throw new InvalidOperationException("Tool publicada sem schema."));
        return document.RootElement.Clone();
    }
}
