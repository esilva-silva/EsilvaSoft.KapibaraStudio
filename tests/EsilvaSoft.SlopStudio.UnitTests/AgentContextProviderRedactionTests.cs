using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.UnitTests.Mcp;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Best-effort redaction of editor text shared with an agent turn. Each case plants a canary that must not reach the
/// authorized context, while ordinary query text around it survives.
/// </summary>
[TestFixture]
public sealed class AgentContextProviderRedactionTests
{
    private const string Canary = "Canary7Secret";

    private static readonly AgentContextProvider Provider =
        new(new McpBrokerFixture.FixedProfiles(ConnectionProfile.Create("p", "mongodb://localhost:27017")));

    [TestCase(@"{""uri"":""mongodb:\/\/svc:" + Canary + @"@db.internal:27017\/shop""}", TestName = "JsonEscapedUri")]
    [TestCase("mongodb%3A%2F%2Fsvc%3A" + Canary + "%40db.internal", TestName = "UrlEncodedUri")]
    [TestCase("mongodb+srv://svc:" + Canary + "@cluster0.example/shop", TestName = "SrvUri")]
    [TestCase(@"{ ""password"": """ + Canary + @""" }", TestName = "JsonPassword")]
    [TestCase(@"{\""password\"":\""" + Canary + @"\"",\""user\"":\""app\""}", TestName = "EscapedJsonPassword")]
    [TestCase("db.createUser({ user: 'app', pwd: '" + Canary + "', roles: [] })", TestName = "CreateUserPwd")]
    [TestCase("MONGO_PASSWORD=" + Canary, TestName = "EnvAssignment")]
    [TestCase("password = \"" + Canary + " with spaces\"", TestName = "QuotedAssignmentWithSpaces")]
    [TestCase("const apiKey = '" + Canary + "';", TestName = "ApiKeyAssignment")]
    [TestCase("client_secret=" + Canary + "&grant_type=x", TestName = "QueryStringSecret")]
    [TestCase("db.auth('admin', '" + Canary + "')", TestName = "DbAuthPositional")]
    [TestCase("db.getSiblingDB('admin').auth({ user: 'admin', pwd: \"" + Canary + "\" })", TestName = "DbAuthObject")]
    [TestCase("// key sk-proj-" + Canary + "abcdefghijkl", TestName = "OpenAiKey")]
    [TestCase("sk-ant-api03-" + Canary + "abcdefghijkl", TestName = "AnthropicKey")]
    [TestCase("ghp_" + Canary + "abcdefghijklmnopqrstuvwxyz0", TestName = "GitHubToken")]
    [TestCase("AKIA" + "CANARY7SECRET123", TestName = "AwsAccessKey")]
    [TestCase("AIza" + Canary + "abcdefghijklmnopqrstuvw", TestName = "GoogleApiKey")]
    [TestCase("Authorization: Bearer " + Canary + "abcdefghij", TestName = "BearerToken")]
    [TestCase("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ" + Canary + ".c2lnbmF0dXJlLXZhbHVl", TestName = "Jwt")]
    [TestCase("-----BEGIN PRIVATE KEY-----\nMIIE" + Canary + "\n-----END PRIVATE KEY-----", TestName = "PemBlock")]
    public async Task SecretIsRedactedFromSharedText(string secretText)
    {
        var shared = "db.orders.find({ status: \"paid\" })\n" + secretText;
        var snapshot = await Provider.CaptureAsync(new AgentContextCaptureRequest("tab-a", 1, SelectedText: shared),
            CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.AuthorizedContext, Does.Not.Contain(Canary).IgnoreCase);
            Assert.That(snapshot.AuthorizedContext, Does.Not.Contain("CANARY7SECRET"));
            Assert.That(snapshot.AuthorizedContext, Does.Contain("db.orders.find({ status: \"paid\" })"));
        });
    }

    [Test]
    public async Task OrdinaryQueryTextIsNotRedacted()
    {
        const string query = "db.tokens.find({ tokenCount: { $gt: 3 }, passwordPolicy: \"strong\", status: \"paid\" })";
        var snapshot = await Provider.CaptureAsync(new AgentContextCaptureRequest("tab-a", 1, EditorText: query),
            CancellationToken.None);
        Assert.That(snapshot.AuthorizedContext, Does.Contain(query));
    }

    [Test]
    public void LimitIsEnforcedOnTheRedactedTextBecauseMarkersCanGrowIt()
    {
        // Each 12-char "mongodb://a " grows into a longer marker: the raw text fits, the redacted text does not.
        var unit = "mongodb://a ";
        var raw = string.Concat(Enumerable.Repeat(unit, AgentContextProvider.MaximumSharedTextChars / unit.Length));
        Assert.That(raw.Length, Is.LessThanOrEqualTo(AgentContextProvider.MaximumSharedTextChars));
        var error = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            Provider.CaptureAsync(new AgentContextCaptureRequest("tab-a", 1, SelectedText: raw), CancellationToken.None));
        Assert.That(error!.Code, Is.EqualTo("ContextTooLarge"));
    }

    [Test]
    public async Task RedactedTextThatFitsIsSharedWhole()
    {
        var text = "db.a.find()\nmongodb://svc:" + Canary + "@h";
        var snapshot = await Provider.CaptureAsync(new AgentContextCaptureRequest("tab-a", 1, SelectedText: text),
            CancellationToken.None);
        Assert.That(snapshot.AuthorizedContext, Does.Contain("db.a.find()").And.Contain("[connection string removida]")
            .And.Not.Contain(Canary));
    }
}
