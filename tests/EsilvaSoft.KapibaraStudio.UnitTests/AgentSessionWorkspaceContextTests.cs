using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentSessionWorkspaceContextTests
{
    [TestCase("activeFile", false)]
    [TestCase("connectionName", false)]
    [TestCase("database", false)]
    [TestCase("collection", false)]
    [TestCase("activeFile", true)]
    [TestCase("connectionName", true)]
    [TestCase("database", true)]
    [TestCase("collection", true)]
    public async Task WorkspaceContextOmitsNamesThatExceedSchemaBoundsAfterRedaction(string field, bool expandsPastLimit)
    {
        using var rig = new AgentSessionToolsTestRig();
        const string provider = AgentProviderIds.GitHubCopilotSubscription;
        var permissions = rig.Permissions with { ProviderId = provider };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(true, true))
            with { AllowedConnectionIds = [rig.Profile.Id] };
        // Every assignment is synthetic; expanding the redaction placeholder can exceed the output schema.
        var name = expandsPastLimit ? string.Join(';', Enumerable.Repeat("password=x", 23)) : "password=x";
        Assert.That(name.Length, Is.LessThanOrEqualTo(255), "The input passes the existing name limit.");
        var snapshot = new AgentWorkspaceContext(DateTimeOffset.UtcNow,
            ActiveFileName: field == "activeFile" ? name : "query.js",
            ConnectionId: rig.Profile.Id.ToString("D"),
            ConnectionName: field == "connectionName" ? name : "profile",
            DatabaseName: field == "database" ? name : "app",
            CollectionName: field == "collection" ? name : "people");
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        Assert.That(rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(session, turn, provider,
            plan, permissions, snapshot)), Is.True);
        var result = await rig.Registry.InvokeAsync(new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1),
            new AgentInvocationContext(provider, null, session, turn), AgentOutputDestination.ProviderExternal(provider),
            AgentOutputDataScope.Metadata, "get_workspace_context", "{}");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var container = json.RootElement.GetProperty(field == "activeFile" ? "activeFile" : "tab");
        var propertyName = field == "activeFile" ? "name" : field;
        var actual = container.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() : null;
        Assert.Multiple(() =>
        {
            if (expandsPastLimit)
                Assert.That(actual, Is.Null, "An expanded name must not violate the published maxLength=255.");
            else
                Assert.That(actual, Is.EqualTo("password=[segredo removido]"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("password=x"));
            Assert.That(rig.Workspace.Captures, Is.Zero, "Use only the bound turn snapshot.");
            Assert.That(rig.Files.Reads, Is.Zero, "Workspace context never reads file contents.");
            Assert.That(rig.Metadata.Calls, Is.Zero);
            Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("password="));
        });
    }
}
