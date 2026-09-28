using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Text;

[TestFixture]
public sealed class TextSnapshotTests
{
    [Test]
    public void SpansAreHalfOpenWhileTheCaretIntersectsBothEdges()
    {
        var span = TextSpan.FromBounds(4, 9);
        Assert.That((span.Start, span.Length, span.End, span.IsEmpty), Is.EqualTo((4, 5, 9, false)));
        Assert.That(span.Contains(4) && span.Contains(8), Is.True);
        Assert.That(span.Contains(9) || span.Contains(3), Is.False);
        Assert.That(span.IntersectsWith(4) && span.IntersectsWith(9) && !span.IntersectsWith(10), Is.True);
        var caret = new TextSpan(9, 0);
        Assert.That(caret.IsEmpty && caret.IntersectsWith(9) && !caret.Contains(9), Is.True);
        Assert.That(span.Contains(new TextSpan(5, 4)) && !span.Contains(new TextSpan(5, 5)), Is.True);
        Assert.That(span.OverlapsWith(new TextSpan(8, 3)) && !span.OverlapsWith(new TextSpan(9, 3)) && !span.OverlapsWith(new TextSpan(6, 0)), Is.True);
        Assert.That(span.ToString(), Is.EqualTo("[4..9)"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextSpan(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextSpan(1, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextSpan(int.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = TextSpan.FromBounds(5, 4));
    }

    [Test]
    public void ChangesExposeOldAndNewSpans()
    {
        var change = new TextChange(5, 3, 1);
        Assert.That((change.OldSpan, change.NewSpan, change.Delta), Is.EqualTo((new TextSpan(5, 3), new TextSpan(5, 1), -2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextChange(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextChange(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextChange(0, 0, -1));
    }

    [Test]
    public void StringSnapshotReadsUtf16CodeUnitsThroughTheContract()
    {
        ITextSnapshot snapshot = new StringTextSnapshot("a😀\r\nb");
        Assert.That(snapshot.Length, Is.EqualTo(6));
        Assert.That(snapshot[1], Is.EqualTo('\uD83D'));
        Assert.That(snapshot.GetText(new TextSpan(1, 2)), Is.EqualTo("😀"));
        var buffer = new char[3];
        snapshot.CopyTo(3, buffer);
        Assert.That(new string(buffer), Is.EqualTo("\r\nb"));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.CopyTo(4, new char[3]));
    }

    [Test]
    public void MinimalAdaptersGetBoundedCopyAndSpanReadsFromTheContract()
    {
        ITextSnapshot snapshot = new MinimalSnapshot("db.Projects");
        var buffer = new char[8];
        snapshot.CopyTo(3, buffer);
        Assert.That(new string(buffer), Is.EqualTo("Projects"));
        Assert.That(snapshot.GetText(TextSpan.FromBounds(0, 2)), Is.EqualTo("db"));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.CopyTo(4, new char[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.CopyTo(-1, new char[1]));
    }

    [Test]
    public void EditsAdvanceTheVersionOfTheSameDocumentOnly()
    {
        var first = new StringTextSnapshot("db.Projects.find({})");
        var second = first.Insert(18, "Active: true");
        var other = new StringTextSnapshot(first.Text);
        Assert.That(second.Text, Is.EqualTo("db.Projects.find({Active: true})"));
        Assert.That(first.Text, Is.EqualTo("db.Projects.find({})"), "snapshots are immutable");
        Assert.That(second.Version.IsSameDocument(first.Version) && second.Version.IsNewerThan(first.Version), Is.True);
        Assert.That(first.Version.IsNewerThan(second.Version), Is.False);
        Assert.That(other.Version.IsSameDocument(first.Version), Is.False);
        Assert.That(second.Version.IsNewerThan(other.Version), Is.False, "Versões de documentos diferentes nunca são comparáveis.");
        Assert.That(new StringTextSnapshot("x", new TextSnapshotVersion(7, 42)).Insert(0, "y").Version, Is.EqualTo(new TextSnapshotVersion(7, 43)));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Replace(19, 5, "x"));
    }

    [Test]
    public void ChangesSinceAnOlderSnapshotAreOrderedAndUnknownHistoryIsNull()
    {
        var a = new StringTextSnapshot("db.Projects.find({})");
        var b = a.Insert(18, "Active: true");
        var c = b.Replace(0, 2, "database");
        var d = c.Remove(c.Length - 1, 1);
        var changes = d.GetChangesSince(a);
        Assert.That(changes, Is.EqualTo(new[] { new TextChange(18, 0, 12), new TextChange(0, 2, 8), new TextChange(c.Length - 1, 1, 0) }));
        Assert.That(a.Length + changes!.Sum(change => change.Delta), Is.EqualTo(d.Length));
        Assert.That(c.GetChangesSince(b), Is.EqualTo(new[] { new TextChange(0, 2, 8) }));
        Assert.That(d.GetChangesSince(d), Is.Empty);
        Assert.That(a.GetChangesSince(d), Is.Null, "snapshot mais novo");
        Assert.That(d.GetChangesSince(new StringTextSnapshot(a.Text)), Is.Null, "outro documento");
        Assert.That(d.GetChangesSince(new MinimalSnapshot(a.Text)), Is.Null, "outra implementação");
        var sibling = a.Remove(0, 3);
        Assert.That(b.GetChangesSince(sibling), Is.Null, "mesma sequência em outra linhagem");
        Assert.That(c.GetChangesSince(sibling), Is.Null);
        var reloaded = d.WithText("db.Other.find()");
        Assert.That(reloaded.GetChangesSince(d), Is.Null, "alteração não descrita");
        var edited = reloaded.Insert(0, "// ");
        Assert.That(edited.GetChangesSince(reloaded), Is.EqualTo(new[] { new TextChange(0, 0, 3) }));
        Assert.That(edited.GetChangesSince(a), Is.Null);
    }

    private sealed class MinimalSnapshot(string text) : ITextSnapshot
    {
        public TextSnapshotVersion Version => default;
        public int Length => text.Length;
        public char this[int index] => text[index];
        public string GetText(int start, int length) => text.Substring(start, length);
        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous) => null;
    }
}
