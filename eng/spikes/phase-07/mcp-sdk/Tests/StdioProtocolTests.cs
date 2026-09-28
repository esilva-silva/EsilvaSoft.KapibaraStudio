using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Spikes.McpTests;

[TestFixture]
public sealed class StdioProtocolTests
{
    [TestCase("2025-11-25")]
    [TestCase("2026-07-28")]
    public async Task SdkClientAndServerRespectTheSelectedEraOnRealStdio(string version)
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", version + ".json")));
        using var child = StartServer();
        var stderr = child.StandardError.ReadToEndAsync();
        using var sent = new RecordingStream(child.StandardInput.BaseStream);
        using var received = new RecordingStream(child.StandardOutput.BaseStream);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await using (var client = await McpClient.CreateAsync(new StreamClientTransport(sent, received),
                ClientOptions(version), cancellationToken: deadline.Token))
            {
                Assert.That(client.NegotiatedProtocolVersion, Is.EqualTo(version));
                Assert.That(client.ServerInfo.Name, Is.EqualTo("slop-phase07-protocol-spike"));
                Assert.That(await client.ListToolsAsync(cancellationToken: deadline.Token), Is.Empty);
            }

            // StreamClientTransport não é proprietário dos pipes externos.
            // Fechar stdin explicitamente deve produzir EOF e encerramento natural.
            child.StandardInput.Close();
            await child.WaitForExitAsync(deadline.Token);
            Assert.That(child.ExitCode, Is.Zero);
            Assert.That(await stderr, Is.Empty, "stderr deve permanecer vazio nesta fixture sem logger.");

            var requests = ParseLines(sent.CapturedText);
            var responses = ParseLines(received.CapturedText);
            Assert.That(requests[0].GetProperty("method").GetString(),
                Is.EqualTo(fixture.RootElement.GetProperty("firstMethod").GetString()));
            var methods = requests.Select(x => x.GetProperty("method").GetString()).ToArray();
            Assert.That(methods.Contains("notifications/initialized"),
                Is.EqualTo(fixture.RootElement.GetProperty("initializedNotification").GetBoolean()));
            Assert.That(methods.Contains(version == "2026-07-28" ? "initialize" : "server/discover"), Is.False);
            var listRequest = requests.Single(x => x.GetProperty("method").GetString() == "tools/list");
            if (fixture.RootElement.GetProperty("requestMetadata").GetBoolean())
            {
                foreach (var request in requests.Where(x => x.TryGetProperty("id", out _)))
                    Assert.That(request.GetProperty("params").GetProperty("_meta")
                        .GetProperty("io.modelcontextprotocol/protocolVersion").GetString(), Is.EqualTo(version));
            }
            else
            {
                Assert.That(requests[0].GetProperty("params").GetProperty("protocolVersion").GetString(), Is.EqualTo(version));
                Assert.That(listRequest.TryGetProperty("params", out var parameters) &&
                    parameters.TryGetProperty("_meta", out var meta) &&
                    meta.TryGetProperty("io.modelcontextprotocol/protocolVersion", out _), Is.False);
                Assert.That(responses.Single(x => x.TryGetProperty("id", out var id) &&
                    id.ToString() == requests[0].GetProperty("id").ToString())
                    .GetProperty("result").GetProperty("protocolVersion").GetString(), Is.EqualTo(version));
            }

            foreach (var request in requests.Where(x => x.TryGetProperty("id", out _)))
                Assert.That(responses.Count(x => x.TryGetProperty("id", out var id) &&
                    id.ToString() == request.GetProperty("id").ToString()), Is.EqualTo(1));
            Assert.That(responses.All(x => !x.TryGetProperty("error", out _)), Is.True);
            SaveEvidence(version, sent.CapturedText, received.CapturedText);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    [TestCase("2025-11-25", "2026-07-28")]
    [TestCase("2026-07-28", "2025-11-25")]
    public async Task ExplicitProtocolPinRejectsAnIncompatibleServer(string clientVersion, string serverVersion)
    {
        using var child = StartServer(serverVersion);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.ThrowsAsync(Is.InstanceOf<McpException>(), async () =>
            {
                await using var client = await McpClient.CreateAsync(
                    new StreamClientTransport(child.StandardInput.BaseStream, child.StandardOutput.BaseStream),
                    ClientOptions(clientVersion), cancellationToken: deadline.Token);
            });
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            await stderr;
        }
    }

    private static McpClientOptions ClientOptions(string version) => new()
    {
        ProtocolVersion = version,
        ClientInfo = new Implementation { Name = "slop-synthetic-fixture", Version = "0.1.0" },
        InitializationTimeout = TimeSpan.FromSeconds(5)
    };

    private static Process StartServer(string? version = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Allowlist do SDK: não propagar chaves ou tokens do processo de testes.
        start.Environment.Clear();
        foreach (var pair in StdioClientTransportOptions.GetDefaultEnvironmentVariables())
            if (pair.Value is not null) start.Environment[pair.Key] = pair.Value;
        start.ArgumentList.Add(typeof(McpServer.Program).Assembly.Location);
        if (version is not null) start.ArgumentList.Add(version);
        return Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o servidor de fixture.");
    }

    private static JsonElement[] ParseLines(string wire)
    {
        Assert.That(wire.EndsWith('\n'), Is.True, "Framing STDIO precisa terminar cada mensagem com newline.");
        return wire.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var json = JsonDocument.Parse(line);
            Assert.That(json.RootElement.GetProperty("jsonrpc").GetString(), Is.EqualTo("2.0"));
            return json.RootElement.Clone();
        }).ToArray();
    }

    private static void SaveEvidence(string version, string sent, string received)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "mcp-wire");
        Directory.CreateDirectory(directory);
        foreach (var (direction, wire) in new[] { ("client", sent), ("server", received) })
        {
            var path = Path.Combine(directory, version + "." + direction + ".jsonl");
            File.WriteAllText(path, wire);
            TestContext.AddTestAttachment(path, "Wire STDIO sintético " + version + " " + direction);
        }
    }
}
