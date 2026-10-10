using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentCatalogPlanExportCommandTests
{
    [Test]
    public async Task ExportPlanCommandWritesMarkedSyntheticFilteredCatalog()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "plan.json");
        var output = Path.Combine(workspace.Path, "reports", "lab", "catalog-plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        var permissions = AgentProviderPermissions.Default("local") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
            EnabledReadTools = ["list_connections"],
        };
        var plan = new AgentToolCatalogPlanSnapshot.PlanInput(AgentToolCatalogPlanSnapshot.InputSchema,
            new AgentToolCatalogPlanSnapshot.PlanCaseInput("cli-plan", AgentOperationMode.Planning, permissions,
                false, true, false));
        await File.WriteAllTextAsync(input, AgentToolCatalogPlanSnapshot.Serialize(plan));

        var exitCode = await AgentCatalogPlanExportCommand.RunAsync("local", input, output, workspace.Path);

        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var root = snapshot.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-agent-catalog-plan-v1"));
            Assert.That(root.GetProperty("synthetic").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("toolInvocationPerformed").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("grantsVerified").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo("Planning"));
            Assert.That(root.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()),
                Is.EquivalentTo(["list_connections"]));
            Assert.That(root.GetProperty("snapshotSha256").GetString(), Does.Match("^[0-9a-f]{64}$"));
        });
    }

    [Test]
    public async Task ExportPlanCommandRejectsProviderMismatch()
    {
        using var workspace = new TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "data", "lab", "plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        var plan = new AgentToolCatalogPlanSnapshot.PlanInput(AgentToolCatalogPlanSnapshot.InputSchema,
            new AgentToolCatalogPlanSnapshot.PlanCaseInput("cli-plan", AgentOperationMode.Agent,
                AgentProviderPermissions.Default("local"), false, true, false));
        await File.WriteAllTextAsync(input, AgentToolCatalogPlanSnapshot.Serialize(plan));

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AgentCatalogPlanExportCommand.RunAsync("claude-code", input, null, workspace.Path));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-catalog-plan-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
