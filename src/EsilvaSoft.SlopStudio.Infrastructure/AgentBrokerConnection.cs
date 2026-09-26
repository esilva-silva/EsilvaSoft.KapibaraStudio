using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// One authenticated proxy connection. The principal comes only from the channel proof verified by
/// <see cref="IAgentPrincipalAuthority"/>; client names, tool arguments and MCP annotations never authorize. Every call
/// has its own cancellation source linked to this connection, so EOF or host shutdown cancels only this proxy's work
/// and nothing is replayed.
/// </summary>
internal sealed class AgentBrokerConnection : IDisposable
{
    private static readonly AgentOutputDestination McpDestination =
        AgentOutputDestination.McpExternal(AgentBrokerProtocol.McpProviderId);

    private readonly Stream _stream;
    private readonly IAgentToolRegistry _registry;
    private readonly IAgentPrincipalAuthority _authority;
    private readonly AgentBrokerOptions _options;
    private readonly AgentBrokerAuthenticationLimiter _limiter;
    private readonly AgentBrokerCallAdmission _admission;
    private readonly IReadOnlyList<AgentBrokerToolDescriptor> _tools;
    private readonly IAgentMcpSessionScopes? _sessionScopes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _principalLock = new(1, 1);
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _inFlight = new();
    private readonly List<Task> _calls = [];
    private CancellationTokenSource? _lifetime;
    private Guid _channelId;
    private string? _proof;
    private AgentPrincipal? _principal;
    // Set at the handshake from the durable channel purpose (or an active scope); never cleared for this connection.
    private bool _sessionChannel;

    public AgentBrokerConnection(Stream stream, IAgentToolRegistry registry, IAgentPrincipalAuthority authority,
        AgentBrokerOptions options, AgentBrokerAuthenticationLimiter limiter, AgentBrokerCallAdmission admission,
        IReadOnlyList<AgentBrokerToolDescriptor> tools, IAgentMcpSessionScopes? sessionScopes = null)
    {
        _stream = stream;
        _registry = registry;
        _authority = authority;
        _options = options;
        _limiter = limiter;
        _admission = admission;
        _tools = tools;
        _sessionScopes = sessionScopes;
    }

    public async Task RunAsync(CancellationToken hostToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        _lifetime = lifetime;
        try
        {
            if (await HandshakeAsync(lifetime.Token).ConfigureAwait(false))
                await ServeAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (AgentBrokerProtocolException)
        {
            await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.ProtocolViolation), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            // EOF, violation or shutdown: cancel only this connection's calls; never retry them.
            await lifetime.CancelAsync().ConfigureAwait(false);
            Task[] pending;
            lock (_calls) pending = [.. _calls];
            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (TimeoutException) { }
            _proof = null;
            _principal = null;
            _lifetime = null;
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Releases the locks after <see cref="RunAsync"/>; a call that outlived the drain fails closed.</summary>
    public void Dispose()
    {
        _writeLock.Dispose();
        _principalLock.Dispose();
    }

    private async Task<bool> HandshakeAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_options.HandshakeTimeout);
        try
        {
            var hello = await ReadAsync(deadline.Token).ConfigureAwait(false);
            if (hello is null) return false;
            if (hello.Type != AgentBrokerProtocol.MessageTypes.Hello || hello.Protocol != AgentBrokerProtocol.Name ||
                hello.Major is not { } major || hello.Minor is null || !IsNonce(hello.Nonce))
            {
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.ProtocolViolation), token).ConfigureAwait(false);
                return false;
            }

            if (major != AgentBrokerProtocol.MajorVersion)
            {
                // Rejected before any credential crosses the channel.
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.IncompatibleVersion) with
                {
                    SupportedMajor = AgentBrokerProtocol.MajorVersion
                }, token).ConfigureAwait(false);
                return false;
            }

            var serverNonce = NewNonce();
            await WriteAsync(new AgentBrokerMessage
            {
                Type = AgentBrokerProtocol.MessageTypes.HelloAck, Protocol = AgentBrokerProtocol.Name,
                Major = AgentBrokerProtocol.MajorVersion, Minor = AgentBrokerProtocol.MinorVersion,
                Nonce = serverNonce, PeerNonce = hello.Nonce
            }, deadline.Token).ConfigureAwait(false);

            var authenticate = await ReadAsync(deadline.Token).ConfigureAwait(false);
            if (authenticate is null) return false;
            if (authenticate.Type != AgentBrokerProtocol.MessageTypes.Authenticate ||
                !string.Equals(authenticate.Nonce, hello.Nonce, StringComparison.Ordinal) ||
                !string.Equals(authenticate.PeerNonce, serverNonce, StringComparison.Ordinal) ||
                authenticate.ChannelId is not { } channelId || channelId == Guid.Empty ||
                authenticate.Proof is not { Length: > 0 and <= AgentBrokerProtocol.MaximumProofLength } proof)
            {
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.ProtocolViolation), token).ConfigureAwait(false);
                return false;
            }

            if (_limiter.IsBlocked(channelId))
            {
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.RateLimited), token).ConfigureAwait(false);
                return false;
            }

            AgentPrincipalIssueResult issued;
            try
            {
                issued = await _authority.AuthenticateExternalAsync(channelId, proof, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.HostUnavailable), token).ConfigureAwait(false);
                return false;
            }

            switch (issued.Status)
            {
                case AgentPrincipalIssueStatus.Issued when issued.Principal!.Origin == AgentPrincipalOrigin.External:
                    _principal = issued.Principal;
                    break;
                case AgentPrincipalIssueStatus.PolicyMissing:
                    // Proof verified, nothing granted yet: discovery works, every call is denied until a policy exists.
                    _principal = null;
                    break;
                case AgentPrincipalIssueStatus.CredentialStoreFailed or AgentPrincipalIssueStatus.Corrupt:
                    await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.HostUnavailable), token).ConfigureAwait(false);
                    return false;
                default:
                    _limiter.RecordFailure(channelId);
                    await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed), token)
                        .ConfigureAwait(false);
                    return false;
            }

            // External MCP clients need the user's opt-in; per-session channels of integrated providers do not, but a
            // session channel is accepted only while its scope is active (an orphan is never an external client).
            var scope = _sessionScopes?.FindByChannel(channelId);
            var sessionChannel = scope is not null || _principal?.IsSessionChannel == true;
            if (sessionChannel ? scope is null : !_options.Enabled)
            {
                _principal = null;
                _limiter.RecordFailure(channelId);
                await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed), token).ConfigureAwait(false);
                return false;
            }

            _channelId = channelId;
            _proof = proof;
            _sessionChannel = sessionChannel;
            await WriteAsync(new AgentBrokerMessage
            {
                Type = AgentBrokerProtocol.MessageTypes.Authenticated,
                Major = AgentBrokerProtocol.MajorVersion, Minor = AgentBrokerProtocol.MinorVersion
            }, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            // Slow or silent peers cannot hold a connection slot beyond the handshake deadline.
            return false;
        }
    }

    private async Task ServeAsync(CancellationToken token)
    {
        long lastId = 0;
        while (true)
        {
            var message = await ReadAsync(token).ConfigureAwait(false);
            if (message is null) return;
            switch (message.Type)
            {
                case AgentBrokerProtocol.MessageTypes.ListTools:
                    lastId = NextId(message, lastId);
                    if (SessionScopeGone())
                    {
                        await TryWriteAsync(Error(AgentBrokerProtocol.ErrorCodes.AuthenticationRequired), token)
                            .ConfigureAwait(false);
                        return;
                    }
                    await WriteAsync(new AgentBrokerMessage
                    {
                        Type = AgentBrokerProtocol.MessageTypes.Tools, Id = lastId, Tools = VisibleTools()
                    }, token).ConfigureAwait(false);
                    break;
                case AgentBrokerProtocol.MessageTypes.CallTool:
                    lastId = NextId(message, lastId);
                    StartCall(message, lastId, token);
                    break;
                case AgentBrokerProtocol.MessageTypes.Cancel:
                    // Late or unknown cancellations are ignored: a finished call is never re-executed or re-published.
                    if (message.Id is { } cancelId && _inFlight.TryGetValue(cancelId, out var call))
                    {
                        try { await call.CancelAsync().ConfigureAwait(false); }
                        catch (ObjectDisposedException) { }
                    }
                    break;
                default:
                    throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
            }
        }
    }

    private bool SessionScopeGone() =>
        (_sessionChannel || _principal?.IsSessionChannel == true) &&
        _sessionScopes?.FindByChannel(_channelId) is not { Closed: false };

    /// <summary>
    /// Discovery per channel: a per-session channel lists exactly its current turn plan; any other channel never lists
    /// a per-session tool. The registry enforces the same rule on every call.
    /// </summary>
    private IReadOnlyList<AgentBrokerToolDescriptor> VisibleTools()
    {
        var scope = _sessionScopes?.FindByChannel(_channelId);
        return scope is null
            ? [.. _tools.Where(static tool => !AgentToolRegistry.IsSessionTool(tool.Name))]
            : [.. _tools.Where(tool => scope.Exposes(tool.Name))];
    }

    // Ids must increase strictly, so a repeated or replayed request id is a protocol violation, not a new call.
    private static long NextId(AgentBrokerMessage message, long lastId) =>
        message.Id is { } id && id > lastId
            ? id
            : throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);

    private void StartCall(AgentBrokerMessage message, long id, CancellationToken token)
    {
        if (_inFlight.Count >= _options.MaximumConcurrentCallsPerConnection)
        {
            Track(TryWriteAsync(Failure(id, AgentBrokerProtocol.ErrorCodes.Busy, dispatched: false), token));
            return;
        }

        // Channel-wide rate and concurrency admission happens here, before the registry writes any audit intent.
        var admission = _admission.TryAdmit(_channelId, out var release);
        if (admission != AgentBrokerAdmission.Admitted)
        {
            var code = admission == AgentBrokerAdmission.RateLimited
                ? AgentBrokerProtocol.ErrorCodes.RateLimited
                : AgentBrokerProtocol.ErrorCodes.Busy;
            Track(TryWriteAsync(Failure(id, code, dispatched: false), token));
            return;
        }

        var call = CancellationTokenSource.CreateLinkedTokenSource(token);
        _inFlight[id] = call;
        Track(Task.Run(() => HandleCallAsync(message, id, call, release!, token), CancellationToken.None));
    }

    private void Track(Task task)
    {
        lock (_calls)
        {
            _calls.RemoveAll(static item => item.IsCompleted);
            _calls.Add(task);
        }
    }

    private async Task HandleCallAsync(AgentBrokerMessage message, long id, CancellationTokenSource call,
        Action releaseAdmission, CancellationToken connection)
    {
        AgentBrokerMessage response;
        var closeAfterResponse = false;
        using var deadline = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Token, deadline.Token);
        try
        {
            var timeout = message.TimeoutMilliseconds ?? AgentBrokerProtocol.DefaultCallTimeoutMilliseconds;
            // Only the permission-prompt tool waits for a human; it alone may use the approval window.
            var ceiling = message.Name == AgentBrokerProtocol.ApproveToolName
                ? AgentBrokerProtocol.MaximumApprovalCallTimeoutMilliseconds
                : AgentBrokerProtocol.MaximumCallTimeoutMilliseconds;
            if (timeout < 1 || timeout > ceiling ||
                message.Name is not { Length: > 0 and <= AgentBrokerProtocol.MaximumToolNameLength } name ||
                !TryGetArguments(message.Arguments, out var argumentsJson))
            {
                response = Failure(id, AgentBrokerProtocol.ErrorCodes.InvalidArguments, dispatched: false);
            }
            else
            {
                deadline.CancelAfter(timeout);
                (response, closeAfterResponse) = await ExecuteAsync(id, name, argumentsJson, linked.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (connection.IsCancellationRequested)
        {
            // Connection closed: there is no recipient, and the call is not retried.
            return;
        }
        catch (OperationCanceledException)
        {
            response = Failure(id, deadline.IsCancellationRequested && !call.IsCancellationRequested
                ? AgentBrokerProtocol.ErrorCodes.DeadlineExceeded
                : AgentBrokerProtocol.ErrorCodes.Cancelled, dispatched: null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            response = Failure(id, AgentBrokerProtocol.ErrorCodes.OutcomeUnknown, dispatched: null);
        }
        finally
        {
            // Released before the response is written, so a client that waits for it is never refused as busy.
            _inFlight.TryRemove(id, out _);
            releaseAdmission();
            call.Dispose();
        }

        if (!await TryWriteAsync(response, connection).ConfigureAwait(false) &&
            response.Status == AgentBrokerMessage.SucceededStatus && !connection.IsCancellationRequested)
        {
            // The only oversize case is a structured result near the frame ceiling: report it without data.
            await TryWriteAsync(Failure(id, "ResultTooLarge", dispatched: true), connection).ConfigureAwait(false);
        }
        if (closeAfterResponse && _lifetime is { } lifetime)
        {
            try { await lifetime.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task<(AgentBrokerMessage Response, bool Close)> ExecuteAsync(long id, string name, string argumentsJson,
        CancellationToken token)
    {
        // Defense in depth: this ingress executes only read-only descriptors.
        if (_registry.FindDescriptor(name) is { Risk: not AgentToolRisk.ReadOnly })
            return (Failure(id, AgentBrokerProtocol.ErrorCodes.PermissionDenied, dispatched: false), false);
        // A closed or revoked session ends this connection on its next call, not only at the handshake.
        if (SessionScopeGone())
            return (Failure(id, AgentBrokerProtocol.ErrorCodes.AuthenticationRequired, dispatched: false), true);

        var (principal, denial) = await CurrentPrincipalAsync(token).ConfigureAwait(false);
        if (principal is null)
            return (Failure(id, denial!, dispatched: false),
                denial == AgentBrokerProtocol.ErrorCodes.AuthenticationRequired);

        // Session correlation of the MCP ingress: the session is the enrolled channel, because MCP policies are granted
        // as AgentInvocationScope.ForSession(channelId) and the registry session quota must bound every connection of
        // that channel together. MCP has no model turn, so the turn id is a fresh per-call correlation id: turn-scoped
        // grants never cover MCP calls (fail closed) and the registry applies no per-turn budget to external principals.
        var context = new AgentInvocationContext(AgentBrokerProtocol.McpProviderId, _channelId, _channelId, Guid.NewGuid());
        var result = await _registry.InvokeAsync(principal, context, McpDestination,
            AgentBrokerOutputScopes.For(name), name, argumentsJson, token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            // Busy is admission saturation: the registry refused it before any source was touched.
            var code = result.ErrorCode ?? AgentBrokerProtocol.ErrorCodes.PermissionDenied;
            return (Failure(id, code, dispatched: code == AgentBrokerProtocol.ErrorCodes.Busy ? false : null), false);
        }

        // Publication point: the channel and its policy revision must still be current when bytes leave the IDE.
        bool current;
        try { current = await _authority.IsCurrentAsync(principal, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { current = false; }
        if (!current)
            return (Failure(id, AgentBrokerProtocol.ErrorCodes.PermissionDenied, dispatched: true), false);

        using var document = JsonDocument.Parse(result.StructuredContentJson!);
        return (new AgentBrokerMessage
        {
            Type = AgentBrokerProtocol.MessageTypes.Result, Id = id, Status = AgentBrokerMessage.SucceededStatus,
            StructuredContent = document.RootElement.Clone()
        }, false);
    }

    /// <summary>
    /// Returns the current principal, re-issuing it from the channel proof after a policy revision change. A revoked
    /// channel ends the connection; a channel without policy stays connected but every call is denied.
    /// </summary>
    private async Task<(AgentPrincipal? Principal, string? Denial)> CurrentPrincipalAsync(CancellationToken token)
    {
        await _principalLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            try
            {
                if (_principal is { } principal && await _authority.IsCurrentAsync(principal, token).ConfigureAwait(false))
                    return (principal, null);
                if (_proof is not { } proof) return (null, AgentBrokerProtocol.ErrorCodes.AuthenticationRequired);
                var issued = await _authority.AuthenticateExternalAsync(_channelId, proof, token).ConfigureAwait(false);
                switch (issued.Status)
                {
                    case AgentPrincipalIssueStatus.Issued when issued.Principal!.Origin == AgentPrincipalOrigin.External:
                        _principal = issued.Principal;
                        return (_principal, null);
                    case AgentPrincipalIssueStatus.PolicyMissing:
                        _principal = null;
                        return (null, AgentBrokerProtocol.ErrorCodes.PermissionDenied);
                    case AgentPrincipalIssueStatus.CredentialStoreFailed or AgentPrincipalIssueStatus.Corrupt:
                        _principal = null;
                        return (null, AgentBrokerProtocol.ErrorCodes.HostUnavailable);
                    default:
                        _principal = null;
                        _proof = null;
                        return (null, AgentBrokerProtocol.ErrorCodes.AuthenticationRequired);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return (null, AgentBrokerProtocol.ErrorCodes.PermissionDenied);
            }
        }
        finally
        {
            _principalLock.Release();
        }
    }

    private static bool TryGetArguments(JsonElement? arguments, out string json)
    {
        json = "{}";
        if (arguments is not { } element) return true;
        if (element.ValueKind != JsonValueKind.Object) return false;
        json = element.GetRawText();
        return Encoding.UTF8.GetByteCount(json) <= AgentBrokerProtocol.MaximumArgumentsBytes;
    }

    private Task<AgentBrokerMessage?> ReadAsync(CancellationToken token) =>
        AgentBrokerFrameCodec.ReadAsync(_stream, AgentBrokerProtocol.MaximumRequestFrameBytes, token);

    private async Task WriteAsync(AgentBrokerMessage message, CancellationToken token)
    {
        await _writeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await AgentBrokerFrameCodec.WriteAsync(_stream, message, AgentBrokerProtocol.MaximumResponseFrameBytes, token)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> TryWriteAsync(AgentBrokerMessage message, CancellationToken token)
    {
        try
        {
            await WriteAsync(message, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or
                                           ObjectDisposedException or AgentBrokerProtocolException)
        {
            return false;
        }
    }

    private static AgentBrokerMessage Error(string code) =>
        new() { Type = AgentBrokerProtocol.MessageTypes.Error, ErrorCode = code };

    private static AgentBrokerMessage Failure(long id, string code, bool? dispatched) =>
        new()
        {
            Type = AgentBrokerProtocol.MessageTypes.Result, Id = id, Status = AgentBrokerMessage.FailedStatus,
            ErrorCode = code, Dispatched = dispatched
        };

    private static string NewNonce()
    {
        Span<byte> bytes = stackalloc byte[AgentBrokerProtocol.NonceBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static bool IsNonce(string? value)
    {
        if (value is not { Length: > 0 and <= 64 }) return false;
        Span<byte> bytes = stackalloc byte[48];
        return Convert.TryFromBase64String(value, bytes, out var written) && written == AgentBrokerProtocol.NonceBytes;
    }
}
