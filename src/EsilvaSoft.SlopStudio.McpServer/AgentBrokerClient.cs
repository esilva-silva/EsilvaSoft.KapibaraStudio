using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// IPC client of the broker hosted by the IDE. It connects lazily, verifies that the endpoint is owned by the current
/// user, negotiates the IPC major version before sending any credential, and authenticates with the proof read from
/// the OS store at connection time. A lost connection fails every pending request as <c>HostUnavailable</c>; nothing
/// is replayed. Reconnection happens only for new requests, after a bounded backoff.
/// </summary>
internal sealed class AgentBrokerClient : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(4);

    private readonly AgentBrokerEndpoint _endpoint;
    private readonly Guid _channelId;
    private readonly SecretReference _proofReference;
    private readonly IClientTransportCredentialStore _credentials;
    private readonly int _clientMajor;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TimeProvider _time;
    private Connection? _connection;
    private long _nextId;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private TimeSpan _backoff = TimeSpan.FromMilliseconds(250);
    private string _lastFailure = AgentBrokerProtocol.ErrorCodes.HostUnavailable;

    public AgentBrokerClient(AgentBrokerEndpoint endpoint, Guid channelId, SecretReference proofReference,
        IClientTransportCredentialStore credentials, int clientMajor = AgentBrokerProtocol.MajorVersion,
        TimeProvider? timeProvider = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _channelId = channelId;
        _proofReference = proofReference ?? throw new ArgumentNullException(nameof(proofReference));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clientMajor = clientMajor;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <exception cref="AgentBrokerProtocolException">Transport, version or authentication failure (sanitized code).</exception>
    public async Task<IReadOnlyList<AgentBrokerToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.ListTools },
            TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        return response.Type == AgentBrokerProtocol.MessageTypes.Tools && response.Tools is { } tools
            ? tools
            : throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
    }

    /// <returns>The broker result frame (succeeded or a domain failure code).</returns>
    /// <exception cref="OperationCanceledException">The MCP client cancelled; the broker was told to cancel.</exception>
    /// <exception cref="AgentBrokerProtocolException">Transport, version or authentication failure (sanitized code).</exception>
    public async Task<AgentBrokerMessage> CallToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        // The permission-prompt tool waits for a human; only it gets the approval window.
        var timeout = name == AgentBrokerProtocol.ApproveToolName
            ? AgentBrokerProtocol.MaximumApprovalCallTimeoutMilliseconds
            : AgentBrokerProtocol.DefaultCallTimeoutMilliseconds;
        var ceiling = name == AgentBrokerProtocol.ApproveToolName
            ? AgentBrokerProtocol.MaximumApprovalCallTimeoutMilliseconds
            : AgentBrokerProtocol.MaximumCallTimeoutMilliseconds;
        var response = await SendAsync(new AgentBrokerMessage
        {
            Type = AgentBrokerProtocol.MessageTypes.CallTool, Name = name, Arguments = arguments,
            TimeoutMilliseconds = timeout
        }, TimeSpan.FromMilliseconds(ceiling + 5_000), cancellationToken)
            .ConfigureAwait(false);
        return response.Type == AgentBrokerProtocol.MessageTypes.Result && response.Status is not null
            ? response
            : throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
    }

    private async Task<AgentBrokerMessage> SendAsync(AgentBrokerMessage request, TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<AgentBrokerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = pending;
        try
        {
            await WriteAsync(connection, request with { Id = id }, cancellationToken).ConfigureAwait(false);
            return await pending.Task.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await TryCancelAsync(connection, id).ConfigureAwait(false);
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.DeadlineExceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancels only this call on the broker; a late result for this id is dropped.
            await TryCancelAsync(connection, id).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Fail(connection);
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.HostUnavailable, exception);
        }
        finally
        {
            connection.Pending.TryRemove(id, out _);
        }
    }

    private async Task TryCancelAsync(Connection connection, long id)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await WriteAsync(connection, new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.Cancel, Id = id },
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or
                                           OperationCanceledException or AgentBrokerProtocolException)
        {
        }
    }

    private async Task WriteAsync(Connection connection, AgentBrokerMessage message, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AgentBrokerFrameCodec.WriteAsync(connection.Stream, message, AgentBrokerProtocol.MaximumRequestFrameBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<Connection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsAlive: true } alive) return alive;
            if (_time.GetUtcNow() < _retryAfter) throw new AgentBrokerProtocolException(_lastFailure);
            try
            {
                var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                _connection = connection;
                _backoff = TimeSpan.FromMilliseconds(250);
                _retryAfter = DateTimeOffset.MinValue;
                connection.Reader = Task.Run(() => ReadLoopAsync(connection), CancellationToken.None);
                return connection;
            }
            catch (AgentBrokerProtocolException exception)
            {
                Backoff(exception.Code);
                throw;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or
                                               UnauthorizedAccessException or ObjectDisposedException)
            {
                Backoff(AgentBrokerProtocol.ErrorCodes.HostUnavailable);
                throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.HostUnavailable, exception);
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void Backoff(string code)
    {
        _lastFailure = code;
        _retryAfter = _time.GetUtcNow() + _backoff;
        _backoff = TimeSpan.FromMilliseconds(Math.Min(_backoff.TotalMilliseconds * 2, MaximumBackoff.TotalMilliseconds));
    }

    private async Task<Connection> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!_endpoint.HasPrivateDirectory())
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.HostUnavailable);
        // CurrentUserOnly: the client verifies that the pipe/socket server runs as the same OS user before any byte.
        var pipe = new NamedPipeClientStream(".", _endpoint.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(HandshakeTimeout);
            var nonce = NewNonce();
            await AgentBrokerFrameCodec.WriteAsync(pipe, new AgentBrokerMessage
            {
                Type = AgentBrokerProtocol.MessageTypes.Hello, Protocol = AgentBrokerProtocol.Name,
                Major = _clientMajor, Minor = AgentBrokerProtocol.MinorVersion, Nonce = nonce
            }, AgentBrokerProtocol.MaximumRequestFrameBytes, handshake.Token).ConfigureAwait(false);
            var ack = await ReadHandshakeAsync(pipe, handshake).ConfigureAwait(false);
            if (ack.Type != AgentBrokerProtocol.MessageTypes.HelloAck || ack.Protocol != AgentBrokerProtocol.Name ||
                ack.Major != _clientMajor || !string.Equals(ack.PeerNonce, nonce, StringComparison.Ordinal) ||
                ack.Nonce is not { Length: > 0 } serverNonce)
                throw new AgentBrokerProtocolException(ack.Major is { } major && major != _clientMajor
                    ? AgentBrokerProtocol.ErrorCodes.IncompatibleVersion
                    : AgentBrokerProtocol.ErrorCodes.ProtocolViolation);

            var proof = await _credentials.ReadAsync(_proofReference, handshake.Token).ConfigureAwait(false);
            if (proof is not { Length: > 0 and <= AgentBrokerProtocol.MaximumProofLength })
                throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.AuthenticationRequired);
            await AgentBrokerFrameCodec.WriteAsync(pipe, new AgentBrokerMessage
            {
                Type = AgentBrokerProtocol.MessageTypes.Authenticate, ChannelId = _channelId, Proof = proof,
                Nonce = nonce, PeerNonce = serverNonce
            }, AgentBrokerProtocol.MaximumRequestFrameBytes, handshake.Token).ConfigureAwait(false);
            var authenticated = await ReadHandshakeAsync(pipe, handshake).ConfigureAwait(false);
            if (authenticated.Type != AgentBrokerProtocol.MessageTypes.Authenticated)
                throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
            return new Connection(pipe);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.HostUnavailable);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<AgentBrokerMessage> ReadHandshakeAsync(Stream pipe, CancellationTokenSource handshake)
    {
        var message = await AgentBrokerFrameCodec.ReadAsync(pipe, AgentBrokerProtocol.MaximumResponseFrameBytes,
            handshake.Token).ConfigureAwait(false) ?? throw new AgentBrokerProtocolException(
                AgentBrokerProtocol.ErrorCodes.HostUnavailable);
        if (message.Type == AgentBrokerProtocol.MessageTypes.Error)
            throw new AgentBrokerProtocolException(message.ErrorCode switch
            {
                AgentBrokerProtocol.ErrorCodes.IncompatibleVersion => AgentBrokerProtocol.ErrorCodes.IncompatibleVersion,
                AgentBrokerProtocol.ErrorCodes.RateLimited => AgentBrokerProtocol.ErrorCodes.RateLimited,
                AgentBrokerProtocol.ErrorCodes.AuthenticationFailed => AgentBrokerProtocol.ErrorCodes.AuthenticationRequired,
                AgentBrokerProtocol.ErrorCodes.HostUnavailable => AgentBrokerProtocol.ErrorCodes.HostUnavailable,
                _ => AgentBrokerProtocol.ErrorCodes.ProtocolViolation
            });
        return message;
    }

    private static async Task ReadLoopAsync(Connection connection)
    {
        try
        {
            while (true)
            {
                var message = await AgentBrokerFrameCodec.ReadAsync(connection.Stream,
                    AgentBrokerProtocol.MaximumResponseFrameBytes, CancellationToken.None).ConfigureAwait(false);
                if (message is null) break;
                if (message.Id is { } id && connection.Pending.TryRemove(id, out var pending))
                    pending.TrySetResult(message);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or
                                           AgentBrokerProtocolException or OperationCanceledException)
        {
        }
        Fail(connection);
    }

    // EOF, crash or violation: every pending request fails; the caller reports it and never re-sends it.
    private static void Fail(Connection connection)
    {
        if (!connection.MarkDead()) return;
        foreach (var pending in connection.Pending.Values)
            pending.TrySetException(new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.HostUnavailable));
        connection.Pending.Clear();
        connection.Stream.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            Fail(connection);
            if (connection.Reader is { } reader)
            {
                try { await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }
        }
        _connectLock.Dispose();
        _writeLock.Dispose();
    }

    private static string NewNonce()
    {
        Span<byte> bytes = stackalloc byte[AgentBrokerProtocol.NonceBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private sealed class Connection(Stream stream)
    {
        private int _dead;
        public Stream Stream { get; } = stream;
        public ConcurrentDictionary<long, TaskCompletionSource<AgentBrokerMessage>> Pending { get; } = new();
        public Task? Reader { get; set; }
        public bool IsAlive => Volatile.Read(ref _dead) == 0;
        public bool MarkDead() => Interlocked.Exchange(ref _dead, 1) == 0;
    }
}
