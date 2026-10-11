using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentFixtureTests
{
    private static readonly string[] RequiredScenarios = ["permission_missing", "grant_expired", "grant_revoked", "confirmation_denied", "invalid_arguments", "quota_exceeded", "truncated"];
    private static string RepositoryRoot => FindRepository();
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new DirectoryNotFoundException("Checkout não encontrado.");
    }

    [TestCase("mongodb://127.0.0.1:27017", true)]
    [TestCase("mongodb://[::1]:27017", true)]
    [TestCase("mongodb://127.0.0.1:27017/?directConnection=true", true)]
    [TestCase("mongodb://example.com:27017", false)]
    [TestCase("mongodb://localhost:27017", false)]
    [TestCase("mongodb://127.0.0.1:27017/userdata", false)]
    [TestCase("mongodb://user:secret@127.0.0.1:27017", false)]
    [TestCase("mongodb://127.0.0.1:27017/?replicaSet=external", false)]
    [TestCase("mongodb://127.0.0.1:27017,example.com:27017", false)]
    [TestCase("mongodb+srv://127.0.0.1", false)]
    public void FixtureUriCannotRedirectDriver(string uri, bool allowed)
    {
        if (allowed) Assert.That(KapiSimFixture.RequireUri(uri), Does.EndWith("/?directConnection=true"));
        else Assert.That(() => KapiSimFixture.RequireUri(uri), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("sample_mflix")]
    [TestCase("admin")]
    [TestCase("kapi_sim_")]
    [TestCase("kapi_sim_store/other")]
    [TestCase("KAPI_SIM_store")]
    public void NonFixtureDatabaseIsRefusedBeforeMongoAccess(string database) =>
        Assert.That(() => KapiSimFixture.RequireDatabase(database), Throws.TypeOf<InvalidDataException>());

    [TestCase("database", "sample_mflix")]
    [TestCase("database", "kapi_sim_unseeded")]
    [TestCase("connectionId", "11111111-1111-1111-1111-111111111111")]
    [TestCase("path", "C:/user/secret.js")]
    public void FixtureRefusesRedirectionBeforeSeed(string field, string value)
    {
        var source = Path.Combine(RepositoryRoot, "tools/KapiLab/Fixtures/kapi-sim/cases/reference.json");
        var fixture = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        fixture["cases"]![0]!["arguments"]![field] = value;
        var path = Path.Combine(RepositoryRoot, "tmp", "refused-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, fixture.ToJsonString());
            Assert.That(async () => await AgentFixtureCommand.RunAsync(path, null, RepositoryRoot), Throws.TypeOf<InvalidDataException>());
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void SyntheticPortsBelongOnlyToLabExecutable()
    {
        var port = typeof(AgentFixturePorts);
        Assert.That(port.IsNotPublic, Is.True);
        Assert.That(port.Assembly.GetName().Name, Is.EqualTo("EsilvaSoft.KapibaraStudio.KapiLab"));
        Assert.That(typeof(AgentPlatformOptions).Assembly.GetType(port.FullName!), Is.Null);
        var desktopProject = System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryRoot, "src/EsilvaSoft.KapibaraStudio.Desktop/EsilvaSoft.KapibaraStudio.Desktop.csproj"));
        Assert.That(desktopProject.Descendants("ProjectReference").Select(reference => reference.Attribute("Include")!.Value),
            Has.None.Contains("KapiLab"));
    }

    [Test]
    public void FixtureBlocksSamplingDuringToolsAndRemoteDriverSettings()
    {
        using var access = new FixtureMongoAccess { ToolExecutionActive = true };
        var source = new FixtureMetadataSource(new MongoMetadataSource(), new(StringComparer.Ordinal) { "kapi_sim_store" }, access);
        var profile = EsilvaSoft.KapibaraStudio.Core.ConnectionProfile.Create("Fixture", "mongodb://127.0.0.1:27017/?directConnection=true");
        Assert.That(() => source.SampleSchemaAsync(profile, "kapi_sim_store", "items", new(), CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => source.SampleConcreteCollectionSchemaBoundedAsync(profile, "kapi_sim_store", "items", new(), 1024, CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => access.GetClient(MongoDB.Driver.MongoClientSettings.FromConnectionString("mongodb://remote.invalid:27017/?directConnection=true")), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ReferenceCoversEveryToolAndProductionFailureScenario()
    {
        var input = AgentFixtureCommand.ReadInput(RepositoryRoot,
            Path.Combine(RepositoryRoot, "tools/KapiLab/Fixtures/kapi-sim/cases/reference.json"), false);
        Assert.That(input.Cases.Length, Is.GreaterThanOrEqualTo(200));
        foreach (var tool in AgentProductToolNames.ReadTools.Append(AgentProductToolNames.ProposeFileEdit).Append(AgentProductToolNames.CreateWorkspaceFile))
        {
            Assert.That(input.Cases.Count(item => item.Tool == tool && item.Scenario == "success"), Is.GreaterThanOrEqualTo(20), tool);
            Assert.That(input.Cases.Where(item => item.Tool == tool).Select(item => item.Scenario),
                Is.SupersetOf(RequiredScenarios), tool);
        }
    }

    [TestCase(AgentOperationMode.Agent, 11)]
    [TestCase(AgentOperationMode.Planning, 9)]
    [TestCase(AgentOperationMode.Automatic, 11)]
    [TestCase(AgentOperationMode.AskConfirmations, 11)]
    public async Task CatalogIsCompleteWithAllSchemasAndNoRemovedNames(AgentOperationMode mode, int count)
    {
        var temporary = Path.Combine(RepositoryRoot, "tmp", "catalog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(temporary, "workspace.db"),
                new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.Metadata,
                    InProcessToolExposureStage = AgentToolExposureStage.Metadata,
                    CopilotToolExposureStage = AgentToolExposureStage.Metadata,
                    ClaudeToolExposureStage = AgentToolExposureStage.Metadata });
            AgentFixturePorts.Register(services);
            await using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<IAgentToolRegistry>();
            var catalog = AgentToolCatalogSnapshot.Export(registry, "local", "in-process", mode);
            Assert.That(catalog.Complete, Is.True);
            Assert.That(catalog.Tools, Has.Length.EqualTo(count));
            Assert.That(AgentToolCatalogSnapshot.Check(catalog, AgentToolCatalogSnapshot.Parse(AgentToolCatalogSnapshot.Serialize(catalog))!).Matches, Is.True);
            string[] removed = ["mongo_find", "mongo_count", "sample_documents", "mongo_find_one", "get_document", "mongo_distinct", "mongo_explain",
                "get_collection_schema", "insert_one", "update_one", "delete_one", "create_index", "drop_index"];
            foreach (var providerId in new[] { LocalAgentProvider.Id, AgentProviderIds.GitHubCopilotSubscription, AgentProviderIds.ClaudeCodeSubscription, "unknown" })
                foreach (var surface in new[] { "in-process", "session-channel" })
                    foreach (var name in removed)
                        Assert.That(AgentToolCatalogSnapshot.Export(registry, providerId, surface, mode).Tools.Select(tool => tool.Name), Does.Not.Contain(name));
            var drift = catalog with { Tools = catalog.Tools.Select((tool, index) => index == 0 ? tool with { MaxResultBytes = 17 } : tool).ToArray() };
            Assert.That(AgentToolCatalogSnapshot.Check(catalog, drift).Matches, Is.False);
            var tampered = System.Text.Json.Nodes.JsonNode.Parse(AgentToolCatalogSnapshot.Serialize(catalog))!;
            tampered["tools"]![0]!["outputSchema"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "null" };
            Assert.That(AgentToolCatalogSnapshot.Parse(tampered.ToJsonString()), Is.Null);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Test]
    public void SchemaValidatorRejectsWrongTypesMissingPropertiesAndInventedFields()
    {
        const string schema = """{"type":"object","required":["count"],"additionalProperties":false,"properties":{"count":{"type":"integer","minimum":0}}}""";
        FixtureSchemaValidation.Validate(schema, "{\"count\":2}");
        foreach (var data in new[] { "{}", "{\"count\":-1}", "{\"count\":1.5}", "{\"count\":\"2\"}", "{\"count\":2,\"secret\":true}" })
            Assert.That(() => FixtureSchemaValidation.Validate(schema, data), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ReplayNeverIgnoresPermissionDecisionsOrDocumentValues()
    {
        const string expected = """{"status":"Succeeded","result":"{\"executionId\":\"a\",\"results\":[\"42\"]}","error":null,"truncated":false}""";
        Assert.That(AgentFixtureResponseComparison.Matches(expected, expected.Replace("\\\"a\\\"", "\\\"b\\\""), ["executionId"]), Is.True);
        Assert.That(AgentFixtureResponseComparison.Matches(expected, expected.Replace("42", "43"), ["executionId"]), Is.False);
        Assert.That(AgentFixtureResponseComparison.Matches(expected, expected.Replace("Succeeded", "Denied"), ["executionId"]), Is.False);
        Assert.That(() => AgentFixtureResponseComparison.ValidateFields(["results"]), Throws.TypeOf<InvalidDataException>());
    }

    [Test, Explicit("Recria os três bancos kapi_sim_* no MongoDB loopback; execução manual de desenvolvimento.")]
    public async Task RealReferenceInvocationsMatchAndValidateOutputSchemas()
    {
        var code = await AgentFixtureCommand.RunAsync("tools/KapiLab/Fixtures/kapi-sim/cases/reference.json",
            "reports/lab/pr-3/reference-test.json", RepositoryRoot);
        Assert.That(code, Is.Zero);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "reports/lab/pr-3/reference-test.json")));
        var cases = report.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.That(cases.SelectMany(item => item.GetProperty("mongoCommands").EnumerateArray()).Any(command => command.GetProperty("prohibited").GetBoolean()), Is.False);
        foreach (var item in cases.Where(item => item.GetProperty("tool").GetString() is "get_query_results" or "get_query_diagnostics"))
            Assert.That(item.GetProperty("mongoCommands").GetArrayLength(), Is.Zero, item.GetProperty("id").GetString());
        foreach (var tool in AgentProductToolNames.ReadTools.Where(name => name != AgentProductToolNames.GetSearchIndexes)
            .Append(AgentProductToolNames.ProposeFileEdit).Append(AgentProductToolNames.CreateWorkspaceFile))
            Assert.That(cases.Count(item => item.GetProperty("tool").GetString() == tool && item.GetProperty("outputSchemaValidated").GetBoolean()), Is.GreaterThanOrEqualTo(20), tool);
    }
}
