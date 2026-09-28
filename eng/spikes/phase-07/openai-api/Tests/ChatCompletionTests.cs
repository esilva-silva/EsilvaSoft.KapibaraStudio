using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Spikes.OpenAiApi.Tests;

[TestFixture]
public sealed class ChatCompletionTests
{
    [Test]
    public async Task FragmentedUtf8SseAccumulatesTextAndUsesOnlyTheExplicitFixtureFunction()
    {
        using var handler = new OfflineHandler(Fixture("text.sse"));
        using var http = new HttpClient(handler);
        var messages = ChatCompletionContract.CreateMessages();

        var result = await ChatCompletionContract.ReadAsync(
            CreateClient(http), messages, ChatCompletionContract.CreateOptions(), CancellationToken.None);

        Assert.That(result.Text, Is.EqualTo("Olá, ação concluída."));
        Assert.That(result.Functions, Is.Empty);
        Assert.That(result.FinishReason, Is.EqualTo(ChatFinishReason.Stop));
        AssertRequest(handler.Requests.Single(), messages);
        Assert.That(handler.AuthorizationHeaders, Is.EqualTo(new[] { "Bearer fixture-placeholder-not-a-credential" }));
    }

    [Test]
    public async Task CompleteFunctionProposalKeepsCallIdAcrossSyntheticToolResult()
    {
        using var handler = new OfflineHandler(Fixture("function.sse"), Fixture("text.sse"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var messages = ChatCompletionContract.CreateMessages();
        var options = ChatCompletionContract.CreateOptions();

        var proposalTurn = await ChatCompletionContract.ReadAsync(client, messages, options, CancellationToken.None);

        Assert.That(proposalTurn.FinishReason, Is.EqualTo(ChatFinishReason.ToolCalls));
        Assert.That(proposalTurn.Functions, Has.Count.EqualTo(1));
        var proposal = proposalTurn.Functions[0];
        Assert.That(proposal, Is.EqualTo(new ProposedFunction("call_fixture_1", "fixture_echo", "{\"message\":\"ação\"}")));

        // The spike returns a fixture value only. It never dispatches a tool implementation.
        messages.Add(new AssistantChatMessage(
            [ChatToolCall.CreateFunctionToolCall(proposal.CallId, proposal.Name, BinaryData.FromString(proposal.Arguments))]));
        messages.Add(new ToolChatMessage(proposal.CallId, "{\"status\":\"synthetic-success\"}"));

        var finalTurn = await ChatCompletionContract.ReadAsync(client, messages, options, CancellationToken.None);

        Assert.That(finalTurn.Text, Is.EqualTo("Olá, ação concluída."));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
        AssertRequest(handler.Requests[0], ChatCompletionContract.CreateMessages());
        AssertRequest(handler.Requests[1], messages);

        using var continuation = JsonDocument.Parse(handler.Requests[1]);
        var requestMessages = continuation.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = requestMessages.Single(message => message.GetProperty("role").GetString() == "assistant");
        var function = assistant.GetProperty("tool_calls")[0];
        Assert.That(function.GetProperty("id").GetString(), Is.EqualTo(proposal.CallId));
        Assert.That(function.GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("fixture_echo"));
        var toolResult = requestMessages.Single(message => message.GetProperty("role").GetString() == "tool");
        Assert.That(toolResult.GetProperty("tool_call_id").GetString(), Is.EqualTo(proposal.CallId));
        Assert.That(toolResult.GetProperty("content").GetString(), Is.EqualTo("{\"status\":\"synthetic-success\"}"));
    }

    [Test]
    public void TruncatedStreamDoesNotProduceAFunctionProposal()
    {
        var stream = Fixture("function.sse");
        var terminalChunk = stream.IndexOf("\"finish_reason\":\"tool_calls\"", StringComparison.Ordinal);
        var end = stream.LastIndexOf("data: ", terminalChunk, StringComparison.Ordinal);
        using var handler = new OfflineHandler(stream[..end]);
        using var http = new HttpClient(handler);

        Assert.ThrowsAsync<InvalidDataException>(async () => await ChatCompletionContract.ReadAsync(
            CreateClient(http), ChatCompletionContract.CreateMessages(), ChatCompletionContract.CreateOptions(), CancellationToken.None));
    }

    [Test]
    public void UnregisteredFunctionProposalIsRejected()
    {
        var stream = Fixture("function.sse").Replace("fixture_echo", "shell_exec", StringComparison.Ordinal);
        using var handler = new OfflineHandler(stream);
        using var http = new HttpClient(handler);

        Assert.ThrowsAsync<InvalidDataException>(async () => await ChatCompletionContract.ReadAsync(
            CreateClient(http), ChatCompletionContract.CreateMessages(), ChatCompletionContract.CreateOptions(), CancellationToken.None));
    }

    [Test]
    public async Task CancellingOneStreamDoesNotCancelAnIndependentStream()
    {
        using var blocked = new OfflineHandler { BlockUntilCancelled = true };
        using var blockedHttp = new HttpClient(blocked);
        using var cancellation = new CancellationTokenSource();
        var pending = ChatCompletionContract.ReadAsync(
            CreateClient(blockedHttp), ChatCompletionContract.CreateMessages(), ChatCompletionContract.CreateOptions(), cancellation.Token);
        await blocked.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));

        using var independent = new OfflineHandler(Fixture("text.sse"));
        using var independentHttp = new HttpClient(independent);
        var result = await ChatCompletionContract.ReadAsync(
            CreateClient(independentHttp), ChatCompletionContract.CreateMessages(), ChatCompletionContract.CreateOptions(), CancellationToken.None);
        Assert.That(result.Text, Is.EqualTo("Olá, ação concluída."));
        Assert.That(blocked.Requests, Has.Count.EqualTo(1));
    }

    private static ChatClient CreateClient(HttpClient http)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://fixture.invalid/v1/"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        };
        // This invalid fixture sentinel exercises the stable SDK constructor; the handler is the only transport.
        return new ChatClient("fixture-model-not-a-real-model", new ApiKeyCredential("fixture-placeholder-not-a-credential"), options);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", name));

    private static void AssertRequest(string body, IReadOnlyList<ChatMessage> expectedMessages)
    {
        using var json = JsonDocument.Parse(body);
        var request = json.RootElement;
        Assert.That(request.GetProperty("stream").GetBoolean(), Is.True);
        Assert.That(request.GetProperty("model").GetString(), Is.EqualTo("fixture-model-not-a-real-model"));
        Assert.That(request.GetProperty("parallel_tool_calls").GetBoolean(), Is.False);
        Assert.That(request.GetProperty("messages").GetArrayLength(), Is.EqualTo(expectedMessages.Count));

        var tools = request.GetProperty("tools").EnumerateArray().ToArray();
        Assert.That(tools, Has.Length.EqualTo(1));
        Assert.That(tools[0].GetProperty("type").GetString(), Is.EqualTo("function"));
        var function = tools[0].GetProperty("function");
        Assert.That(function.GetProperty("name").GetString(), Is.EqualTo("fixture_echo"));
        Assert.That(function.GetProperty("strict").GetBoolean(), Is.True);
        Assert.That(function.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean(), Is.False);
    }
}
