using AvaloniaEdit.Document;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Desktop.Language.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Text;

[TestFixture]
public sealed class AvaloniaTextSnapshotTests
{
    [Test]
    public void CaptureUsesAnImmutableAvaloniaEditSnapshotWithUtf16Offsets()
    {
        var document = new TextDocument("a😀\r\nb");
        ITextSnapshot snapshot = new AvaloniaTextSnapshot(document);

        document.Replace(1, 2, "x");

        Assert.That(snapshot.Length, Is.EqualTo(6));
        Assert.That(snapshot[1], Is.EqualTo('\uD83D'));
        Assert.That(snapshot.GetText(new TextSpan(1, 2)), Is.EqualTo("😀"));
        var buffer = new char[3];
        snapshot.CopyTo(3, buffer);
        Assert.That(new string(buffer), Is.EqualTo("\r\nb"));
        Assert.That(snapshot.GetText(0, snapshot.Length), Is.EqualTo("a😀\r\nb"));
        Assert.That(document.Text, Is.EqualTo("ax\r\nb"));
    }

    [Test]
    public void VersionsAndChangesComeFromAvaloniaEditLineage()
    {
        var document = new TextDocument("db.Items.find({})");
        var first = new AvaloniaTextSnapshot(document);
        var sameVersion = new AvaloniaTextSnapshot(document);

        document.Insert(3, "Main.");
        document.Replace(0, 2, "database");
        var current = new AvaloniaTextSnapshot(document);

        Assert.That(sameVersion.Version, Is.EqualTo(first.Version));
        Assert.That(current.Version.IsSameDocument(first.Version), Is.True);
        Assert.That(current.Version.IsNewerThan(first.Version), Is.True);
        Assert.That(current.GetChangesSince(first), Is.EqualTo(new[]
        {
            new TextChange(3, 0, 5),
            new TextChange(0, 2, 8)
        }));
        Assert.That(current.GetChangesSince(current), Is.Empty);
        Assert.That(first.GetChangesSince(current), Is.Null);
    }

    [Test]
    public void ChangesAreUnknownAcrossDocumentsAndOtherAdapters()
    {
        var firstDocument = new TextDocument("db.Items");
        var first = new AvaloniaTextSnapshot(firstDocument);
        firstDocument.Insert(3, "Main.");
        var current = new AvaloniaTextSnapshot(firstDocument);

        Assert.That(current.GetChangesSince(new AvaloniaTextSnapshot(new TextDocument(firstDocument.Text))), Is.Null);
        Assert.That(current.GetChangesSince(new StringTextSnapshot("db.Items")), Is.Null);
        Assert.That(current.GetChangesSince(first), Is.EqualTo(new[] { new TextChange(3, 0, 5) }));
    }
}
