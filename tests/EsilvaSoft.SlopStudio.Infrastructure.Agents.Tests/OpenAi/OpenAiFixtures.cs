using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.OpenAi;

/// <summary>
/// Offline HTTP double. It never delegates to a socket handler, answers only the fixture endpoint and refuses any
/// request beyond the scripted responses (a replay would surface as a failure, not as a silent success).
/// </summary>
internal sealed class OpenAiOfflineHandler : HttpMessageHandler
{
    public static readonly Uri Endpoint = new("https://fixture.invalid/v1/");
    private static readonly Uri Completions = new("https://fixture.invalid/v1/chat/completions");
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly object _gate = new();

    public List<string> Requests { get; } = [];
    public List<string> AuthorizationHeaders { get; } = [];
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OpenAiOfflineHandler Sse(string body)
    {
        _responses.Enqueue(_ => Task.FromResult(Response(HttpStatusCode.OK, body, "text/event-stream")));
        return this;
    }

    public OpenAiOfflineHandler Status(HttpStatusCode status, string errorCode = "fixture_error")
    {
        var body = "{\"error\":{\"message\":\"fixture failure\",\"type\":\"" + errorCode + "\",\"code\":\"" + errorCode + "\"}}";
        _responses.Enqueue(_ => Task.FromResult(Response(status, body, "application/json")));
        return this;
    }

    /// <summary>Accepts the request and then never answers until cancelled.</summary>
    public OpenAiOfflineHandler Hang()
    {
        _responses.Enqueue(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != Completions)
        {
            throw new InvalidOperationException("Destino fora da fixture offline.");
        }

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<CancellationToken, Task<HttpResponseMessage>> next;
        lock (_gate)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            Requests.Add(body);
            if (!_responses.TryDequeue(out next!))
            {
                throw new InvalidOperationException("Request inesperado; replay bloqueado pela fixture.");
            }
        }

        RequestStarted.TrySetResult();
        return await next(cancellationToken);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType)
    {
        var content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(status) { Content = content };
    }

    /// <summary>Three-byte reads split UTF-8 code points and SSE delimiters.</summary>
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(count, 3), cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
    }
}

/// <summary>Builds Chat Completions chunks in the documented SSE shape.</summary>
internal static class Sse
{
    public static string Text(params string[] fragments) =>
        Stream(fragments.Select(static (fragment, index) => Delta(index == 0
            ? new { role = "assistant", content = fragment }
            : (object)new { content = fragment })).Append(Finish("stop")).ToArray());

    public static string ToolCall(string nativeId, string name, params string[] argumentFragments) =>
        Stream(argumentFragments.Select((fragment, index) => Delta(new
        {
            tool_calls = new[]
            {
                index == 0
                    ? (object)new { index = 0, id = nativeId, type = "function", function = new { name, arguments = fragment } }
                    : new { index = 0, function = new { arguments = fragment } },
            },
        })).Append(Finish("tool_calls")).ToArray());

    public static string Stream(params string[] chunks) =>
        string.Concat(chunks.Select(static chunk => "data: " + chunk + "\n\n")) + "data: [DONE]\n\n";

    public static string Delta(object delta) => Chunk(new[] { new { index = 0, delta, finish_reason = (string?)null } });

    public static string Finish(string reason) => Chunk(new[] { new { index = 0, delta = new { }, finish_reason = reason } });

    public static string Usage(long total) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-fixture",
        @object = "chat.completion.chunk",
        created = 1760000000,
        model = "fixture-model",
        choices = Array.Empty<object>(),
        usage = new { prompt_tokens = total / 2, completion_tokens = total - total / 2, total_tokens = total },
    });

    private static string Chunk(object choices) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-fixture",
        @object = "chat.completion.chunk",
        created = 1760000000,
        model = "fixture-model",
        choices,
    });
}

internal sealed class FakeCredentialProvider(SecretStoreResult<string> result) : IAgentCredentialProvider
{
    public const string FixtureKey = "fixture-placeholder-not-a-credential";

    public int Calls { get; private set; }

    public static FakeCredentialProvider WithKey(string key = FixtureKey) => new(SecretStoreResults.Success(key));

    public static FakeCredentialProvider Failing(SecretStoreFailureCode code) => new(SecretStoreResults.Failed<string>(code));

    public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(result);
    }
}

/// <summary>Registry double exposing descriptors and closed schemas only; the adapter must never invoke it.</summary>
internal sealed class FakeToolRegistry : IAgentToolRegistry
{
    public const string Schema =
        """{"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"urn:fixture","type":"object","additionalProperties":false,"required":["database"],"properties":{"database":{"type":"string"}}}""";

    private readonly Dictionary<string, AgentToolDescriptor> _descriptors;

    public FakeToolRegistry(params string[] names) =>
        _descriptors = names.ToDictionary(static name => name,
            static name => new AgentToolDescriptor(name, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]),
            StringComparer.Ordinal);

    public int Invocations { get; private set; }

    public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [.. _descriptors.Values];

    public AgentToolDescriptor? FindDescriptor(string? name) =>
        name is not null && _descriptors.TryGetValue(name, out var descriptor) ? descriptor : null;

    public string? GetInputSchemaJson(string? name) => FindDescriptor(name) is null ? null : Schema;

    public string? GetOutputSchemaJson(string? name) => null;

    public Task<AgentToolInvocationResult> InvokeAsync(
        AgentPrincipal? principal, AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
        AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson, CancellationToken cancellationToken = default)
    {
        Invocations++;
        throw new InvalidOperationException("O adapter não pode executar tools.");
    }
}

internal static class OpenAiTestFactory
{
    public const string Model = "fixture-model";

    public static OpenAiAgentProvider Provider(
        OpenAiOfflineHandler handler, IAgentCredentialProvider? credentials = null, OpenAiAgentProviderOptions? options = null,
        IAgentToolRegistry? tools = null) =>
        Provider(handler, () => options ?? new OpenAiAgentProviderOptions(), credentials, tools);

    public static OpenAiAgentProvider Provider(
        OpenAiOfflineHandler handler, Func<OpenAiAgentProviderOptions> options, IAgentCredentialProvider? credentials = null,
        IAgentToolRegistry? tools = null) =>
        new(credentials ?? FakeCredentialProvider.WithKey(), options, tools,
            OpenAiOfflineHandler.Endpoint, new HttpClientPipelineTransport(new HttpClient(handler)));

    public static AgentTurnRequest Turn(string message = "Quantas coleções existem?", string? context = null) =>
        new(AgentTurnId.New(), message, "tab-1", 1, context);

    public static async Task<List<AgentProviderEvent>> CollectAsync(
        IAsyncEnumerable<AgentProviderEvent> stream, Func<AgentProviderEvent, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            events.Add(item);
            if (onEvent is not null)
            {
                await onEvent(item);
            }
        }

        return events;
    }

    public static string Text(IEnumerable<AgentProviderEvent> events) =>
        string.Concat(events.Where(static item => item.Kind == AgentEventKind.MessageDelta).Select(static item => item.Text));
}
