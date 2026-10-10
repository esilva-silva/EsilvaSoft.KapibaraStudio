using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentCatalogInvokeTests
{
    [TestCase("list_connections", "Succeeded", null, "Succeeded", null, 1, "Succeeded", "PolicyAllowed", true)]
    [TestCase("list_connections", "Succeeded", null, "Succeeded", "UnexpectedError", 1, "Succeeded", "PolicyAllowed", false)]
    [TestCase("list_connections", "Succeeded", null, "Succeeded", null, 0, null, null, false)]
    [TestCase("list_connections", "Succeeded", null, "Succeeded", null, 2, "Succeeded", "PolicyAllowed", false)]
    [TestCase("list_connections", "Succeeded", null, "Succeeded", null, 1, "Succeeded", "PermissionMissing", false)]
    [TestCase("list_databases", "Denied", "PermissionDenied", "Denied", "PermissionDenied", 1, "Denied", "PermissionMissing", true)]
    [TestCase("list_databases", "Denied", "PermissionDenied", "Denied", "PermissionDenied", 0, null, null, false)]
    [TestCase("list_databases", "Denied", "PermissionDenied", "Denied", "PermissionDenied", 1, "Denied", "PolicyAllowed", false)]
    public void InvocationCaseRequiresExpectedRuntimeAndAuditEvidence(string toolName, string expectedStatus,
        string? expectedError, string actualStatus, string? actualError, int terminalAuditCount,
        string? auditOutcome, string? auditReason, bool expectedMatch)
    {
        var item = new AgentCatalogInvokeCommand.InvocationCase("case-1", toolName, expectedStatus, expectedError);

        var matches = AgentCatalogInvokeCommand.CaseMatches(item, actualStatus, actualError,
            terminalAuditCount, auditOutcome, auditReason);

        Assert.That(matches, Is.EqualTo(expectedMatch));
    }

    [Test]
    public async Task InvokeExercisesPermittedAndDeniedMetadataThroughComposedRuntime()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "invocations.json");
        var output = Path.Combine(workspace.Path, "reports", "lab", "invocations-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        await File.WriteAllTextAsync(input, """
            {"schema":"kapilab-agent-catalog-invocation-cases-v1","version":1,"cases":[
              {"id":"metadata-safe","toolName":"list_connections","expectedStatus":"Succeeded","expectedErrorCode":null},
              {"id":"metadata-denied","toolName":"list_databases","expectedStatus":"Denied","expectedErrorCode":"PermissionDenied"}
            ]}
            """);

        var exitCode = await AgentCatalogInvokeCommand.RunAsync(input, output, workspace.Path);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var root = report.RootElement;
        var cases = root.GetProperty("cases");
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(root.GetProperty("evidenceKind").GetString(), Is.EqualTo("synthetic-agent-runtime-registry"));
            Assert.That(root.GetProperty("grantsSupported").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("mongoDocumentsAccessed").GetBoolean(), Is.False);
            Assert.That(cases.GetArrayLength(), Is.EqualTo(2));
            Assert.That(cases[0].GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
            Assert.That(cases[0].GetProperty("auditOutcome").GetString(), Is.EqualTo("Succeeded"));
            Assert.That(cases[0].GetProperty("auditDecisionReason").GetString(), Is.EqualTo("PolicyAllowed"));
            Assert.That(cases[0].GetProperty("matches").GetBoolean(), Is.True);
            Assert.That(cases[1].GetProperty("status").GetString(), Is.EqualTo("Denied"));
            Assert.That(cases[1].GetProperty("errorCode").GetString(), Is.EqualTo("PermissionDenied"));
            Assert.That(cases[1].GetProperty("auditOutcome").GetString(), Is.EqualTo("Denied"));
            Assert.That(cases[1].GetProperty("auditDecisionReason").GetString(), Is.EqualTo("PermissionMissing"));
            Assert.That(cases[1].GetProperty("matches").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task InvokeHandlesFiveHundredAllowedAndDeniedCallsThroughRuntime()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "invocations-500.json");
        var output = Path.Combine(workspace.Path, "reports", "lab", "invocations-500-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        var cases = Enumerable.Range(0, 500).Select(index => new
        {
            id = $"case-{index:D3}",
            toolName = "list_connections",
            expectedStatus = "Succeeded",
            expectedErrorCode = (string?)null
        });
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new
        {
            schema = "kapilab-agent-catalog-invocation-cases-v1",
            version = 1,
            cases
        }));

        var exitCode = await AgentCatalogInvokeCommand.RunAsync(input, output, workspace.Path);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var reportCases = report.RootElement.GetProperty("cases");
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(reportCases.GetArrayLength(), Is.EqualTo(500));
            Assert.That(reportCases.EnumerateArray().All(item => item.GetProperty("status").GetString() == "Succeeded"), Is.True);
            Assert.That(reportCases.EnumerateArray().All(item =>
                item.GetProperty("auditOutcome").GetString() == item.GetProperty("status").GetString() &&
                item.GetProperty("auditDecisionReason").GetString() == "PolicyAllowed" &&
                item.GetProperty("matches").GetBoolean()), Is.True);
        });
    }

    [Test]
    public async Task CatalogExportComposesTheRegisteredProviderRegistry()
    {
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, "reports", "lab", "catalog.json");

        var exitCode = await AgentCatalogExportCommand.RunAsync("local", "in-process", output, workspace.Path);

        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var root = snapshot.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-agent-catalog-v1"));
            Assert.That(root.GetProperty("providerId").GetString(), Is.EqualTo("local"));
            Assert.That(root.GetProperty("surface").GetString(), Is.EqualTo("in-process"));
            Assert.That(root.GetProperty("scope").GetString(), Is.EqualTo("provider-maximum"));
            Assert.That(root.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("tools").GetArrayLength(), Is.GreaterThan(0));
            var connections = root.GetProperty("tools").EnumerateArray()
                .Single(tool => tool.GetProperty("name").GetString() == "list_connections");
            Assert.That(connections.GetProperty("outputSchemaJson").GetString(), Does.Contain(":output\""));
            Assert.That(connections.GetProperty("outputSchemaSha256").GetString(), Is.Not.Empty);
        });
    }

    [TestCase("mongo_find")]
    [TestCase("create_workspace_file")]
    public async Task InvokeRefusesNonAllowlistedTools(string toolName)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "invocations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        await File.WriteAllTextAsync(input, $$"""
            {"schema":"kapilab-agent-catalog-invocation-cases-v1","version":1,"cases":[
              {"id":"forbidden","toolName":"{{toolName}}","expectedStatus":"Succeeded","expectedErrorCode":null}
            ]}
            """);

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AgentCatalogInvokeCommand.RunAsync(input, null, workspace.Path));
    }

    [TestCase("\"version\":\"1\"", "\"version\":1")]
    [TestCase("\"id\":17", "\"id\":\"metadata-safe\"")]
    [TestCase("\"expectedErrorCode\":[]", "\"expectedErrorCode\":null")]
    public async Task InvokeRejectsMalformedJsonValueTypes(string malformed, string valid)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "invocations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        var json = $$"""
            {"schema":"kapilab-agent-catalog-invocation-cases-v1","version":1,"cases":[
              {"id":"metadata-safe","toolName":"list_connections","expectedStatus":"Succeeded","expectedErrorCode":null}
            ]}
            """;
        await File.WriteAllTextAsync(input, json.Replace(valid, malformed, StringComparison.Ordinal));

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AgentCatalogInvokeCommand.RunAsync(input, null, workspace.Path));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-catalog-invoke-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}

