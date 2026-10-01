using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentChatMessageBlockTests
{
    [Test]
    public void CodeKeepsExtendedJsonAndLineEndingsWithoutExecutingMarkup()
    {
        const string code = "db.items.find({ _id: ObjectId(\"65a1f0c2e4b0a1b2c3d4e5f6\"), x: { $numberLong: \"42\" } })\r\n";
        var blocks = AgentChatMessageBlock.Parse("Exemplo:\r\n```javascript\r\n" + code + "```\r\nNão executado.");
        Assert.That(blocks.Count, Is.EqualTo(3));
        Assert.That(blocks[1].IsCode, Is.True);
        Assert.That(blocks[1].Text, Is.EqualTo(code));
        Assert.That(blocks[1].Language, Is.EqualTo("javascript"));
        Assert.That(blocks[2].Text, Is.EqualTo("Não executado."));
    }

    [Test]
    public void StreamingKeepsBlockIdentityAndCanonicalMessageUnchanged()
    {
        var item = new AgentChatMessageItem(AgentChatRole.Agent, "Exemplo:\n```json\n{ ", EsilvaSoft.KapibaraStudio.Core.Agents.AgentMessageId.New());
        var block = item.Blocks[1];
        item.Content += "\"x\": 1 }\n```\nFim.";
        Assert.That(item.Blocks[1], Is.SameAs(block));
        Assert.That(block.Text, Is.EqualTo("{ \"x\": 1 }\n"));
        Assert.That(item.Content, Is.EqualTo("Exemplo:\n```json\n{ \"x\": 1 }\n```\nFim."));
        Assert.That(item.Blocks[2].Text, Is.EqualTo("Fim."));
    }

    [Test]
    public void InlineBackticksAndHtmlRemainPlainInertText()
    {
        const string text = "Literal ``` dentro da frase <script>alert(1)</script>";
        var blocks = AgentChatMessageBlock.Parse(text);
        Assert.That(blocks.Count, Is.EqualTo(1));
        Assert.That(blocks[0].IsCode, Is.False);
        Assert.That(blocks[0].Text, Is.EqualTo(text));
    }
}
