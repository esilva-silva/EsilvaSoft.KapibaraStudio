using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;

/// <summary>A server notification or server-initiated request. The caller must answer requests with <see cref="CodexAppServerJsonRpcTransport.RespondAsync"/>.</summary>
internal sealed record CodexAppServerInboundMessage(string Method, JsonElement Parameters, JsonElement? Id);

/// <summary>Only the machine-readable RPC code is exposed; server error text may contain user data.</summary>
internal sealed class CodexAppServerRpcException(int code) : Exception("Codex App Server rejected the request.")
{
    public int Code { get; } = code;
}

/// <summary>
/// Bounded, single-reader JSON-RPC 2.0 transport for a local App Server over stdio. This class does not initialize
/// the protocol or decide which methods a provider may call. It never logs prompt, response, stderr, or frame text.
/// </summary>
internal sealed class CodexAppServerJsonRpcTransport : IAsyncDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CodexAppServerProcess _process;
    private readonly CodexAppServerTransportOptions _options;
    private readonly Channel<CodexAppServerInboundMessage> _messages;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _readTask;
    private Exception? _failure;
    private long _nextId;
    private int _disposed;

    private CodexAppServerJsonRpcTransport(CodexAppServerProcess process, CodexAppServerTransportOptions options)
    {
        _process = process;
        _options = options;
        _messages = Channel.CreateBounded<CodexAppServerInboundMessage>(new BoundedChannelOptions(options.MaxQueuedMessages)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _readTask = ReadStdoutAsync();
    }

    public ChannelReader<CodexAppServerInboundMessage> Messages => _messages.Reader;

    public int ProcessId => _process.Id;

    public static CodexAppServerJsonRpcTransport Start(string executable, string workingDirectory, string codexHome,
        CodexAppServerTransportOptions? options = null, IReadOnlyList<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        options ??= new CodexAppServerTransportOptions();
        if (options.MaxFrameBytes < 256 || options.MaxStdoutBytes < options.MaxFrameBytes ||
            options.MaxStderrBytes < 1 || options.MaxQueuedMessages < 1 ||
            options.DefaultRequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var process = CodexAppServerProcess.Start(executable,
            arguments ??
            [
                "--disable", "shell_tool",
                "--disable", "unified_exec",
                "--disable", "code_mode_host",
                "--disable", "code_mode_only",
                "--disable", "multi_agent",
                "--disable", "apps",
                "--disable", "plugins",
                "--disable", "hooks",
                "--disable", "browser_use",
                "--disable", "computer_use",
                "--disable", "view_image",
                "app-server",
            ], workingDirectory,
            codexHome, options.MaxStderrBytes);
        return new CodexAppServerJsonRpcTransport(process, options);
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfFailed();
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Codex App Server request id collision.");
        }

        try
        {
            await WriteAsync(CreateRequest(id, method, parameters), cancellationToken).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            deadline.CancelAfter(timeout ?? _options.DefaultRequestTimeout);
            try
            {
                return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _stop.IsCancellationRequested)
            {
                throw Volatile.Read(ref _failure) ?? new InvalidOperationException("Codex App Server stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Codex App Server request timed out.");
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfFailed();
        return WriteAsync(CreateNotification(method, parameters), cancellationToken);
    }

    public Task RespondAsync(JsonElement requestId, object? result, CancellationToken cancellationToken = default)
    {
        ValidateServerRequestId(requestId);
        ThrowIfFailed();
        return WriteAsync(new Dictionary<string, object?>
        {
            ["id"] = requestId, ["result"] = result,
        }, cancellationToken);
    }

    public Task RespondErrorAsync(JsonElement requestId, int code, string message,
        CancellationToken cancellationToken = default)
    {
        ValidateServerRequestId(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ThrowIfFailed();
        return WriteAsync(new Dictionary<string, object?>
        {
            ["id"] = requestId,
            ["error"] = new { code, message },
        }, cancellationToken);
    }

    private static Dictionary<string, object?> CreateRequest(long id, string method, object? parameters)
    {
        var message = CreateNotification(method, parameters);
        message["id"] = id;
        return message;
    }

    private static Dictionary<string, object?> CreateNotification(string method, object? parameters)
    {
        var message = new Dictionary<string, object?> { ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return message;
    }

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var utf8 = JsonSerializer.SerializeToUtf8Bytes(message);
        if (utf8.Length > _options.MaxFrameBytes)
        {
            throw new InvalidOperationException("Codex App Server outgoing frame exceeds its limit.");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfFailed();
            await _process.StandardInput.WriteAsync(StrictUtf8.GetString(utf8).AsMemory(), _stop.Token)
                .ConfigureAwait(false);
            await _process.StandardInput.WriteAsync("\n".AsMemory(), _stop.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            var failure = new InvalidOperationException("Codex App Server stdin failed.");
            Fail(failure);
            throw failure;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // A concurrent stdout protocol failure cancels stdin while the request is being written. Surface the
            // recorded transport failure instead of leaking the incidental cancellation to the caller.
            throw Volatile.Read(ref _failure) ?? new InvalidOperationException("Codex App Server stopped unexpectedly.");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadStdoutAsync()
    {
        var chunk = new byte[4096];
        using var frame = new MemoryStream();
        long totalBytes = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var read = await _process.StandardOutput.ReadAsync(chunk, _stop.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Codex App Server stdout closed.");
                }

                totalBytes += read;
                if (totalBytes > _options.MaxStdoutBytes)
                {
                    throw new InvalidDataException("Codex App Server stdout exceeded its limit.");
                }

                for (var index = 0; index < read; index++)
                {
                    if (chunk[index] == (byte)'\n')
                    {
                        if (frame.Length == 0)
                        {
                            throw new InvalidDataException("Codex App Server sent an empty frame.");
                        }

                        Dispatch(frame.GetBuffer().AsSpan(0, checked((int)frame.Length)));
                        frame.SetLength(0);
                    }
                    else
                    {
                        if (frame.Length >= _options.MaxFrameBytes)
                        {
                            throw new InvalidDataException("Codex App Server frame exceeded its limit.");
                        }

                        frame.WriteByte(chunk[index]);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidDataException or
            EndOfStreamException or JsonException or DecoderFallbackException or OverflowException)
        {
            // Never propagate the source exception: JSON or IO diagnostics can contain prompt or local paths.
            Fail(new InvalidOperationException("Codex App Server output failed validation."));
        }
    }

    private void Dispatch(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > 0 && utf8[^1] == (byte)'\r')
        {
            utf8 = utf8[..^1];
        }

        _ = StrictUtf8.GetString(utf8); // Reject invalid UTF-8 rather than accepting replacement characters.
        using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Codex App Server sent an invalid RPC envelope.");
        }

        if (root.TryGetProperty("method", out var method))
        {
            if (method.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(method.GetString()))
            {
                throw new InvalidDataException("Codex App Server sent an invalid RPC method.");
            }

            JsonElement? id = null;
            if (root.TryGetProperty("id", out var requestId))
            {
                if (requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                {
                    throw new InvalidDataException("Codex App Server sent an invalid request id.");
                }
                id = requestId.Clone();
            }

            var parameters = root.TryGetProperty("params", out var value) ? value.Clone() : default;
            if (!_messages.Writer.TryWrite(new CodexAppServerInboundMessage(method.GetString()!, parameters, id)))
            {
                throw new InvalidDataException("Codex App Server event queue exceeded its limit.");
            }

            return;
        }

        if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number ||
            !responseId.TryGetInt64(out var numericId))
        {
            throw new InvalidDataException("Codex App Server sent an invalid RPC response id.");
        }

        if (!_pending.TryRemove(numericId, out var pending))
        {
            return; // Late response to a timed-out or cancelled request.
        }

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericCode) ? numericCode : 0;
            pending.TrySetException(new CodexAppServerRpcException(code));
        }
        else if (root.TryGetProperty("result", out var result))
        {
            pending.TrySetResult(result.Clone());
        }
        else
        {
            throw new InvalidDataException("Codex App Server response lacks result or error.");
        }
    }

    private static void ValidateServerRequestId(JsonElement id)
    {
        if (id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            throw new ArgumentException("Codex App Server request id is invalid.", nameof(id));
        }
    }

    private void ThrowIfFailed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Volatile.Read(ref _failure) is { } failure)
        {
            throw failure;
        }
    }

    private void Fail(Exception failure)
    {
        if (Interlocked.CompareExchange(ref _failure, failure, null) is not null)
        {
            return;
        }

        _stop.Cancel();
        _process.KillTree();
        _messages.Writer.TryComplete(failure);
        foreach (var entry in _pending)
        {
            if (_pending.TryRemove(entry.Key, out var completion))
            {
                completion.TrySetException(failure);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Fail(new ObjectDisposedException(nameof(CodexAppServerJsonRpcTransport)));
        await _readTask.ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
        _stop.Dispose();
    }
}
