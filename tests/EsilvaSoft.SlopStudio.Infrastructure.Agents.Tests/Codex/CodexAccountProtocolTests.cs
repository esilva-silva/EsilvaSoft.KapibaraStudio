using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Codex;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Codex;

[TestFixture]
public sealed class CodexAccountProtocolTests
{
    private static readonly string[] LoginParameterNames = ["type", "useHostedLoginSuccessPage", "appBrand"];
    private static readonly string[] AccountReadParameterNames = ["refreshToken"];
    private static readonly string[] ParsedAccountProperties = ["PlanType"];
    [Test]
    public void LoginStartBuildsOnlyTheManagedChatGptBrowserFlow()
    {
        using var request = JsonDocument.Parse(CodexAccountProtocol.BuildLoginStartRequest(7));
        var root = request.RootElement;
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("account/login/start"));
        Assert.That(root.GetProperty("id").GetInt64(), Is.EqualTo(7));

        var parameters = root.GetProperty("params");
        Assert.That(parameters.EnumerateObject().Select(static property => property.Name),
            Is.EquivalentTo(LoginParameterNames));
        Assert.That(parameters.GetProperty("type").GetString(), Is.EqualTo("chatgpt"));
        Assert.That(parameters.GetProperty("useHostedLoginSuccessPage").GetBoolean(), Is.True);
        Assert.That(parameters.GetProperty("appBrand").GetString(), Is.EqualTo("codex"));
    }

    [Test]
    public void AccountReadNeverRequestsTokenRefresh()
    {
        using var request = JsonDocument.Parse(CodexAccountProtocol.BuildAccountReadRequest(0));
        Assert.That(request.RootElement.GetProperty("method").GetString(), Is.EqualTo("account/read"));
        Assert.That(request.RootElement.GetProperty("params").EnumerateObject().Select(static property => property.Name),
            Is.EqualTo(AccountReadParameterNames));
        Assert.That(request.RootElement.GetProperty("params").GetProperty("refreshToken").GetBoolean(), Is.False);
    }

    [Test]
    public void LoginCancelAndLogoutUseOnlyTheDocumentedAccountMethods()
    {
        var loginId = Guid.Parse("de08caf0-e245-4d36-853f-76b611d30a4e");
        using var cancel = JsonDocument.Parse(CodexAccountProtocol.BuildLoginCancelRequest(8, loginId.ToString("B")));
        Assert.That(cancel.RootElement.GetProperty("method").GetString(), Is.EqualTo("account/login/cancel"));
        Assert.That(cancel.RootElement.GetProperty("params").GetProperty("loginId").GetString(),
            Is.EqualTo(loginId.ToString("D")));

        using var logout = JsonDocument.Parse(CodexAccountProtocol.BuildLogoutRequest(9));
        Assert.That(logout.RootElement.GetProperty("method").GetString(), Is.EqualTo("account/logout"));
        Assert.That(logout.RootElement.TryGetProperty("params", out _), Is.False);
    }

    [TestCase("")]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void LoginCancelRejectsInvalidLoginIds(string loginId)
    {
        Assert.Throws<ArgumentException>(() => CodexAccountProtocol.BuildLoginCancelRequest(1, loginId));
    }

    [Test]
    public void AccountReadAcceptsChatGptAndDiscardsIdentityFields()
    {
        const string response = """
            {"id":1,"result":{"account":{"type":"chatgpt","email":"private@example.com","planType":"pro"},"requiresOpenaiAuth":true}}
            """;

        var accepted = CodexAccountProtocol.TryParseAccountReadResponse(Utf8(response), out var account);

        Assert.That(accepted, Is.True);
        Assert.That(account, Is.EqualTo(new CodexChatGptAccount("pro")));
        Assert.That(account!.GetType().GetProperties().Select(static property => property.Name),
            Is.EquivalentTo(ParsedAccountProperties), "No email, account identifier, or credential is returned by the parser.");
    }

    [TestCase("apiKey", true)]
    [TestCase("amazonBedrock", false)]
    public void AccountReadRejectsOtherAccountTypes(string type, bool requiresOpenaiAuth)
    {
        var response = JsonSerializer.Serialize(new
        {
            id = 1,
            result = new
            {
                account = new { type, email = (string?)null, planType = "pro" },
                requiresOpenaiAuth,
            },
        });

        Assert.That(CodexAccountProtocol.TryParseAccountReadResponse(Utf8(response), out var account), Is.False);
        Assert.That(account, Is.Null);
    }

    [Test]
    public void AccountReadRejectsSignedOutMalformedUnexpectedAndDuplicateFields()
    {
        string[] rejected =
        [
            "{\"id\":1,\"result\":{\"account\":null,\"requiresOpenaiAuth\":false}}",
            "{\"id\":1,\"result\":{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"pro\"},\"requiresOpenaiAuth\":false}}",
            "{\"id\":1,\"result\":{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"unknown-future-plan\"},\"requiresOpenaiAuth\":true}}",
            "{\"id\":1,\"result\":{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"pro\",\"accessToken\":\"secret\"},\"requiresOpenaiAuth\":true}}",
            "{\"id\":1,\"result\":{\"account\":{\"type\":\"chatgpt\",\"type\":\"apiKey\",\"email\":null,\"planType\":\"pro\"},\"requiresOpenaiAuth\":true}}",
            "{\"id\":1,\"error\":{\"code\":-1,\"message\":\"failed\"}}",
            "{malformed",
        ];

        foreach (var json in rejected)
        {
            Assert.That(CodexAccountProtocol.TryParseAccountReadResponse(Utf8(json), out var account), Is.False, json);
            Assert.That(account, Is.Null, json);
        }
    }

    [Test]
    public void AccountUpdatedAcceptsOnlyChatGptAuthMode()
    {
        const string chatGptUpdate = "{\"authMode\":\"chatgpt\",\"planType\":\"plus\"}";
        Assert.That(CodexAccountProtocol.TryParseAccountUpdatedParams(Utf8(chatGptUpdate), out var account), Is.True);
        Assert.That(account, Is.EqualTo(new CodexChatGptAccount("plus")));

        string[] rejected =
        [
            "{\"authMode\":\"apikey\",\"planType\":null}",
            "{\"authMode\":\"chatgptAuthTokens\",\"planType\":\"pro\"}",
            "{\"authMode\":null,\"planType\":null}",
            "{\"planType\":\"pro\"}",
            "{\"authMode\":\"chatgpt\",\"planType\":\"future-plan\"}",
            "{\"authMode\":\"chatgpt\",\"authMode\":\"apikey\"}",
            "{\"authMode\":\"chatgpt\",\"accessToken\":\"secret\"}",
            "null",
        ];

        foreach (var json in rejected)
        {
            Assert.That(CodexAccountProtocol.TryParseAccountUpdatedParams(Utf8(json), out var rejectedAccount), Is.False, json);
            Assert.That(rejectedAccount, Is.Null, json);
        }
    }

    [Test]
    public void AccountParsersBoundPayloadSizeAndNesting()
    {
        var oversized = Encoding.UTF8.GetBytes(new string(' ', 32 * 1024 + 1));
        var tooDeep = Utf8("{\"account\":{\"nested\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{}}}}}}}}}");

        Assert.That(CodexAccountProtocol.TryParseAccountUpdatedParams(oversized, out _), Is.False);
        Assert.That(CodexAccountProtocol.TryParseAccountUpdatedParams(tooDeep, out _), Is.False);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
