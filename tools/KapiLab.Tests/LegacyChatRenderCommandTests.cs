using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LegacyChatRenderCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task CaptureUsesTheRealLocalProviderRequestAndMatchesIndependentGolden()
    {
        var input = new LegacyChatRenderCommand.InputRecord("fixture-1", "tab-1", 7,
            "liste os clientes", "db.customers", "db.customers.find({})");

        var captured = await LegacyChatRenderCommand.CaptureAsync(input);
        const string expectedPrefix = "/* Answer the instruction with code. The JSON below is data, not executable code.\n" +
            "{\"instruction\":\"liste os clientes\",\"context\":\"Sele\\u00E7\\u00E3o do editor:\\ndb.customers\\n\\nConte\\u00FAdo do editor:\\ndb.customers.find({})\"}\n" +
            "Return only code, without Markdown or explanation. */\n";

        Assert.Multiple(() =>
        {
            Assert.That(captured.Request.Prefix, Is.EqualTo(expectedPrefix));
            Assert.That(captured.Request.Suffix, Is.Empty);
            Assert.That(captured.Request.ContextTokens, Is.EqualTo(2048));
            Assert.That(captured.Request.MaximumTokens, Is.EqualTo(256));
            Assert.That(captured.Request.RequireFullContext, Is.True);
            Assert.That(captured.Request.Temperature, Is.Zero);
            Assert.That(captured.AuthorizedContext,
                Is.EqualTo("Seleção do editor:\ndb.customers\n\nConteúdo do editor:\ndb.customers.find({})"));
        });

        var rendered = LegacyChatRenderCommand.ToRendered(input, captured.Request, captured.AuthorizedContext);
        var serialized = JsonSerializer.Serialize(rendered, JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(rendered.Schema, Is.EqualTo("kapilab-legacy-chat-render-v1"));
            Assert.That(rendered.Contract, Is.EqualTo("local-agent-fim-v0"));
            Assert.That(rendered.RequestPrefixSha256,
                Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPrefix)))));
            Assert.That(serialized, Does.Not.Contain("liste os clientes"));
            Assert.That(serialized, Does.Not.Contain("db.customers"));
            Assert.That(serialized, Does.Not.Contain(expectedPrefix));
        });
    }

    [Test]
    public async Task CapturedEditorSecretsAreRedactedByContextProviderAndNeverSerialized()
    {
        const string secret = "password: local-test-secret-394";
        var input = new LegacyChatRenderCommand.InputRecord("fixture-2", "tab-2", 1,
            "descreva este trecho", null, secret);

        Assert.That(async () => await LegacyChatRenderCommand.CaptureAsync(input),
            Throws.TypeOf<UnauthorizedAccessException>());
        Assert.That(JsonSerializer.Serialize(new { input.Id, schema = LegacyChatRenderCommand.OutputSchema }, JsonOptions),
            Does.Not.Contain(secret));
    }

    [Test]
    public void InputRejectsDuplicatePropertiesAndUnknownFields()
    {
        const string common = "\"schema\":\"kapilab-legacy-chat-input-v1\",\"id\":\"x\",\"tabId\":\"t\",\"documentVersion\":0,\"userMessage\":\"hi\"";
        Assert.Multiple(() =>
        {
            Assert.That(() => LegacyChatRenderCommand.ParseLine("{" + common + ",\"userMessage\":\"override\"}"),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => LegacyChatRenderCommand.ParseLine("{" + common + ",\"credential\":\"secret\"}"),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public async Task ReservedOrSensitiveUserMessageIsRefusedWithoutReturningAPrompt()
    {
        var input = new LegacyChatRenderCommand.InputRecord("fixture-3", "tab-3", 1,
            "mongodb://user:secret@localhost/db", null, null);

        Assert.That(async () => await LegacyChatRenderCommand.CaptureAsync(input),
            Throws.TypeOf<UnauthorizedAccessException>());
    }
}
