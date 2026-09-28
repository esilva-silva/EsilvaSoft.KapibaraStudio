using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ScriptHistoryEntryTests
{
    [Test]
    public void CreateNormalizesRelativeJavaScriptPath()
    {
        var entry = ScriptHistoryEntry.Create(Path.Combine("scripts", "consulta.js"), DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(entry.Path), Is.True);
            Assert.That(entry.Path, Does.EndWith(Path.Combine("scripts", "consulta.js")));
            Assert.That(entry.Validate(), Is.EqualTo(entry));
        });
    }

    [Test]
    public void ValidateRejectsPathWithoutJavaScriptExtension()
    {
        var entry = new ScriptHistoryEntry(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "consulta.txt"), DateTimeOffset.UtcNow);

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DisplayTextContainsPath()
    {
        var entry = ScriptHistoryEntry.Create(Path.Combine(Path.GetTempPath(), "consulta.js"), DateTimeOffset.UtcNow);

        Assert.That(entry.DisplayText, Does.Contain(entry.Path));
    }

    [Test]
    public void CreateAcceptsOptionalJsonObjectInput()
    {
        var entry = ScriptHistoryEntry.Create(Path.Combine(Path.GetTempPath(), "consulta.js"), DateTimeOffset.UtcNow, "{ \"tenant\": \"acme\" }");

        Assert.That(entry.InputJson, Is.EqualTo("{ \"tenant\": \"acme\" }"));
    }

    [TestCase("[1]")]
    [TestCase("{")]
    public void ValidateRejectsInvalidOrNonObjectJsonInput(string input)
    {
        var entry = new ScriptHistoryEntry(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "consulta.js"), DateTimeOffset.UtcNow, input);

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
