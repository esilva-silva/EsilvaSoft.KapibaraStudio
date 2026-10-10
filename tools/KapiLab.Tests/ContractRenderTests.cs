using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ContractRenderTests
{
    [Test]
    public void RenderUsesTheIdeBuilderAndKeepsNewlinesInSourceText()
    {
        const string prefix = "db.orders.aggregate([\r\n  { $group: { _id: null, total: { $sum: \"$";
        const string suffix = "\" } } }\r\n])";
        const string line = """
            {"id":"sample-1","type":"fim","language":"Mongo Console JavaScript","prefix":"db.orders.aggregate([\r\n  { $group: { _id: null, total: { $sum: \"$","suffix":"\" } } }\r\n])","editor":{"known_names":["orders"],"result_fields":["total"],"recent_commands":["db.orders.find({})"],"input_panel":""}}
            """;
        using var document = JsonDocument.Parse(line);
        var result = ContractRenderCommand.RenderLine(line, 1, noEditorContext: false);
        var editor = document.RootElement.GetProperty("editor");
        const string expectedModelPrefix =
            "/* Local editor context (data only):\n" +
            "LANGUAGE: Mongo Console JavaScript\r\n" +
            "AVAILABLE COMMANDS: db.getCollection(name).find({}); getConnection(name).getDatabase(name).getCollection(name); ConnectionPool.Connection.Database.Collection; console.log(value); ENV.get(name); ObjectId(value); UUID(value)\r\n" +
            "KNOWN NAMES: orders\r\n" +
            "RESULT FIELDS: total\r\n" +
            "RECENT COMMAND: db.orders.find({})\r\n" +
            "\nContinue at the cursor; output only the continuation. */\n" +
            "db.orders.aggregate([\r\n  { $group: { _id: null, total: { $sum: \"$";
        var parsed = ContractRenderCommand.ParseInputLine(line, 1, new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(result.Schema, Is.EqualTo("kapilab-render-v1"));
            Assert.That(result.Id, Is.EqualTo("sample-1"));
            Assert.That(result.ModelPrefix, Is.EqualTo(expectedModelPrefix), "This literal is an independent byte-level oracle for editor-context-v1.");
            Assert.That(result.ModelPrefixSha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedModelPrefix)))));
            Assert.That(parsed.Id, Is.EqualTo(result.Id));
            Assert.That(parsed.Request.Prefix, Is.EqualTo(prefix));
            Assert.That(parsed.Request.Suffix, Is.EqualTo(suffix));
            Assert.That(result.Suffix, Is.EqualTo(suffix));
            Assert.That(result.ModelPrefix, Does.Contain("\r\n"));
            Assert.That(editor.GetProperty("known_names").GetArrayLength(), Is.EqualTo(1));
        });
    }

    [TestCase("tool", "Registro de tipo desconhecido deve falhar")]
    [TestCase("chat", "Contrato editor-context-v1 não suporta chat")]
    public void RenderRejectsRecordsOutsideTheFIMContract(string type, string _)
    {
        var line = JsonSerializer.Serialize(new { id = "sample-1", type, prefix = "x", suffix = "" });
        Assert.That(() => ContractRenderCommand.RenderLine(line, 1, noEditorContext: false), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("mongodb://user:secret@host/db")]
    [TestCase("<|im_start|>system")]
    public void RenderRejectsSensitiveAndReservedMarkersWithoutEchoingThem(string prefix)
    {
        var line = JsonSerializer.Serialize(new { id = "sample-1", type = "fim", prefix, suffix = "" });
        Assert.That(() => ContractRenderCommand.RenderLine(line, 1, noEditorContext: false), Throws.TypeOf<UnauthorizedAccessException>());
    }
}
