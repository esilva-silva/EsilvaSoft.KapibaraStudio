using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LineDiffTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static string ApplyAll(string text, IEnumerable<AgentEditHunk> hunks)
    {
        foreach (var hunk in hunks)
        {
            var result = LineDiff.ApplyHunk(text, hunk, fallbackLineEnding: "\n");
            Assert.That(result.Status, Is.EqualTo(LineDiffHunkStatus.Succeeded), $"hunk {hunk.Index}");
            text = result.Text!;
        }

        return text;
    }

    private static string RevertAll(string text, IEnumerable<AgentEditHunk> hunks)
    {
        foreach (var hunk in hunks)
        {
            var result = LineDiff.RevertHunk(text, hunk, fallbackLineEnding: "\n");
            Assert.That(result.Status, Is.EqualTo(LineDiffHunkStatus.Succeeded), $"hunk {hunk.Index}");
            text = result.Text!;
        }

        return text;
    }

    [Test]
    public void IdenticalTextsHaveNoHunks() =>
        Assert.That(LineDiff.Compute(Lines("a", "b"), Lines("a", "b")), Is.Empty);

    [Test]
    public void InsertionRemovalAndSubstitutionAreSeparateHunks()
    {
        var original = Lines("1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
        var proposed = Lines("1", "2", "novo", "3", "4", "5", "6", "8", "9", "10", "11", "doze");
        var hunks = LineDiff.Compute(original, proposed);
        Assert.Multiple(() =>
        {
            Assert.That(hunks, Has.Count.EqualTo(3));
            Assert.That(hunks[0].OriginalLines, Is.Empty);
            Assert.That(hunks[0].ProposedLines, Is.EqualTo(["novo"]));
            Assert.That(hunks[0].OriginalStartLine, Is.EqualTo(2));
            Assert.That(hunks[1].OriginalLines, Is.EqualTo(["7"]));
            Assert.That(hunks[1].ProposedLines, Is.Empty);
            Assert.That(hunks[2].OriginalLines, Is.EqualTo(["12"]));
            Assert.That(hunks[2].ProposedLines, Is.EqualTo(["doze"]));
            Assert.That(hunks.Select(static hunk => hunk.Index), Is.EqualTo([0, 1, 2]));
            Assert.That(hunks.All(static hunk => hunk.State == AgentEditHunkState.Pending));
        });
    }

    [Test]
    public void HunksApplyAndRevertInAnyOrder()
    {
        var original = Lines("1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
        var proposed = Lines("0", "1", "2", "3", "4", "cinco", "6", "7", "8", "9", "10", "11");
        var hunks = LineDiff.Compute(original, proposed);
        Assert.That(hunks, Has.Count.GreaterThan(1));
        Assert.Multiple(() =>
        {
            Assert.That(ApplyAll(original, hunks), Is.EqualTo(proposed));
            Assert.That(ApplyAll(original, hunks.Reverse()), Is.EqualTo(proposed));
            Assert.That(RevertAll(proposed, hunks), Is.EqualTo(original));
            Assert.That(RevertAll(proposed, hunks.Reverse()), Is.EqualTo(original));
        });
    }

    [Test]
    public void ApplyingOneHunkLeavesTheOthersUntouched()
    {
        var original = Lines("a", "b", "c", "d", "e", "f", "g", "h");
        var proposed = Lines("A", "b", "c", "d", "e", "f", "g", "H");
        var hunks = LineDiff.Compute(original, proposed);
        var result = LineDiff.ApplyHunk(original, hunks[1]);
        Assert.That(result.Text, Is.EqualTo(Lines("a", "b", "c", "d", "e", "f", "g", "H")));
    }

    [Test]
    public void CrlfFileKeepsItsTerminatorWhenProposalUsesLf()
    {
        var original = "db.a.find()\r\ndb.b.find()\r\ndb.c.find()\r\n";
        var proposed = "db.a.find()\ndb.b.find({ x: 1 })\ndb.c.find()\n";
        var hunks = LineDiff.Compute(original, proposed);
        var applied = ApplyAll(original, hunks);
        Assert.Multiple(() =>
        {
            Assert.That(hunks, Has.Count.EqualTo(1));
            Assert.That(applied, Is.EqualTo("db.a.find()\r\ndb.b.find({ x: 1 })\r\ndb.c.find()\r\n"));
            Assert.That(RevertAll(applied, hunks), Is.EqualTo(original));
            Assert.That(LineDiff.DetectLineEnding(original), Is.EqualTo("\r\n"));
            Assert.That(LineDiff.NormalizeLineEndings(proposed, "\r\n"), Is.EqualTo(applied));
        });
    }

    [Test]
    public void TrailingNewlineChangesAreHunks()
    {
        var hunks = LineDiff.Compute("a\nb", "a\nb\n");
        Assert.Multiple(() =>
        {
            Assert.That(hunks, Has.Count.EqualTo(1));
            Assert.That(ApplyAll("a\nb", hunks), Is.EqualTo("a\nb\n"));
        });
    }

    [Test]
    public void EditElsewhereStillAppliesButChangedTargetIsStale()
    {
        var original = Lines("1", "2", "3", "4", "5", "6", "7", "8");
        var proposed = Lines("1", "2", "3", "4", "cinco", "6", "7", "8");
        var hunk = LineDiff.Compute(original, proposed).Single();

        var shifted = Lines("zero", "extra", "1", "2", "3", "4", "5", "6", "7", "8");
        var edited = Lines("1", "2", "3", "4", "5 editado", "6", "7", "8");
        var anchorChanged = Lines("1", "2", "3", "quatro", "5", "6", "7", "8");
        Assert.Multiple(() =>
        {
            Assert.That(LineDiff.ApplyHunk(shifted, hunk).Text,
                Is.EqualTo(Lines("zero", "extra", "1", "2", "3", "4", "cinco", "6", "7", "8")));
            Assert.That(LineDiff.ApplyHunk(edited, hunk).Status, Is.EqualTo(LineDiffHunkStatus.Stale));
            Assert.That(LineDiff.ApplyHunk(anchorChanged, hunk).Status, Is.EqualTo(LineDiffHunkStatus.Stale));
            Assert.That(LineDiff.RevertHunk(original, hunk).Status, Is.EqualTo(LineDiffHunkStatus.Stale),
                "Reverter um trecho não aplicado não encontra as linhas propostas.");
            Assert.That(LineDiff.ApplyHunk(proposed, hunk).Status, Is.EqualTo(LineDiffHunkStatus.Stale),
                "Aplicar duas vezes não duplica o trecho.");
        });
    }

    [Test]
    public void RepeatedBlockIsAmbiguousUnlessTheHintIsExact()
    {
        var original = Lines("x", "y", "alvo", "y", "x");
        var hunk = LineDiff.Compute(original, Lines("x", "y", "novo", "y", "x")).Single();
        var shifted = Lines("topo", "x", "y", "alvo", "y", "x", "y", "alvo", "y", "x");
        Assert.Multiple(() =>
        {
            Assert.That(LineDiff.ApplyHunk(shifted, hunk).Status, Is.EqualTo(LineDiffHunkStatus.Stale),
                "Dois candidatos e nenhum na posição exata: ambíguo.");
            Assert.That(LineDiff.ApplyHunk(shifted, hunk, hintLine: 7).Text,
                Is.EqualTo(Lines("topo", "x", "y", "alvo", "y", "x", "y", "novo", "y", "x")));
        });
    }

    [Test]
    public void PeriodicTextNeedsTheShiftedHintAfterAnEarlierInsertion()
    {
        string[] period = ["a", "b", "c", "d"];
        var originalLines = Enumerable.Repeat(period, 6).SelectMany(static lines => lines).ToList();
        var proposedLines = originalLines.ToList();
        proposedLines[13] = "B";
        proposedLines.InsertRange(2, ["novo1", "novo2", "novo3"]);
        var original = string.Join("\n", originalLines);
        var proposed = string.Join("\n", proposedLines);
        var hunks = LineDiff.Compute(original, proposed);
        Assert.That(hunks, Has.Count.EqualTo(2));

        var afterFirst = LineDiff.ApplyHunk(original, hunks[0]).Text!;
        var defaultHint = LineDiff.ApplyHunk(afterFirst, hunks[1]);
        var shiftedHint = LineDiff.ApplyHunk(afterFirst, hunks[1], hunks[1].OriginalStartLine + 3);
        Assert.Multiple(() =>
        {
            Assert.That(defaultHint.Status, Is.EqualTo(LineDiffHunkStatus.Stale), "Nunca aplica no bloco periódico errado.");
            Assert.That(shiftedHint.Text, Is.EqualTo(proposed));
        });
    }

    [Test]
    public void MixedLineEndingsOutsideTheHunkArePreservedByteForByte()
    {
        const string original = "a\r\nb\nc\r\nd\ne\r\nf\ng\r\nh";
        const string proposed = "a\nb\nc\nd\nE\nf\ng\nh\nnova";
        var hunks = LineDiff.Compute(original, proposed);
        var applied = ApplyAll(original, hunks.Reverse());
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo("a\r\nb\nc\r\nd\nE\r\nf\ng\r\nh\r\nnova"));
            Assert.That(RevertAll(applied, hunks.Reverse()), Is.EqualTo(original));
        });
    }

    [Test]
    public void EditDescribesOnlyTheRegion()
    {
        const string original = "a\r\nb\r\nc\r\nd\r\ne";
        var hunk = LineDiff.Compute(original, "a\nb\nC\nd\ne").Single();
        var result = LineDiff.ApplyHunk(original, hunk);
        Assert.Multiple(() =>
        {
            Assert.That(result.Edit, Is.EqualTo(new LineDiffTextEdit(6, 3, "C\r\n")));
            Assert.That(result.Edit!.ApplyTo(original), Is.EqualTo(result.Text));
        });
    }

    [Test]
    public void OversizedInputIsATypedRefusal()
    {
        var big = new string('a', LineDiff.MaximumInputChars + 1);
        Assert.Multiple(() =>
        {
            Assert.That(LineDiff.TryCompute(big, "a", out var hunks), Is.False);
            Assert.That(hunks, Is.Empty);
            Assert.That(() => LineDiff.Compute("a", big), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void LargeRewriteFallsBackToOneHunk()
    {
        var original = string.Join("\n", Enumerable.Range(0, 3000).Select(static i => "a" + i));
        var proposed = string.Join("\n", Enumerable.Range(0, 3000).Select(static i => "b" + i));
        var hunks = LineDiff.Compute(original, proposed);
        Assert.Multiple(() =>
        {
            Assert.That(hunks, Has.Count.EqualTo(1));
            Assert.That(ApplyAll(original, hunks), Is.EqualTo(proposed));
        });
    }

    [Test]
    public void RandomEditsRoundTrip()
    {
        var random = new Random(20260926);
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var original = RandomLines(random);
            var proposed = RandomLines(random);
            var hunks = LineDiff.Compute(original, proposed);
            // Highly repetitive lines: exact hints (what the editor knows) make the location deterministic.
            Assert.That(ApplyAll(original, hunks.Reverse()), Is.EqualTo(proposed), $"iteração {iteration}");
            var text = proposed;
            foreach (var hunk in hunks)
            {
                text = LineDiff.RevertHunk(text, hunk, hunk.OriginalStartLine, "\n").Text!;
            }

            Assert.That(text, Is.EqualTo(original), $"iteração {iteration}");
        }
    }

    private static string RandomLines(Random random)
    {
        var count = random.Next(0, 25);
        return string.Join("\n", Enumerable.Range(0, count).Select(_ => "l" + random.Next(0, 6)));
    }
}
