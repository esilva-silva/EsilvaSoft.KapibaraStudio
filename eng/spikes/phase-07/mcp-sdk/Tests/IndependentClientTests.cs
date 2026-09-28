using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Spikes.McpTests;

[TestFixture]
public sealed class IndependentClientTests
{
    [TestCase("2025-11-25", null)]
    [TestCase("2026-07-28", null)]
    [TestCase("2025-11-25", "2025-11-25")]
    [TestCase("2026-07-28", "2026-07-28")]
    public async Task BclOnlyExecutableInteroperatesWithSdkServer(string version, string? serverPin)
    {
        using var child = StartClient(version, serverPin);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var stdout = child.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            Assert.That(await stderr, Is.Empty);
            Assert.That(child.ExitCode, Is.Zero);
            using var json = JsonDocument.Parse(await stdout);
            var result = json.RootElement;
            Assert.That(result.GetProperty("ProtocolVersion").GetString(), Is.EqualTo(version));
            Assert.That(result.GetProperty("ServerPin").GetString(), Is.EqualTo(serverPin));
            Assert.That(result.GetProperty("ServerExitCode").GetInt32(), Is.Zero);
            var sent = result.GetProperty("ClientMessages").EnumerateArray().ToArray();
            var received = result.GetProperty("ServerMessages").EnumerateArray().ToArray();
            Assert.That(received, Has.Length.EqualTo(2));
            Assert.That(sent.Select(m => m.GetProperty("method").GetString()), Is.EqualTo(version == "2025-11-25"
                ? new[] { "initialize", "notifications/initialized", "tools/list" }
                : new[] { "server/discover", "tools/list" }));
            Assert.That(received[1].GetProperty("result").GetProperty("tools").GetArrayLength(), Is.Zero);

            var artifactDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "mcp-independent-wire");
            Directory.CreateDirectory(artifactDirectory);
            foreach (var (direction, messages) in new[] { ("client", sent), ("server", received) })
            {
                var path = Path.Combine(artifactDirectory, version + (serverPin is null ? ".dual" : ".pinned") + "." + direction + ".jsonl");
                await File.WriteAllLinesAsync(path, messages.Select(m => m.GetRawText()), deadline.Token);
                TestContext.AddTestAttachment(path, "Wire sintético do cliente BCL independente");
            }
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    [TestCase("2025-11-25", "2026-07-28")]
    [TestCase("2026-07-28", "2025-11-25")]
    public async Task BclOnlyExecutableRejectsAnIncompatiblePinnedServer(string version, string serverPin)
    {
        using var child = StartClient(version, serverPin);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var stdout = child.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            Assert.That(child.ExitCode, Is.EqualTo(4));
            Assert.That((await stderr).Trim(), Is.EqualTo("ProbeRejected"));
            Assert.That(await stdout, Is.Empty, "Incompatibilidade não pode produzir relatório de sucesso.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    private static Process StartClient(string version, string? serverPin)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "HOME", "TEMP", "TMP", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.ArgumentList.Add(typeof(McpIndependentClient.Program).Assembly.Location);
        start.ArgumentList.Add(typeof(McpServer.Program).Assembly.Location);
        start.ArgumentList.Add(version);
        if (serverPin is not null) start.ArgumentList.Add(serverPin);
        return Process.Start(start)!;
    }
}
