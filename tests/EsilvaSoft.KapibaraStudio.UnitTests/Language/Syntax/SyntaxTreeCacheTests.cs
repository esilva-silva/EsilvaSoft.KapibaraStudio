using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

[TestFixture]
public sealed class SyntaxTreeCacheTests
{
    [Test]
    public void SameSnapshotAndEquivalentWrapperReuseTheTreeWithoutReadingText()
    {
        var cache = new SyntaxTreeCache();
        var source = new StringTextSnapshot("db.Clientes.find({})");
        var first = new ProbeSnapshot(source);
        var wrapper = new ProbeSnapshot(source);
        var parsed = cache.GetOrParse(first);

        Assert.That(parsed.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(parsed.IsSuperseded, Is.False);
        Assert.That(parsed.Tree!.Version, Is.EqualTo(source.Version));
        Assert.That(cache.GetOrParse(first).Tree, Is.SameAs(parsed.Tree));
        var reused = cache.GetOrParse(wrapper);
        Assert.That(reused.IsReused, Is.True);
        Assert.That(reused.ParseKind, Is.EqualTo(SyntaxTreeParseKind.None));
        Assert.That(reused.Tree, Is.SameAs(parsed.Tree));
        Assert.That((first.ReadCount, wrapper.ReadCount), Is.EqualTo((1, 0)));
    }

    [Test]
    public void NewVersionReplacesTheOldTreeAndOldRequestsDoNotReadText()
    {
        var cache = new SyntaxTreeCache();
        var first = new StringTextSnapshot("db.A.find({})");
        var oldTree = cache.GetOrParse(first).Tree;
        var next = first.Replace(3, 1, "B");
        var parsed = cache.GetOrParse(next);

        Assert.That(parsed.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Incremental), "A edição do statement é processada pelo caminho incremental.");
        Assert.That(parsed.Tree, Is.Not.SameAs(oldTree));
        Assert.That(parsed.Tree!.Version, Is.EqualTo(next.Version));
        var old = new ProbeSnapshot(first, () => Assert.Fail("Versão antiga não deve ser lida."));
        var discarded = cache.GetOrParse(old);
        Assert.That(discarded.IsSuperseded, Is.True);
        Assert.That(discarded.Tree, Is.Null);
        Assert.That(discarded.ParseKind, Is.EqualTo(SyntaxTreeParseKind.None));
        Assert.That(cache.GetOrParse(next).Tree, Is.SameAs(parsed.Tree));
    }

    [Test]
    public void SameSequenceInAnotherLineageDoesNotReuseTheTree()
    {
        var cache = new SyntaxTreeCache();
        var origin = new StringTextSnapshot("db.A");
        var left = origin.Insert(origin.Length, ".find()");
        var right = origin.Insert(origin.Length, ".drop()");
        Assert.That(left.Version, Is.EqualTo(right.Version));
        Assert.That(right.GetChangesSince(left), Is.Null);
        var leftTree = cache.GetOrParse(left).Tree;
        var result = cache.GetOrParse(right);
        Assert.That(result.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(result.Tree, Is.Not.SameAs(leftTree));
        Assert.That(result.Tree!.Tokens.Any(token => right.GetText(token.Start, token.Length) == "drop"), Is.True);
        Assert.That(cache.GetOrParse(right).Tree, Is.SameAs(result.Tree));
    }

    [Test]
    public void UnknownHistoryEvenWithIdenticalTextRequiresAFullParse()
    {
        var cache = new SyntaxTreeCache();
        var initial = new StringTextSnapshot("db.A.find()");
        var first = cache.GetOrParse(initial);
        var detached = new StringTextSnapshot(initial.Text, initial.Version);
        var result = cache.GetOrParse(detached);
        Assert.That(result.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(result.Tree, Is.Not.SameAs(first.Tree));
        var reloaded = detached.WithText("db.B.find()");
        Assert.That(reloaded.GetChangesSince(detached), Is.Null);
        Assert.That(cache.GetOrParse(reloaded).ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
    }

    [Test]
    public void LexerModeIsPartOfTheCacheKey()
    {
        var cache = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot("/abc/i");
        var script = cache.GetOrParse(snapshot);
        var json = cache.GetOrParse(snapshot, MongoLexerMode.Json);
        Assert.That(script.Tree!.Tokens.Any(token => token.Kind == MongoTokenKind.Regex), Is.True);
        Assert.That(json.Tree!.Tokens.Any(token => token.Kind == MongoTokenKind.Regex), Is.False);
        Assert.That(json.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(json.Mode, Is.EqualTo(MongoLexerMode.Json));
        Assert.That(cache.GetOrParse(snapshot, MongoLexerMode.Json).Tree, Is.SameAs(json.Tree));
        Assert.That(cache.GetOrParse(snapshot).ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
    }

    [Test]
    public void EditorsWithEqualVersionNumbersKeepIndependentEntries()
    {
        var cache = new SyntaxTreeCache();
        var a = new StringTextSnapshot("db.A");
        var b = new StringTextSnapshot("db.B");
        Assert.That(a.Version.Sequence, Is.EqualTo(b.Version.Sequence));
        var treeA = cache.GetOrParse(a).Tree;
        var treeB = cache.GetOrParse(b).Tree;
        Assert.That(treeB, Is.Not.SameAs(treeA));
        Assert.That(cache.GetOrParse(a).Tree, Is.SameAs(treeA));
        var nextA = a.Insert(a.Length, ".find()");
        cache.GetOrParse(nextA);
        Assert.That(cache.GetOrParse(b).Tree, Is.SameAs(treeB));
        Assert.That(cache.RemoveDocument(a.Version.DocumentId), Is.True);
        Assert.That(cache.RemoveDocument(a.Version.DocumentId), Is.False);
        Assert.That(cache.GetOrParse(b).Tree, Is.SameAs(treeB));
        Assert.That(cache.GetOrParse(nextA).ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
    }

    [Test]
    public void UndoWithTheSameTextDoesNotReuseAnEarlierVersion()
    {
        var cache = new SyntaxTreeCache();
        var a = new StringTextSnapshot("db.A");
        var original = cache.GetOrParse(a).Tree;
        var b = a.Insert(a.Length, ".");
        cache.GetOrParse(b);
        var restored = b.Remove(b.Length - 1, 1);
        Assert.That(restored.Text, Is.EqualTo(a.Text));
        var result = cache.GetOrParse(restored);
        Assert.That(result.Tree, Is.Not.SameAs(original));
        Assert.That(result.Tree!.Version, Is.EqualTo(restored.Version));
        Assert.That(cache.GetOrParse(a).IsSuperseded, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LateParseCannotReplaceANewerVersionOrSiblingLineage(bool sibling)
    {
        var cache = new SyntaxTreeCache();
        var origin = new StringTextSnapshot("db.A");
        var first = origin.Insert(origin.Length, ".find()");
        var next = sibling ? origin.Insert(origin.Length, ".drop()") : first.Insert(first.Length, ";");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new ProbeSnapshot(first, () =>
        {
            started.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True, "Liberação da leitura bloqueada.");
        });
        var pending = Task.Run(() => cache.GetOrParse(slow));
        SyntaxTreeCacheResult latest;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            latest = await Task.Run(() => cache.GetOrParse(next)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(latest.Tree!.Version, Is.EqualTo(next.Version));
        }
        finally { release.Set(); }
        var late = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(late.Tree, Is.Null);
        Assert.That(late.IsSuperseded, Is.True);
        Assert.That(late.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full), "Trabalho descartado ainda foi uma análise completa.");
        Assert.That(cache.GetOrParse(next).Tree, Is.SameAs(latest.Tree));
    }

    [Test]
    public async Task ConcurrentConsumersOfTheSameVersionParseOnlyOnce()
    {
        var cache = new SyntaxTreeCache();
        var source = new StringTextSnapshot("db.A.find()");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ProbeSnapshot(source, () =>
        {
            started.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
        });
        var second = new ProbeSnapshot(source, onHistory: () => joined.TrySetResult());
        var pending = Task.Run(() => cache.GetOrParse(first));
        Task<SyntaxTreeCacheResult>? follower = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            follower = Task.Run(() => cache.GetOrParse(second));
            await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
        var parsed = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var reused = await follower!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(reused.Tree, Is.SameAs(parsed.Tree));
        Assert.That(reused.IsReused, Is.True);
        Assert.That((first.ReadCount, second.ReadCount), Is.EqualTo((1, 0)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellingOneConsumerDoesNotCancelAnotherConsumerOfTheSameVersion(bool cancelParser)
    {
        var cache = new SyntaxTreeCache();
        var source = new StringTextSnapshot("db.A.find()");
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ProbeSnapshot(source, () =>
        {
            started.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
        });
        var second = new ProbeSnapshot(source, onHistory: () => joined.TrySetResult());
        var pending = Task.Run(() => cache.GetOrParse(first,
            cancellationToken: cancelParser ? cancellation.Token : CancellationToken.None));
        Task<SyntaxTreeCacheResult>? follower = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            follower = Task.Run(() => cache.GetOrParse(second,
                cancellationToken: cancelParser ? CancellationToken.None : cancellation.Token));
            await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            if (!cancelParser)
                Assert.ThrowsAsync<OperationCanceledException>(async () => { await follower.WaitAsync(TimeSpan.FromSeconds(5)); });
        }
        finally { release.Set(); }
        if (cancelParser)
            Assert.ThrowsAsync<OperationCanceledException>(async () => { await pending.WaitAsync(TimeSpan.FromSeconds(5)); });
        var survivor = await (cancelParser ? follower! : pending).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(survivor.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(survivor.IsSuperseded, Is.False);
        Assert.That(cache.GetOrParse(second).Tree, Is.SameAs(survivor.Tree));
        Assert.That((first.ReadCount, second.ReadCount), Is.EqualTo((1, cancelParser ? 1 : 0)));
    }

    [Test]
    public async Task LateParseInAnotherLexerModeIsDiscarded()
    {
        var cache = new SyntaxTreeCache();
        var source = new StringTextSnapshot("/abc/i");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new ProbeSnapshot(source, () =>
        {
            started.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
        });
        var pending = Task.Run(() => cache.GetOrParse(slow));
        SyntaxTreeCacheResult json;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            json = await Task.Run(() => cache.GetOrParse(source, MongoLexerMode.Json)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
        Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuperseded, Is.True);
        Assert.That(cache.GetOrParse(source, MongoLexerMode.Json).Tree, Is.SameAs(json.Tree));
    }

    [Test]
    public async Task RemovingADocumentDiscardsItsPendingParseWithoutBlockingAnotherEditor()
    {
        var cache = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot("db.A");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ProbeSnapshot(snapshot, () =>
        {
            started.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
        });
        var pending = Task.Run(() => cache.GetOrParse(probe));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var other = await Task.Run(() => cache.GetOrParse(new StringTextSnapshot("db.B"))).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(other.IsSuperseded, Is.False);
            Assert.That(cache.RemoveDocument(snapshot.Version.DocumentId), Is.True);
            Assert.That(cache.GetOrParse(snapshot).IsSuperseded, Is.False, "Reabertura cria uma entrada independente.");
        }
        finally { release.Set(); }
        Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuperseded, Is.True);
        Assert.That(cache.GetOrParse(snapshot).IsReused, Is.True);
    }

    [Test]
    public void FailureDoesNotPoisonRetriesOrRestoreAnOlderTree()
    {
        var cache = new SyntaxTreeCache();
        var old = new StringTextSnapshot("db.A");
        cache.GetOrParse(old);
        var next = old.Insert(old.Length, ".find()");
        var attempts = 0;
        var failing = new ProbeSnapshot(next, () =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Falha controlada de leitura.");
        });
        Assert.Throws<InvalidOperationException>(() => cache.GetOrParse(failing));
        Assert.That(cache.GetOrParse(old).IsSuperseded, Is.True);
        Assert.That(cache.GetOrParse(failing).ParseKind, Is.EqualTo(SyntaxTreeParseKind.Incremental));
        Assert.That(cache.GetOrParse(failing).IsReused, Is.True);
        Assert.That(failing.ReadCount, Is.EqualTo(2));
    }

    [Test]
    public void CancellationBeforeAndDuringReadDoesNotPublishOrPoisonTheCache()
    {
        var cache = new SyntaxTreeCache();
        var source = new StringTextSnapshot("");
        using var cancellation = new CancellationTokenSource();
        var probe = new ProbeSnapshot(source, cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => cache.GetOrParse(probe, cancellationToken: cancellation.Token));
        Assert.That(probe.ReadCount, Is.EqualTo(1));
        Assert.Throws<OperationCanceledException>(() => cache.GetOrParse(source, cancellationToken: cancellation.Token));
        var retry = cache.GetOrParse(source);
        Assert.That(retry.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
        Assert.That(retry.Tree!.Root.Span, Is.EqualTo(new TextSpan(0, 0)));
        Assert.Throws<OperationCanceledException>(() => cache.GetOrParse(source, cancellationToken: cancellation.Token));
        Assert.That(cache.GetOrParse(source).Tree, Is.SameAs(retry.Tree));
    }

    [TestCase(25)]
    [TestCase(20260915)]
    [TestCase(7341)]
    public void SeededEditsMatchAnIndependentFullParse(int seed)
    {
        var random = new Random(seed);
        var cache = new SyntaxTreeCache();
        var parser = new TolerantParser();
        var snapshot = new StringTextSnapshot("db.Clientes.find({ Nome: \"João😀\" });\r\ndb.Pedidos.aggregate([{ $match: { ativo: true } }]);");
        string[] inserts = ["x", "\r\n", "😀", "'", "}", "[", "//", "/*", "/abc/i", "`", " "];
        for (var edit = 0; edit < 60; edit++)
        {
            var result = cache.GetOrParse(snapshot);
            Assert.That(result.ParseKind, Is.EqualTo(edit == 0 ? SyntaxTreeParseKind.Full : SyntaxTreeParseKind.Incremental), $"seed={seed}, edit={edit}");
            AssertEquivalent(result.Tree!, parser.Parse(snapshot));
            Assert.That(cache.GetOrParse(snapshot).Tree, Is.SameAs(result.Tree));
            var offset = random.Next(snapshot.Length + 1);
            var removed = random.Next(Math.Min(5, snapshot.Length - offset) + 1);
            snapshot = snapshot.Replace(offset, removed, inserts[random.Next(inserts.Length)]);
        }
    }

    [Test]
    public void IncrementalParseKeepsAnUnaffectedStatementNode()
    {
        var cache = new SyntaxTreeCache();
        var first = new StringTextSnapshot("db.A.find({}); db.B.find({});");
        var original = cache.GetOrParse(first).Tree!;
        var second = first.Replace(3, 1, "C");

        var result = cache.GetOrParse(second);
        Assert.Multiple(() =>
        {
            Assert.That(result.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Incremental));
            Assert.That(result.Tree!.ReusedStatementCount, Is.EqualTo(1));
            Assert.That(result.Tree.Root.Children[1], Is.SameAs(original.Root.Children[1]));
            Assert.That(result.Tree.Root.Children[0].Span, Is.EqualTo(new TextSpan(0, 14)));
            Assert.That(result.Tree.Root.Children[1].Span.Start, Is.EqualTo(15));
        });
    }

    [Test]
    public void AccumulatedChangesFallbackToFullParseUntilPiecewiseMappingExists()
    {
        var cache = new SyntaxTreeCache();
        var parser = new TolerantParser();
        var initial = new StringTextSnapshot("db.A.find({}); db.B.find({});");
        cache.GetOrParse(initial);

        // Do not ask the cache for the intermediate version: the final snapshot carries two changes.
        var final = initial.Remove(0, 1).Insert(14, "x");
        var result = cache.GetOrParse(final);
        var expected = parser.Parse(final);

        Assert.Multiple(() =>
        {
            Assert.That(result.ParseKind, Is.EqualTo(SyntaxTreeParseKind.Full));
            Assert.That(result.Tree!.ReusedStatementCount, Is.Zero);
            AssertEquivalent(result.Tree, expected);
        });
    }

    [Test]
    public void OneMegabyteLongLinePreservesUtf16OffsetsAndRecoveryDiagnostics()
    {
        const string tail = "\r\ndb.A.find({ Nome: \"João😀";
        var snapshot = new StringTextSnapshot("//" + new string('x', 1024 * 1024 - 2 - tail.Length) + tail);
        var cache = new SyntaxTreeCache();
        var result = cache.GetOrParse(snapshot);
        Assert.That(result.Tree!.Root.Span, Is.EqualTo(new TextSpan(0, 1024 * 1024)));
        Assert.That(result.Tree.Tokens[^1].Span.End, Is.EqualTo(snapshot.Length));
        Assert.That(result.Tree.Diagnostics.Any(d => d.Kind == MongoSyntaxDiagnosticKind.Unterminated), Is.True);
        Assert.That(result.Tree.Diagnostics.Any(d => d.Kind == MongoSyntaxDiagnosticKind.MissingClose), Is.True);
        AssertEquivalent(result.Tree, new TolerantParser().Parse(snapshot));
        Assert.That(cache.GetOrParse(snapshot).IsReused, Is.True);
    }

    [Test]
    public void InvalidArgumentsDoNotInvalidateAnExistingTree()
    {
        var cache = new SyntaxTreeCache();
        var snapshot = new StringTextSnapshot("db.A");
        var first = cache.GetOrParse(snapshot);
        Assert.Throws<ArgumentNullException>(() => cache.GetOrParse(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.GetOrParse(snapshot, (MongoLexerMode)255));
        Assert.That(cache.GetOrParse(snapshot).Tree, Is.SameAs(first.Tree));
    }

    private static void AssertEquivalent(MongoSyntaxTree actual, MongoSyntaxTree expected)
    {
        Assert.That(actual.Version, Is.EqualTo(expected.Version));
        Assert.That(actual.Tokens, Is.EqualTo(expected.Tokens));
        Assert.That(actual.Diagnostics, Is.EqualTo(expected.Diagnostics));
        var pending = new Stack<(MongoSyntaxNode Actual, MongoSyntaxNode Expected)>();
        pending.Push((actual.Root, expected.Root));
        while (pending.TryPop(out var pair))
        {
            Assert.That((pair.Actual.Kind, pair.Actual.Span, pair.Actual.Token),
                Is.EqualTo((pair.Expected.Kind, pair.Expected.Span, pair.Expected.Token)));
            Assert.That(pair.Actual.Children.Count, Is.EqualTo(pair.Expected.Children.Count));
            for (var index = 0; index < pair.Actual.Children.Count; index++)
                pending.Push((pair.Actual.Children[index], pair.Expected.Children[index]));
        }
    }

    private sealed class ProbeSnapshot(ITextSnapshot inner, Action? onRead = null, Action? onHistory = null) : ITextSnapshot
    {
        private readonly ITextSnapshot _inner = inner;
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public TextSnapshotVersion Version => _inner.Version;
        public int Length => _inner.Length;
        public char this[int index] => _inner[index];
        public string GetText(int start, int length)
        {
            Interlocked.Increment(ref _readCount);
            onRead?.Invoke();
            return _inner.GetText(start, length);
        }
        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous)
        {
            onHistory?.Invoke();
            return _inner.GetChangesSince(previous is ProbeSnapshot probe ? probe._inner : previous);
        }
    }
}
