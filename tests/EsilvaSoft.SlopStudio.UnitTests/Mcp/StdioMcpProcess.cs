using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EsilvaSoft.SlopStudio.UnitTests.Mcp;

/// <summary>
/// Independent MCP client built only on the BCL (Process, streams, System.Text.Json). It does not reference the proxy
/// assembly or the MCP SDK, runs the proxy in a separate process with a minimal environment and records every stdout
/// line and all of stderr for purity and secret checks.
/// </summary>
internal sealed class StdioMcpProcess : IAsyncDisposable
{
    public const string Legacy = "2025-11-25";
    public const string Current = "2026-07-28";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly string[] InheritedVariables =
        ["SystemRoot", "windir", "SystemDrive", "USERPROFILE", "USERNAME", "USERDOMAIN", "APPDATA", "LOCALAPPDATA",
         "TEMP", "TMP", "HOME", "XDG_RUNTIME_DIR", "DOTNET_ROOT", "ProgramFiles", "PATH"];

    private readonly Process _process;
    private readonly BlockingCollection<string> _lines = new();
    private readonly StringBuilder _stderr = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private readonly List<string> _stdoutLines = [];

    private StdioMcpProcess(Process process)
    {
        _process = process;
        _stdoutPump = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                lock (_stdoutLines) _stdoutLines.Add(line);
                _lines.Add(line);
            }
            _lines.CompleteAdding();
        });
        _stderrPump = Task.Run(async () =>
        {
            var text = await process.StandardError.ReadToEndAsync();
            lock (_stderr) _stderr.Append(text);
        });
    }

    public IReadOnlyList<string> StdoutLines { get { lock (_stdoutLines) return [.. _stdoutLines]; } }
    public string Stderr { get { lock (_stderr) return _stderr.ToString(); } }
    public string AllOutput => string.Join('\n', StdoutLines) + '\n' + Stderr;

    public static string ProxyPath
    {
        get
        {
            var testDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            var configuration = testDirectory.Parent!.Name;
            var root = testDirectory;
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
            var path = Path.Combine(root!.FullName, "src", "EsilvaSoft.SlopStudio.McpServer", "bin", configuration,
                testDirectory.Name, "EsilvaSoft.SlopStudio.McpServer.dll");
            if (!File.Exists(path)) Assert.Fail("Proxy MCP não compilado; execute o build da solução antes dos testes: " + path);
            return path;
        }
    }

    public static StdioMcpProcess Start(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host : "dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        };
        // Minimal environment: no inherited tokens or configuration reach the proxy.
        start.Environment.Clear();
        foreach (var name in InheritedVariables)
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.ArgumentList.Add(ProxyPath);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new StdioMcpProcess(Process.Start(start) ?? throw new InvalidOperationException("Processo não iniciado."));
    }

    public static IEnumerable<string> Arguments(Guid workspaceId, McpBrokerFixture.Channel channel, string? protocol = null)
    {
        yield return "--stdio";
        yield return "--workspace-id";
        yield return workspaceId.ToString("D");
        yield return "--channel-id";
        yield return channel.ChannelId.ToString("D");
        yield return "--proof-ref";
        yield return channel.ProofReference.Id.ToString("D");
        yield return "--proof-ref-version";
        yield return channel.ProofReference.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (protocol is not null)
        {
            yield return "--protocol-version";
            yield return protocol;
        }
    }

    public async Task SendAsync(JsonNode message)
    {
        await _process.StandardInput.WriteLineAsync(message.ToJsonString());
        await _process.StandardInput.FlushAsync();
    }

    /// <summary>Writes a line; a proxy that stops reading (closed stdin) is an expected outcome for hostile input.</summary>
    public async Task SendRawAsync(string line)
    {
        try
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync();
        }
        catch (IOException) { }
    }

    /// <summary>Returns the response with <paramref name="id"/>; fails on timeout or unexpected EOF.</summary>
    public async Task<JsonElement> ReceiveAsync(int id)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (true)
        {
            string line;
            try { line = await Task.Run(() => _lines.Take(timeout.Token), timeout.Token); }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
            {
                Assert.Fail($"Sem resposta para id {id}. stderr: {Stderr}");
                throw;
            }
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId) &&
                responseId.ValueKind == JsonValueKind.Number && responseId.GetInt32() == id)
                return document.RootElement.Clone();
        }
    }

    public async Task<JsonElement> RequestAsync(int id, string method, JsonObject? parameters = null, string? era = Legacy)
    {
        await SendAsync(Request(id, method, parameters, era));
        return await ReceiveAsync(id);
    }

    public static JsonObject Request(int id, string method, JsonObject? parameters, string? era)
    {
        parameters ??= [];
        if (era == Current)
            parameters["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = Current,
                ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "slop-bcl-test", ["version"] = "1.0.0" },
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject()
            };
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
    }

    public async Task InitializeLegacyAsync()
    {
        var initialized = await RequestAsync(0, "initialize", new JsonObject
        {
            ["protocolVersion"] = Legacy, ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "slop-bcl-test", ["version"] = "1.0.0" }
        });
        Assert.That(initialized.GetProperty("result").GetProperty("protocolVersion").GetString(), Is.EqualTo(Legacy));
        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized", ["params"] = new JsonObject() });
    }

    public static JsonObject Call(string name, string argumentsJson) =>
        new() { ["name"] = name, ["arguments"] = JsonNode.Parse(argumentsJson) };

    public async Task<int> CloseAndWaitAsync()
    {
        _process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(Timeout);
        await _process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(_stdoutPump, _stderrPump).WaitAsync(Timeout);
        return _process.ExitCode;
    }

    public async Task<int> WaitForExitAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await _process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(_stdoutPump, _stderrPump).WaitAsync(Timeout);
        return _process.ExitCode;
    }

    /// <summary>stdout must contain only JSON-RPC 2.0 messages.</summary>
    public void AssertCleanStdout()
    {
        foreach (var line in StdoutLines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.That(document.RootElement.GetProperty("jsonrpc").GetString(), Is.EqualTo("2.0"), line);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await _process.WaitForExitAsync();
        }
        _process.Dispose();
        _lines.Dispose();
    }
}
