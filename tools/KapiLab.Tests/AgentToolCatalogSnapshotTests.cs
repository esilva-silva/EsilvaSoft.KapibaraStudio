using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentToolCatalogSnapshotTests
{
    [Test]
    public void ExportUsesProviderSurfaceDescriptorsAndProviderScopedSchemas()
    {
        var registry = new RegistryStub();
        var snapshot = AgentToolCatalogSnapshot.Export(registry, "local", "in-process");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Schema, Is.EqualTo("kapilab-agent-catalog-v1"));
            Assert.That(snapshot.Scope, Is.EqualTo("provider-maximum"));
            Assert.That(snapshot.Complete, Is.False);
            Assert.That(snapshot.Tools.Select(tool => tool.Name).Single(), Is.EqualTo("local_tool"));
            Assert.That(snapshot.Tools[0].InputSchemaJson, Is.EqualTo("{\"type\":\"object\"}"));
            Assert.That(snapshot.Tools[0].OutputSchemaJson, Is.EqualTo("{\"type\":\"string\"}"));
            Assert.That(snapshot.Tools[0].InputSchemaSha256, Is.Not.Null);
            Assert.That(snapshot.Tools[0].OutputSchemaSha256, Is.Not.Null);
        });
    }

    [Test]
    public void InProcessOutputSchemaIsFailClosedForUnexposedProviderOrTool()
    {
        var registry = new RegistryStub();

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetInProcessOutputSchemaJson("unknown-provider", "local_tool"), Is.Null);
            Assert.That(registry.GetInProcessOutputSchemaJson("local", "unknown_tool"), Is.Null);
            Assert.That(AgentToolCatalogSnapshot.Export(registry, "unknown-provider", "in-process").Tools, Is.Empty);
        });
    }

    [Test]
    public void ExportOmitsApproveFromSessionMaximumAndUsesChannelSchemas()
    {
        var snapshot = AgentToolCatalogSnapshot.Export(new RegistryStub(), "claude-code", "session-channel");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Tools.Select(tool => tool.Name).Single(), Is.EqualTo("channel_tool"));
            Assert.That(snapshot.Tools[0].OutputSchemaJson, Is.EqualTo("{\"type\":\"null\"}"));
            Assert.That(snapshot.Tools[0].OutputSchemaSha256, Is.Not.Null);
        });
    }

    [Test]
    public void CheckDetectsContractDriftAndMissingSchemas()
    {
        var registry = new RegistryStub();
        var expected = AgentToolCatalogSnapshot.Export(registry, "local", "in-process");
        registry.InputSchema = null;
        var actual = AgentToolCatalogSnapshot.Export(registry, "local", "in-process");
        var result = AgentToolCatalogSnapshot.Check(expected, actual);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Differences, Does.Contain("tool_contract_changed:local_tool"));
        Assert.That(result.Differences, Does.Contain("schema_missing:local_tool"));
    }

    [Test]
    public void CheckDetectsInProcessOutputSchemaDrift()
    {
        var registry = new RegistryStub();
        var expected = AgentToolCatalogSnapshot.Export(registry, "local", "in-process");
        registry.OutputSchema = "{\"type\":\"integer\"}";
        var actual = AgentToolCatalogSnapshot.Export(registry, "local", "in-process");

        var result = AgentToolCatalogSnapshot.Check(expected, actual);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Differences, Does.Contain("tool_contract_changed:local_tool"));
    }

    [Test]
    public void SnapshotRoundTripValidatesVersionIdentityAndChecksStableContracts()
    {
        var registry = new RegistryStub();
        var snapshot = AgentToolCatalogSnapshot.Export(registry, "claude-code", "session-channel");
        var roundTrip = AgentToolCatalogSnapshot.Parse(AgentToolCatalogSnapshot.Serialize(snapshot));

        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(AgentToolCatalogSnapshot.Check(snapshot, roundTrip!).Matches, Is.True);
    }

    [Test]
    public void SnapshotParserRejectsCaseVariantDuplicateProperties()
    {
        var snapshot = AgentToolCatalogSnapshot.Export(new RegistryStub(), "local", "in-process");
        var serialized = AgentToolCatalogSnapshot.Serialize(snapshot);
        var json = "{\"Schema\":\"tampered\"," + serialized[1..];

        Assert.That(() => AgentToolCatalogSnapshot.Parse(json), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CatalogDescriptionMatchesPromptAndDescriptionDriftIsDetected()
    {
        var snapshot = AgentToolCatalogSnapshot.Export(new RegistryStub(), "local", "in-process");
        var tool = snapshot.Tools[0];
        using var prompt = System.Text.Json.JsonDocument.Parse(LocalAgentPromptBuilder.BuildToolsJson(
            [new(tool.Name, AgentToolDescriptions.Get(tool.Name), tool.InputSchemaJson!)])!);
        Assert.That(tool.Description, Is.EqualTo(prompt.RootElement[0].GetProperty("function").GetProperty("description").GetString()));
        var changed = snapshot with { Tools = [tool with { Description = "Changed description" }] };
        Assert.That(AgentToolCatalogSnapshot.Check(snapshot, changed).Matches, Is.False);
        foreach (var name in new[] { "list_connections", "list_databases", "list_collections", "get_indexes", "get_search_indexes",
            "get_cached_schema", "get_workspace_context", "get_query_results", "get_query_diagnostics", "propose_file_edit", "create_workspace_file", "approve" })
        {
            Assert.That(AgentToolDescriptions.Get(name), Has.Length.InRange(1, 200));
            Assert.That(AgentToolDescriptions.Get(name), Is.Not.EqualTo(name));
        }
    }

    private sealed class RegistryStub : IAgentToolRegistry
    {
        public string? InputSchema { get; set; } = "{\"type\":\"object\"}";
        public string OutputSchema { get; set; } = "{\"type\":\"string\"}";
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public IReadOnlyList<AgentToolDescriptor> GetInProcessDescriptors(string providerId) =>
            providerId == "local" ? [Tool("local_tool")] : [];
        public IReadOnlyList<AgentToolDescriptor> GetSessionChannelDescriptors(string providerId) =>
            providerId == "claude-code" ? [Tool("approve"), Tool("channel_tool")] : [];
        public string? GetSessionChannelInputSchemaJson(string providerId, string? name) =>
            providerId == "claude-code" && name == "channel_tool" ? InputSchema : null;
        public string? GetSessionChannelOutputSchemaJson(string providerId, string? name) =>
            providerId == "claude-code" && name == "channel_tool" ? "{\"type\":\"null\"}" : null;
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
            providerId == "local" && name == "local_tool" ? InputSchema : null;
        public string? GetInProcessOutputSchemaJson(string providerId, string? name) =>
            providerId == "local" && name == "local_tool" ? OutputSchema : null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private static AgentToolDescriptor Tool(string name) => new(name, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]);
    }
}
