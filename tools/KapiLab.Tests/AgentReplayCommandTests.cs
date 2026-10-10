using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentReplayCommandTests
{
    [Test]
    public async Task ReplayComposesRuntimeAndReportsOnlySyntheticFailClosedResult()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "data", "lab"));
        var input = Path.Combine(workspace.Path, "data", "lab", "cases.json");
        await File.WriteAllTextAsync(input, ValidInput("case-1"));
        var originalOut = Console.Out;
        using var capture = new StringWriter();
        try
        {
            Console.SetOut(capture);
            var code = await AgentReplayCommand.RunAsync("data/lab/cases.json", null, workspace.Path);
            Assert.That(code, Is.Zero);
        }
        finally { Console.SetOut(originalOut); }
        using var report = JsonDocument.Parse(capture.ToString());
        var root = report.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-agent-replay-report-v2"));
            Assert.That(root.GetProperty("evidenceKind").GetString(), Is.EqualTo("synthetic-agent-runtime-boundary"));
            Assert.That(root.GetProperty("grantsSupported").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("taskSuccess").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("cases")[0].GetProperty("matches").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("cases")[0].GetProperty("actualResultSha256").GetString(), Is.EqualTo(AgentReplayCommand.ExpectedHash));
        });
    }

    [Test]
    public async Task ReplayRunsAllowlistedMetadataToolWithExplicitPerCaseDecisionAndCorrelatedAudit()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "cases.json");
        await File.WriteAllTextAsync(input, ValidDatabaseInput(includeGrant: true, includeDenial: true));
        var originalOut = Console.Out;
        using var capture = new StringWriter();
        int code;
        try
        {
            Console.SetOut(capture);
            code = await AgentReplayCommand.RunAsync("cases.json", null, workspace.Path);
        }
        finally { Console.SetOut(originalOut); }

        using var report = JsonDocument.Parse(capture.ToString());
        var root = report.RootElement;
        var cases = root.GetProperty("cases");
        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero);
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-agent-replay-report-v2"));
            Assert.That(root.GetProperty("evidenceKind").GetString(), Is.EqualTo("synthetic-agent-runtime-boundary"));
            Assert.That(root.GetProperty("grantsSupported").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("taskSuccess").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(cases.GetArrayLength(), Is.EqualTo(2));
            Assert.That(cases[0].GetProperty("permissionDecision").GetString(), Is.EqualTo("grant"));
            Assert.That(cases[0].GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
            Assert.That(cases[0].GetProperty("actualResultSha256").GetString(), Is.EqualTo(AgentReplayCommand.ExpectedDatabasesHash));
            Assert.That(cases[0].GetProperty("auditOutcome").GetString(), Is.EqualTo("Succeeded"));
            Assert.That(cases[0].GetProperty("auditDecisionReason").GetString(), Is.EqualTo("PolicyAllowed"));
            Assert.That(cases[0].GetProperty("terminalAuditCount").GetInt32(), Is.EqualTo(1));
            Assert.That(cases[0].GetProperty("metadataHandlerCalls").GetInt32(), Is.EqualTo(1));
            Assert.That(cases[0].GetProperty("matches").GetBoolean(), Is.True);
            Assert.That(cases[1].GetProperty("permissionDecision").GetString(), Is.EqualTo("deny"));
            Assert.That(cases[1].GetProperty("status").GetString(), Is.EqualTo("Denied"));
            Assert.That(cases[1].GetProperty("actualResultSha256").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(cases[1].GetProperty("auditOutcome").GetString(), Is.EqualTo("Denied"));
            Assert.That(cases[1].GetProperty("auditDecisionReason").GetString(), Is.EqualTo("PermissionMissing"));
            Assert.That(cases[1].GetProperty("terminalAuditCount").GetInt32(), Is.EqualTo(1));
            Assert.That(cases[1].GetProperty("metadataHandlerCalls").GetInt32(), Is.Zero);
            Assert.That(cases[1].GetProperty("matches").GetBoolean(), Is.True);
        });
    }

    [TestCase("\"toolName\":\"list_databases\"", "\"toolName\":\"list_collections\"", null)]
    [TestCase("\"argumentsJson\":\"{\\\"connectionId\\\":\\\"$fixture\\\"}\"", "\"argumentsJson\":\"{}\"", null)]
    [TestCase("\"permissionDecision\":\"grant\"", "\"permissionDecision\":\"allow\"", "grant")]
    [TestCase("$grant-hash", "\"expectedResultSha256\":null", "grant")]
    [TestCase("\"status\":\"Denied\"", "\"status\":\"Succeeded\"", "deny")]
    [TestCase("\"errorCode\":\"PermissionDenied\"", "\"errorCode\":\"Other\"", "deny")]
    public async Task ReplayRejectsInvalidMetadataToolDecisionAndExpectedResult(string oldValue, string newValue, string? decision)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "cases.json");
        var valid = ValidDatabaseInput(includeGrant: decision != "deny", includeDenial: decision != "grant");
        var needle = oldValue == "$grant-hash" ? $"\"expectedResultSha256\":\"{AgentReplayCommand.ExpectedDatabasesHash}\"" : oldValue;
        await File.WriteAllTextAsync(input, valid.Replace(needle, newValue, StringComparison.Ordinal));
        Assert.ThrowsAsync<InvalidDataException>(async () => await AgentReplayCommand.RunAsync("cases.json", null, workspace.Path));
    }

    [TestCase("\"toolName\":\"list_collections\"")]
    [TestCase("\"argumentsJson\":\"{\\\"database\\\":\\\"x\\\"}\"")]
    [TestCase("\"grants\":[]")]
    public async Task ReplayRejectsOutOfScopeOrGrantInputsBeforeRuntime(string replacement)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "cases.json");
        var json = ValidInput("case-1");
        if (replacement == "\"grants\":[]") json = json.Replace("\"expectedResultSha256\"", "\"grants\":[],\"expectedResultSha256\"", StringComparison.Ordinal);
        else json = json.Replace("\"toolName\":\"list_connections\"", replacement, StringComparison.Ordinal);
        await File.WriteAllTextAsync(input, json);
        Assert.ThrowsAsync<InvalidDataException>(async () => await AgentReplayCommand.RunAsync("cases.json", null, workspace.Path));
    }

    [TestCase("\"schema\":\"kapilab-agent-replay-cases-v1\"", "\"schema\":1")]
    [TestCase("\"version\":1", "\"version\":\"1\"")]
    [TestCase("\"id\":\"case-1\"", "\"id\":true")]
    [TestCase("\"toolName\":\"list_connections\"", "\"toolName\":false")]
    [TestCase("\"argumentsJson\":\"{}\"", "\"argumentsJson\":[]")]
    [TestCase("\"status\":\"Succeeded\"", "\"status\":1")]
    [TestCase("\"truncated\":false", "\"truncated\":\"false\"")]
    public async Task ReplayRejectsWrongJsonValueTypesAsInvalidInput(string oldValue, string newValue)
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "cases.json");
        var json = ValidInput("case-1").Replace(oldValue, newValue, StringComparison.Ordinal);
        await File.WriteAllTextAsync(input, json);
        Assert.ThrowsAsync<InvalidDataException>(async () => await AgentReplayCommand.RunAsync("cases.json", null, workspace.Path));
    }

    [Test]
    public async Task ReplayRejectsDuplicateIdsAndCannotOverwriteInput()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "cases.json");
        await File.WriteAllTextAsync(input, ValidInput("same", "same"));
        Assert.ThrowsAsync<InvalidDataException>(async () => await AgentReplayCommand.RunAsync("cases.json", null, workspace.Path));
        await File.WriteAllTextAsync(input, ValidInput("case-1"));
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await AgentReplayCommand.RunAsync("cases.json", "cases.json", workspace.Path));
    }

    private static string ValidInput(string id, string? secondId = null)
    {
        var entries = new[] { id, secondId }.Where(value => value is not null).Select(value => $"{{\"id\":\"{value}\",\"toolName\":\"list_connections\",\"argumentsJson\":\"{{}}\",\"expectedResult\":{{\"status\":\"Succeeded\",\"data\":{{\"connections\":[],\"truncated\":false}}}},\"expectedResultSha256\":\"{AgentReplayCommand.ExpectedHash}\"}}");
        return $"{{\"schema\":\"kapilab-agent-replay-cases-v1\",\"version\":1,\"cases\":[{string.Join(',', entries)}]}}";
    }

    private static string ValidDatabaseInput(bool includeGrant, bool includeDenial)
    {
        var cases = new List<string>();
        if (includeGrant)
            cases.Add($"{{\"id\":\"db-grant\",\"toolName\":\"list_databases\",\"argumentsJson\":\"{{\\\"connectionId\\\":\\\"$fixture\\\"}}\",\"permissionDecision\":\"grant\",\"expectedResult\":{{\"status\":\"Succeeded\",\"data\":{{\"names\":[],\"truncated\":false}}}},\"expectedResultSha256\":\"{AgentReplayCommand.ExpectedDatabasesHash}\"}}");
        if (includeDenial)
            cases.Add("{\"id\":\"db-deny\",\"toolName\":\"list_databases\",\"argumentsJson\":\"{\\\"connectionId\\\":\\\"$fixture\\\"}\",\"permissionDecision\":\"deny\",\"expectedResult\":{\"status\":\"Denied\",\"errorCode\":\"PermissionDenied\",\"data\":null},\"expectedResultSha256\":null}");
        return $"{{\"schema\":\"kapilab-agent-replay-cases-v2\",\"version\":2,\"cases\":[{string.Join(',', cases)}]}}";
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-agent-replay-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
