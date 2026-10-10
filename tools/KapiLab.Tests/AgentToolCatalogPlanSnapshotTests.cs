using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentToolCatalogPlanSnapshotTests
{
    [Test]
    public void ExportIntersectsProviderExposureWithModeAndPermissionPlanAndHashesSchemas()
    {
        var registry = new RegistryStub();
        var input = Plan("local", AgentOperationMode.Agent, ["list_connections", "list_databases", "get_cached_schema"]);

        var snapshot = AgentToolCatalogPlanSnapshot.Export(registry, "local", input);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Schema, Is.EqualTo("kapilab-agent-catalog-plan-v1"));
            Assert.That(snapshot.EvidenceKind, Is.EqualTo("synthetic-policy-input"));
            Assert.That(snapshot.Synthetic, Is.True);
            Assert.That(snapshot.Complete, Is.False);
            Assert.That(snapshot.ToolInvocationPerformed, Is.False);
            Assert.That(snapshot.GrantsVerified, Is.False);
            Assert.That(snapshot.Mode, Is.EqualTo("Agent"));
            Assert.That(snapshot.Tools.Select(static tool => tool.Name), Is.EquivalentTo(["list_connections", "list_databases", "propose_file_edit"]));
            Assert.That(snapshot.PlannedProductTools, Does.Contain("propose_file_edit"));
            Assert.That(snapshot.Tools.Single(static tool => tool.Name == "list_connections").Stage, Is.EqualTo("Metadata"));
            Assert.That(snapshot.Tools.Single(static tool => tool.Name == "list_connections").ConfirmationCategory, Is.EqualTo("MongoMetadataRead"));
            Assert.That(snapshot.Tools.Single(static tool => tool.Name == "list_connections").OutputScope, Is.EqualTo("Metadata"));
            Assert.That(snapshot.Tools[0].InputSchemaSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(snapshot.Tools[0].OutputSchemaSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(snapshot.SnapshotSha256, Does.Match("^[0-9a-f]{64}$"));
        });
    }

    [Test]
    public void PlanningModeAndDisabledReadPermissionsNarrowTools()
    {
        var registry = new RegistryStub();
        var input = Plan("local", AgentOperationMode.Planning, ["list_connections"]);

        var snapshot = AgentToolCatalogPlanSnapshot.Export(registry, "local", input);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Tools.Select(static tool => tool.Name), Is.EquivalentTo(["list_connections"]));
            Assert.That(snapshot.PlannedProductTools, Does.Not.Contain("propose_file_edit"));
            Assert.That(snapshot.Mode, Is.EqualTo("Planning"));
        });
    }

    [Test]
    public void RegistryProviderBoundaryIsPreservedEvenWhenPolicyNamesTool()
    {
        var registry = new RegistryStub();
        var input = Plan("unknown-provider", AgentOperationMode.Agent, ["list_connections"]);

        var snapshot = AgentToolCatalogPlanSnapshot.Export(registry, "unknown-provider", input);

        Assert.That(snapshot.Tools, Is.Empty);
    }

    [Test]
    public void ExportRejectsProviderMismatch()
    {
        var input = Plan("local", AgentOperationMode.Agent, ["list_connections"]);
        Assert.Throws<InvalidDataException>(() => AgentToolCatalogPlanSnapshot.Export(new RegistryStub(), "claude-code", input));
    }

    [TestCase("{\"schema\":\"wrong\",\"case\":{}}")]
    [TestCase("{\"schema\":\"kapilab-agent-catalog-plan-input-v1\",\"case\":{\"id\":\"bad\",\"mode\":\"Unknown\"}}")]
    [TestCase("{\"schema\":\"kapilab-agent-catalog-plan-input-v1\",\"case\":null}")]
    public void ParserRejectsInvalidInput(string json) =>
        Assert.That(AgentToolCatalogPlanSnapshot.ParseInput(json), Is.Null);

    [Test]
    public void ParserRejectsDuplicateProperties()
    {
        const string json = "{\"schema\":\"kapilab-agent-catalog-plan-input-v1\",\"Schema\":\"kapilab-agent-catalog-plan-input-v1\",\"case\":{}}";
        Assert.Throws<InvalidDataException>(() => AgentToolCatalogPlanSnapshot.ParseInput(json));
    }

    private static AgentToolCatalogPlanSnapshot.PlanInput Plan(string providerId, AgentOperationMode mode,
        IReadOnlyList<string> enabledTools)
    {
        var permissions = AgentProviderPermissions.Default(providerId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch,
            EnabledReadTools = enabledTools,
        };
        return new AgentToolCatalogPlanSnapshot.PlanInput(AgentToolCatalogPlanSnapshot.InputSchema,
            new AgentToolCatalogPlanSnapshot.PlanCaseInput("single-case", mode, permissions, true, true, false));
    }

    private sealed class RegistryStub : IAgentToolRegistry
    {
        private static readonly string[] LocalTools = ["list_connections", "list_databases", "get_cached_schema", "propose_file_edit"];
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public IReadOnlyList<AgentToolDescriptor> GetInProcessDescriptors(string providerId) =>
            providerId == "local" ? LocalTools.Select(Tool).ToArray() : [];
        public IReadOnlyList<AgentToolDescriptor> GetSessionChannelDescriptors(string providerId) => [];
        public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
            providerId == "local" && LocalTools.Contains(name, StringComparer.Ordinal) ? "{\"type\":\"object\"}" : null;
        public string? GetInProcessOutputSchemaJson(string providerId, string? name) =>
            providerId == "local" && LocalTools.Contains(name, StringComparer.Ordinal) ? "{\"type\":\"null\"}" : null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static AgentToolDescriptor Tool(string name) => new(name, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]);
    }
}
