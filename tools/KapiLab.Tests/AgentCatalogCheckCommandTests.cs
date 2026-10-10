using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentCatalogCheckCommandTests
{
    [Test]
    public async Task CliReturnsDriftAndEmitsAnExplicitlyIncompleteCheckEnvelope()
    {
        using var workspace = new TemporaryWorkspace();
        var expected = Snapshot("{\"type\":\"object\"}");
        var actual = Snapshot("{\"type\":\"string\"}");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "data", "lab", "expected.json"),
            AgentToolCatalogSnapshot.Serialize(expected));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "data", "lab", "actual.json"),
            AgentToolCatalogSnapshot.Serialize(actual));

        var parse = KapiLabCommandLine.Build().Parse([
            "catalog", "check", "--expected", "data/lab/expected.json", "--actual", "data/lab/actual.json",
            "--workspace", workspace.Path,
        ]);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            exitCode = await parse.InvokeAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        using var report = JsonDocument.Parse(output.ToString());
        var root = report.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(parse.Errors, Is.Empty);
            Assert.That(exitCode, Is.EqualTo(7));
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-agent-catalog-check-v1"));
            Assert.That(root.GetProperty("matches").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("scope").GetString(), Is.EqualTo("provider-maximum"));
            Assert.That(root.GetProperty("differences").EnumerateArray().Select(item => item.GetString()),
                Does.Contain("tool_contract_changed:local_tool"));
            Assert.That(error.ToString(), Does.Contain("\"exit\":7"));
        });
    }

    [Test]
    public async Task CliMapsInvalidSnapshotToInputErrorWithoutWritingAReport()
    {
        using var workspace = new TemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "data", "lab", "expected.json"), "{\"schema\":\"unknown\"}");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "data", "lab", "actual.json"),
            AgentToolCatalogSnapshot.Serialize(Snapshot("{\"type\":\"object\"}")));

        var parse = KapiLabCommandLine.Build().Parse([
            "catalog", "check", "--expected", "data/lab/expected.json", "--actual", "data/lab/actual.json",
            "--workspace", workspace.Path,
        ]);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            exitCode = await parse.InvokeAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Multiple(() =>
        {
            Assert.That(parse.Errors, Is.Empty);
            Assert.That(exitCode, Is.EqualTo(3));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("snapshot inválido"));
            Assert.That(error.ToString(), Does.Contain("\"exit\":3"));
        });
    }

    private static AgentToolCatalogSnapshot.Snapshot Snapshot(string inputSchema)
    {
        var tool = new AgentToolCatalogSnapshot.Tool("local_tool", 1, "ReadOnly", ["ReadMetadata"],
            inputSchema, null, Hash(inputSchema), null);
        return new("kapilab-agent-catalog-v1", "local", "in-process", "provider-maximum", false, [tool]);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-catalog-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "data", "lab"));
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
