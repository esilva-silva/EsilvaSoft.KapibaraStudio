using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Anthropic;

/// <summary>Chave sintética de teste; nunca uma credencial real.</summary>
internal static class ClaudeFixture
{
    public const string SyntheticKey = "sk-ant-test-SYNTHETIC-0000000000000000";
    public static readonly SecretReference KeyReference = new(Guid.Parse("0f5d1c7e-1111-4a55-9b1e-5d2f2f0b0a01"));

    public static ClaudeAgentProviderOptions Options(ClaudeAgentBudget? budget = null) => new()
    {
        ApiKeyReference = KeyReference,
        BaseUrl = new Uri("https://claude.test.invalid"),
        Budget = budget ?? ClaudeAgentBudget.Default with
        {
            FirstEventTimeout = TimeSpan.FromSeconds(10),
            StreamIdleTimeout = TimeSpan.FromSeconds(10),
            ToolResultTimeout = TimeSpan.FromSeconds(10),
        },
    };

    public static ClaudeAgentProvider Provider(
        FakeClaudeHandler handler, ClaudeAgentProviderOptions? options = null, FakeCredentialProvider? credentials = null,
        IAgentToolRegistry? registry = null)
    {
        var configured = options ?? Options();
        return new ClaudeAgentProvider(credentials ?? new FakeCredentialProvider(), () => configured, registry, handler);
    }
}

internal sealed class FakeCredentialProvider : IAgentCredentialProvider
{
    public SecretStoreFailureCode? Failure { get; set; }
    public int Calls => _calls;
    private int _calls;

    public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Failure is { } code
            ? SecretStoreResults.Failed<string>(code)
            : SecretStoreResults.Success(ClaudeFixture.SyntheticKey));
    }
}

/// <summary>Requisição observada pelo handler falso, sem nunca enviar nada pela rede.</summary>
internal sealed record CapturedRequest(string Method, Uri? Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

/// <summary>Handler HTTP falso: cada requisição consome a próxima resposta programada. Nenhum socket é aberto.</summary>
internal sealed class FakeClaudeHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<CancellationToken, HttpResponseMessage>> _responses = new();
    public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
    public TaskCompletionSource RequestCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeClaudeHandler Enqueue(Func<CancellationToken, HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public FakeClaudeHandler EnqueueSse(string body, int chunkSize = int.MaxValue, bool hangAtEnd = false) =>
        Enqueue(token => Sse(body, chunkSize, hangAtEnd, token));

    public FakeClaudeHandler EnqueueError(HttpStatusCode status, string type) =>
        Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { type = "error", error = new { type, message = "synthetic" } }),
                Encoding.UTF8, "application/json"),
        });

    /// <summary>Nunca responde: simula servidor sem progresso até o cancelamento.</summary>
    public FakeClaudeHandler EnqueueHang() => Enqueue(_ => throw new InvalidOperationException("unused"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(static header => header.Key.ToLowerInvariant(), static header => string.Join(",", header.Value));
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri, headers, body));
        if (!_responses.TryDequeue(out var next))
        {
            throw new InvalidOperationException("Nenhuma resposta programada para a requisição.");
        }

        HttpResponseMessage response;
        try
        {
            response = next(cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "unused")
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RequestCancelled.TrySetResult();
                throw;
            }

            throw;
        }

        return response;
    }

    private HttpResponseMessage Sse(string body, int chunkSize, bool hangAtEnd, CancellationToken token) =>
        new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ChunkedStream(Encoding.UTF8.GetBytes(body), chunkSize, hangAtEnd, RequestCancelled, token))
            {
                Headers = { { "Content-Type", "text/event-stream" } },
            },
        };
}

/// <summary>Entrega o corpo SSE em pedaços arbitrários (inclusive no meio de caracteres UTF-8 e linhas).</summary>
internal sealed class ChunkedStream(byte[] data, int chunkSize, bool hangAtEnd, TaskCompletionSource cancelled, CancellationToken requestToken)
    : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= data.Length)
        {
            if (!hangAtEnd)
            {
                return 0;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestToken);
            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        }

        await Task.Yield();
        var length = Math.Min(Math.Min(chunkSize, buffer.Length), data.Length - _position);
        data.AsMemory(_position, length).CopyTo(buffer);
        _position += length;
        return length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Monta streams SSE sintéticos no formato documentado da Messages API.</summary>
internal sealed class SseBuilder
{
    private readonly StringBuilder _body = new();
    private int _index;

    public SseBuilder Start(int inputTokens = 12) => Event("message_start", new
    {
        type = "message_start",
        message = new
        {
            id = "msg_synthetic", type = "message", role = "assistant", model = "claude-opus-5", content = Array.Empty<object>(),
            stop_reason = (string?)null, stop_sequence = (string?)null,
            usage = new { input_tokens = inputTokens, output_tokens = 1 },
        },
    });

    public SseBuilder Text(params string[] fragments)
    {
        var index = _index++;
        Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } });
        foreach (var fragment in fragments)
        {
            Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text = fragment } });
        }

        return BlockStop(index);
    }

    public SseBuilder Thinking(string signature)
    {
        var index = _index++;
        Event("content_block_start", new
        {
            type = "content_block_start", index, content_block = new { type = "thinking", thinking = "", signature = "" },
        });
        Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "signature_delta", signature } });
        return BlockStop(index);
    }

    public SseBuilder ToolUse(string id, string name, params string[] jsonFragments) => ToolUse(id, name, true, jsonFragments);

    public SseBuilder ToolUse(string id, string name, bool close, params string[] jsonFragments)
    {
        var index = _index++;
        Event("content_block_start", new
        {
            type = "content_block_start", index, content_block = new { type = "tool_use", id, name, input = new { } },
        });
        foreach (var fragment in jsonFragments)
        {
            Event("content_block_delta", new
            {
                type = "content_block_delta", index, delta = new { type = "input_json_delta", partial_json = fragment },
            });
        }

        return close ? BlockStop(index) : this;
    }

    public SseBuilder Stop(string reason, int outputTokens = 20)
    {
        Event("message_delta", new
        {
            type = "message_delta", delta = new { stop_reason = reason, stop_sequence = (string?)null },
            usage = new { output_tokens = outputTokens },
        });
        return Event("message_stop", new { type = "message_stop" });
    }

    public SseBuilder Ping() => Event("ping", new { type = "ping" });

    public SseBuilder Error(string type) =>
        Event("error", new { type = "error", error = new { type, message = "synthetic" } });

    public string Build() => _body.ToString();

    private SseBuilder BlockStop(int index) => Event("content_block_stop", new { type = "content_block_stop", index });

    private SseBuilder Event(string name, object data)
    {
        _body.Append("event: ").Append(name).Append('\n').Append("data: ").Append(JsonSerializer.Serialize(data)).Append("\n\n");
        return this;
    }
}


/// <summary>Registry mínimo que só publica metadados; a execução não pertence ao adapter.</summary>
internal sealed class CatalogOnlyRegistry(params (string Name, string Schema)[] tools) : IAgentToolRegistry
{
    public IReadOnlyList<AgentToolDescriptor> GetDescriptors() =>
        [.. tools.Select(static tool => new AgentToolDescriptor(tool.Name, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]))];

    public AgentToolDescriptor? FindDescriptor(string? name) => GetDescriptors().FirstOrDefault(d => d.Name == name);

    public string? GetInputSchemaJson(string? name) => tools.FirstOrDefault(tool => tool.Name == name).Schema;

    public string? GetOutputSchemaJson(string? name) => null;

    public Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal, AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("O adapter nunca executa tools diretamente.");
}
