using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class ScriptHistoryEntryTests
{
    [Test]
    public void CreateRejectsRelativeJavaScriptPathWithoutConsultingCurrentDirectory()
    {
        Assert.That(() => ScriptHistoryEntry.Create(Path.Combine("scripts", "consulta.js"), DateTimeOffset.UtcNow),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsPathWithoutJavaScriptExtension()
    {
        var entry = new ScriptHistoryEntry(Guid.NewGuid(), SyntheticPaths.Combine("consulta.txt"), DateTimeOffset.UtcNow);

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DisplayTextContainsPath()
    {
        var entry = ScriptHistoryEntry.Create(SyntheticPaths.Combine("consulta.js"), DateTimeOffset.UtcNow);

        Assert.That(entry.DisplayText, Does.Contain(entry.Path));
    }

    [Test]
    public void CreateAcceptsOptionalJsonObjectInput()
    {
        var entry = ScriptHistoryEntry.Create(SyntheticPaths.Combine("consulta.js"), DateTimeOffset.UtcNow, "{ \"tenant\": \"acme\" }");

        Assert.That(entry.InputJson, Is.EqualTo("{ \"tenant\": \"acme\" }"));
    }

    [TestCase("[1]")]
    [TestCase("{")]
    public void ValidateRejectsInvalidOrNonObjectJsonInput(string input)
    {
        var entry = new ScriptHistoryEntry(Guid.NewGuid(), SyntheticPaths.Combine("consulta.js"), DateTimeOffset.UtcNow, input);

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
