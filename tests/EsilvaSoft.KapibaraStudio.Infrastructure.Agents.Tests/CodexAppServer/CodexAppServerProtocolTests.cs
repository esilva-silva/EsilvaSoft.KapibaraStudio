using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.CodexAppServer;

[TestFixture, Category("Unit")]
internal sealed class CodexAppServerProtocolTests
{
    private static readonly string[] DisabledFeatures =
        ["shell_tool", "unified_exec", "multi_agent", "plugins", "hooks", "browser_use", "computer_use"];
    private sealed class RecordingLauncher : ICodexAppServerProcessLauncher
    {
        public int Calls;
        public string[]? Arguments;
        public string? Home;
        public int MaxStderrBytes;
        public IAgentStdioProcess Start(string? executable, IReadOnlyList<string> arguments,
            string? workingDirectory, string codexHome, int maxStderrBytes)
        {
            Calls++;
            Arguments = arguments.ToArray();
            Home = codexHome;
            MaxStderrBytes = maxStderrBytes;
            return new MemoryStdioProcess();
        }
    }

    [Test]
    public async Task LaunchUsesInjectedPortWithRestrictedArgumentsAndRejectsInvalidLimitsBeforeAcquiringResources()
    {
        var launcher = new RecordingLauncher();
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexAppServerJsonRpcTransport.Start(launcher,
            null, null, "synthetic-home", new CodexAppServerTransportOptions { MaxQueuedMessages = 0 }));
        Assert.That(launcher.Calls, Is.Zero);
        await using var transport = CodexAppServerJsonRpcTransport.Start(launcher, null, null, "synthetic-home",
            new CodexAppServerTransportOptions { MaxStderrBytes = 512 });
        Assert.Multiple(() =>
        {
            Assert.That(launcher.Calls, Is.EqualTo(1));
            Assert.That(launcher.Home, Is.EqualTo("synthetic-home"));
            Assert.That(launcher.MaxStderrBytes, Is.EqualTo(512));
            Assert.That(launcher.Arguments![^1], Is.EqualTo("app-server"));
            foreach (var feature in DisabledFeatures)
            {
                var index = Array.IndexOf(launcher.Arguments, feature);
                Assert.That(index, Is.GreaterThan(0));
                Assert.That(launcher.Arguments[index - 1], Is.EqualTo("--disable"));
            }
        });
    }

    [Test]
    public async Task ConcurrentRepliesAreCorrelatedEvenWhenDeliveredInReverseOrder()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var first = transport.RequestAsync("echo", new { value = 1 });
        var firstFrame = await endpoint.NextWrittenAsync();
        var second = transport.RequestAsync("echo", new { value = 2 });
        var secondFrame = await endpoint.NextWrittenAsync();
        endpoint.Send($"{{\"id\":{secondFrame.GetProperty("id")},\"result\":2}}");
        endpoint.Send($"{{\"id\":{firstFrame.GetProperty("id")},\"result\":1}}");
        Assert.That((await second.WaitAsync(TimeSpan.FromSeconds(5))).GetInt32(), Is.EqualTo(2));
        Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(5))).GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task FragmentedUtf8AndCrLfDeliverNotificationsAndServerRequests()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var bytes = Encoding.UTF8.GetBytes("{\"method\":\"event\",\"params\":{\"text\":\"ação\"}}\r\n");
        foreach (var value in bytes) endpoint.SendBytes([value]);
        var notification = await NextAsync(transport);
        Assert.That(notification.Parameters.GetProperty("text").GetString(), Is.EqualTo("ação"));
        Assert.That(notification.Id, Is.Null);
        endpoint.Send("{\"id\":\"approval\",\"method\":\"approve\"}");
        var request = await NextAsync(transport);
        await transport.RespondAsync(request.Id!.Value, new { decision = "decline" });
        var response = await endpoint.NextWrittenAsync();
        Assert.That(response.GetProperty("id").GetString(), Is.EqualTo("approval"));
        Assert.That(response.GetProperty("result").GetProperty("decision").GetString(), Is.EqualTo("decline"));
    }

    [Test]
    public async Task TimeoutCancellationAndLateRepliesDoNotBreakLaterRequests()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var expired = transport.RequestAsync("hang", timeout: TimeSpan.FromMilliseconds(30));
        var expiredId = (await endpoint.NextWrittenAsync()).GetProperty("id").GetInt64();
        Assert.ThrowsAsync<TimeoutException>(async () => await expired);
        using var cancellation = new CancellationTokenSource();
        var cancelled = transport.RequestAsync("hang", cancellationToken: cancellation.Token);
        var cancelledId = (await endpoint.NextWrittenAsync()).GetProperty("id").GetInt64();
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await cancelled);
        endpoint.Send($"{{\"id\":{expiredId},\"result\":null}}");
        endpoint.Send($"{{\"id\":{cancelledId},\"result\":null}}");
        var next = transport.RequestAsync("echo");
        var nextId = (await endpoint.NextWrittenAsync()).GetProperty("id").GetInt64();
        endpoint.Send($"{{\"id\":{nextId},\"result\":true}}");
        Assert.That((await next.WaitAsync(TimeSpan.FromSeconds(5))).GetBoolean(), Is.True);
        Assert.That(endpoint.KillCount, Is.Zero);
    }

    [Test]
    public async Task RpcErrorExposesOnlyCode()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var request = transport.RequestAsync("error");
        var id = (await endpoint.NextWrittenAsync()).GetProperty("id").GetInt64();
        endpoint.Send($"{{\"id\":{id},\"error\":{{\"code\":42,\"message\":\"SECRET_RESPONSE_BODY\"}}}}");
        var error = Assert.ThrowsAsync<CodexAppServerRpcException>(async () => await request);
        Assert.That(error!.Code, Is.EqualTo(42));
        Assert.That(error.ToString(), Does.Not.Contain("SECRET_RESPONSE_BODY"));
    }

    [TestCase("invalid SECRET")]
    [TestCase("")]
    [TestCase("[]")]
    [TestCase("{\"method\":false}")]
    [TestCase("{\"id\":null,\"method\":\"event\"}")]
    [TestCase("{\"id\":1}")]
    public async Task InvalidFramesFailPendingRequestsAndTerminateEndpoint(string invalid)
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var request = transport.RequestAsync("pending");
        await endpoint.NextWrittenAsync();
        endpoint.Send(invalid);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(error!.ToString(), Does.Not.Contain("SECRET"));
        Assert.That(endpoint.KillCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidUtf8StopsProtocol()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var pending = transport.RequestAsync("pending");
        await endpoint.NextWrittenAsync();
        endpoint.SendBytes([0xff, (byte)'\n']);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task OversizeFramesAndQueueOverflowFailClosed()
    {
        foreach (var overflowQueue in new[] { false, true })
        {
            var endpoint = new MemoryStdioProcess();
            await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint,
                new CodexAppServerTransportOptions { MaxFrameBytes = 256, MaxQueuedMessages = 1 });
            var pending = transport.RequestAsync("pending");
            await endpoint.NextWrittenAsync();
            if (overflowQueue)
            {
                endpoint.Send("{\"method\":\"event\"}");
                endpoint.Send("{\"method\":\"event\"}");
            }
            else endpoint.Send(new string('x', 257));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(endpoint.KillCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task OutgoingLimitDoesNotStopTheTransport()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint,
            new CodexAppServerTransportOptions { MaxFrameBytes = 256 });
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.NotifyAsync("event", new { text = new string('x', 257) }));
        await transport.NotifyAsync("event", new { ok = true });
        Assert.That((await endpoint.NextWrittenAsync()).GetProperty("params").GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(endpoint.KillCount, Is.Zero);
    }

    [Test]
    public async Task CumulativeStdoutLimitStopsSmallIndividuallyValidFrames()
    {
        var endpoint = new MemoryStdioProcess();
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint,
            new CodexAppServerTransportOptions { MaxFrameBytes = 256, MaxStdoutBytes = 256 });
        var pending = transport.RequestAsync("pending");
        await endpoint.NextWrittenAsync();
        for (var index = 0; index < 20; index++) endpoint.Send("{\"method\":\"event\"}");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(endpoint.KillCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StdoutFailureDuringBlockedWriteReportsProtocolFailureNotIncidentalCancellation()
    {
        var endpoint = new MemoryStdioProcess
        {
            WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var pending = transport.RequestAsync("pending");
        await endpoint.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        endpoint.Send("invalid SECRET");
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(error!.Message, Does.Contain("output failed validation"));
        Assert.That(error.ToString(), Does.Not.Contain("SECRET"));
    }

    [Test]
    public async Task WriteFailureAndEofDoNotExposeIoDiagnostics()
    {
        var endpoint = new MemoryStdioProcess { WriteFailure = new IOException("SECRET_LOCAL_PATH") };
        await using (var transport = CodexAppServerJsonRpcTransport.Attach(endpoint))
        {
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await transport.RequestAsync("pending"));
            Assert.That(error!.ToString(), Does.Not.Contain("SECRET_LOCAL_PATH"));
        }
        endpoint = new MemoryStdioProcess();
        await using (var transport = CodexAppServerJsonRpcTransport.Attach(endpoint))
        {
            var pending = transport.RequestAsync("pending");
            await endpoint.NextWrittenAsync();
            endpoint.CloseOutput();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Test]
    public async Task DisposeStopsPendingWorkAndDisposesEndpointOnce()
    {
        var endpoint = new MemoryStdioProcess();
        var transport = CodexAppServerJsonRpcTransport.Attach(endpoint);
        var pending = transport.RequestAsync("pending");
        await endpoint.NextWrittenAsync();
        await transport.DisposeAsync();
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
        await transport.DisposeAsync();
        Assert.That(endpoint.KillCount, Is.EqualTo(1));
        Assert.That(endpoint.DisposeCount, Is.EqualTo(1));
    }

    private static Task<CodexAppServerInboundMessage> NextAsync(CodexAppServerJsonRpcTransport transport) =>
        transport.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
}
