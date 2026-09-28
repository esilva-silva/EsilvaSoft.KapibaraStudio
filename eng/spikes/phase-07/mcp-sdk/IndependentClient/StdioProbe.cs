using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Spikes.McpIndependentClient;

public sealed record ProbeResult(string ProtocolVersion, string? ServerPin, int ServerExitCode,
    IReadOnlyList<JsonElement> ClientMessages, IReadOnlyList<JsonElement> ServerMessages);

/// <summary>Harness BCL de interoperabilidade mínima. Não é um cliente MCP de produção.</summary>
public static class StdioProbe
{
    private const string Legacy = "2025-11-25";
    private const string Modern = "2026-07-28";
    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";

    public static async Task<ProbeResult> RunAsync(string serverPath, string version, string? serverPin, CancellationToken token)
    {
        if (version is not (Legacy or Modern) || (serverPin is not null && serverPin is not (Legacy or Modern)))
            throw new ArgumentException("Revisão fora do escopo da fixture.");
        if (!Path.IsPathFullyQualified(serverPath) || !File.Exists(serverPath))
            throw new ArgumentException("O servidor precisa ser um arquivo local absoluto existente.");

        using var server = StartServer(serverPath, serverPin);
        List<JsonElement> sent = [], received = [];
        var stderr = ReadBoundedToEndAsync(server.StandardError, 8192, token);
        try
        {
            if (version == Legacy)
            {
                await SendAsync(server, sent, new
                {
                    jsonrpc = "2.0", id = 1, method = "initialize",
                    @params = new { protocolVersion = Legacy, capabilities = new { }, clientInfo = new { name = "slop-bcl-fixture", version = "0.1.0" } }
                }, token);
                var initialized = await ReceiveAsync(server, received, 1, token);
                Require(initialized.GetProperty("protocolVersion").GetString() == Legacy);
                Require(initialized.GetProperty("serverInfo").GetProperty("name").GetString() == "slop-phase07-protocol-spike");
                Require(initialized.GetProperty("capabilities").TryGetProperty("tools", out _));
                await SendAsync(server, sent, new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } }, token);
                await SendAsync(server, sent, new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }, token);
            }
            else
            {
                // Moderno: metadata em cada request; não enviar initialize/initialized.
                await SendAsync(server, sent, ModernRequest(1, "server/discover"), token);
                var discovery = await ReceiveAsync(server, received, 1, token);
                Require(discovery.GetProperty("supportedVersions").EnumerateArray().Any(v => v.GetString() == Modern));
                Require(discovery.GetProperty("capabilities").TryGetProperty("tools", out _));
                RequireModernIdentity(discovery);
                await SendAsync(server, sent, ModernRequest(2, "tools/list"), token);
            }

            var list = await ReceiveAsync(server, received, 2, token);
            Require(list.GetProperty("tools").ValueKind == JsonValueKind.Array && list.GetProperty("tools").GetArrayLength() == 0);
            if (version == Modern) RequireModernIdentity(list);

            server.StandardInput.Close();
            // Confere que não existe banner, resposta extra ou ruído depois da listagem.
            var trailing = await ReadBoundedToEndAsync(server.StandardOutput, 65536, token);
            await server.WaitForExitAsync(token);
            Require(trailing.Length == 0 && (await stderr).Length == 0 && server.ExitCode == 0);
            return new(version, serverPin, server.ExitCode, sent.AsReadOnly(), received.AsReadOnly());
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync(CancellationToken.None);
            }
            // Observa também falhas do dreno em teardown sem publicar payloads.
            try { await stderr; } catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException) { }
        }
    }

    private static object ModernRequest(int id, string method) => new
    {
        jsonrpc = "2.0", id, method,
        @params = new
        {
            _meta = new Dictionary<string, object>
            {
                [VersionKey] = Modern,
                ["io.modelcontextprotocol/clientInfo"] = new { name = "slop-bcl-fixture", version = "0.1.0" },
                ["io.modelcontextprotocol/clientCapabilities"] = new { }
            }
        }
    };

    private static void RequireModernIdentity(JsonElement result) =>
        Require(result.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo")
            .GetProperty("name").GetString() == "slop-phase07-protocol-spike");

    private static async Task SendAsync(Process server, List<JsonElement> captured, object message, CancellationToken token)
    {
        var element = JsonSerializer.SerializeToElement(message);
        await server.StandardInput.WriteLineAsync(element.GetRawText().AsMemory(), token);
        await server.StandardInput.FlushAsync(token);
        captured.Add(element);
    }

    private static async Task<JsonElement> ReceiveAsync(Process server, List<JsonElement> captured, int expectedId, CancellationToken token)
    {
        var line = await ReadBoundedLineAsync(server.StandardOutput, token);
        using var json = JsonDocument.Parse(line);
        var message = json.RootElement.Clone();
        Require(message.GetProperty("jsonrpc").GetString() == "2.0");
        Require(message.GetProperty("id").GetInt32() == expectedId);
        Require(!message.TryGetProperty("error", out _));
        captured.Add(message);
        return message.GetProperty("result");
    }

    private static async Task<string> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var line = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token) == 1)
        {
            if (buffer[0] == '\n') return line.ToString();
            if (line.Length >= 65536) throw new InvalidDataException("Frame excede o limite da fixture.");
            line.Append(buffer[0]);
        }
        throw new InvalidDataException("EOF antes de completar um frame JSON-RPC.");
    }

    private static async Task<string> ReadBoundedToEndAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[256];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Saída excede o limite da fixture.");
            output.Append(buffer, 0, read);
        }
        return output.ToString();
    }

    private static Process StartServer(string serverPath, string? serverPin)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false, true),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "HOME", "TEMP", "TMP", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.ArgumentList.Add(serverPath);
        if (serverPin is not null) start.ArgumentList.Add(serverPin);
        return Process.Start(start) ?? throw new InvalidOperationException("Subprocesso indisponível.");
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("Resposta incompatível com o contrato da fixture.");
    }
}
