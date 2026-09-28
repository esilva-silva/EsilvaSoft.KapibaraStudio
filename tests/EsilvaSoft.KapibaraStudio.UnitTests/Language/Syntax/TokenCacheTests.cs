using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

/// <summary>
/// Contrato do cache de tokens: reaproveita a versão corrente sem reler o documento, nunca mistura abas nem
/// linhagens, lexifica a versão superada só para o consumidor atrasado e libera a aba fechada.
/// </summary>
[TestFixture]
public sealed class TokenCacheTests
{
    [Test]
    public void SameVersionAndEquivalentWrapperReuseTheTokensWithoutReadingText()
    {
        var cache = new TokenCache();
        var source = new StringTextSnapshot("db.Clientes.find({ ativo: true })");
        var first = new ProbeSnapshot(source);
        var wrapper = new ProbeSnapshot(source);

        var lexed = cache.GetOrLex(first);
        var reused = cache.GetOrLex(wrapper);

        Assert.That(lexed.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(lexed.Version, Is.EqualTo(source.Version));
        Assert.That(lexed.Text, Is.EqualTo(source.Text));
        Assert.That(reused.LexKind, Is.EqualTo(TokenCacheLexKind.Reused));
        Assert.That(reused.Tokens, Is.SameAs(lexed.Tokens));
        Assert.That(reused.Text, Is.SameAs(lexed.Text));
        Assert.That((first.ReadCount, wrapper.ReadCount), Is.EqualTo((1, 0)));
    }

    [Test]
    public void EachKeystrokeLexesOnceAndTheTokensDescribeTheNewText()
    {
        var cache = new TokenCache();
        var snapshot = new StringTextSnapshot("db.A.find({");
        var kinds = new List<TokenCacheLexKind>();

        for (var index = 0; index < 4; index++)
        {
            snapshot = snapshot.Insert(snapshot.Length, "b");
            kinds.Add(cache.GetOrLex(snapshot).LexKind);
            kinds.Add(cache.GetOrLex(snapshot).LexKind);
        }

        Assert.That(kinds, Is.EqualTo(new[]
        {
            TokenCacheLexKind.Lexed, TokenCacheLexKind.Reused, TokenCacheLexKind.Lexed, TokenCacheLexKind.Reused,
            TokenCacheLexKind.Lexed, TokenCacheLexKind.Reused, TokenCacheLexKind.Lexed, TokenCacheLexKind.Reused
        }));
        var current = cache.GetOrLex(snapshot);
        Assert.That(current.Text, Is.EqualTo(snapshot.Text));
        Assert.That(current.Tokens[^1].End, Is.EqualTo(snapshot.Length));
    }

    [Test]
    public void ASupersededVersionIsLexedForItsConsumerWithoutReplacingTheCurrentOne()
    {
        var cache = new TokenCache();
        var older = new StringTextSnapshot("db.Pedidos.find({");
        var newer = older.Replace(3, 7, "Clientes");
        var current = cache.GetOrLex(newer);

        var late = cache.GetOrLex(older);

        Assert.That(late.LexKind, Is.EqualTo(TokenCacheLexKind.Superseded));
        Assert.That(late.Text, Is.EqualTo(older.Text), "O resultado atrasado descreve a versão pedida, não a corrente.");
        Assert.That(late.Version, Is.EqualTo(older.Version));
        Assert.That(cache.GetOrLex(newer).Tokens, Is.SameAs(current.Tokens));
    }

    [Test]
    public void DifferentDocumentsWithTheSameVersionNumbersNeverShareTokens()
    {
        var cache = new TokenCache();
        var a = new StringTextSnapshot("db.A.find({");
        var b = new StringTextSnapshot("db.BBBB.find({");
        Assert.That(a.Version.Sequence, Is.EqualTo(b.Version.Sequence));

        var tokensA = cache.GetOrLex(a);
        var tokensB = cache.GetOrLex(b);

        Assert.That(tokensB.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(cache.GetOrLex(a).Text, Is.EqualTo(a.Text));
        Assert.That(cache.GetOrLex(b).Text, Is.EqualTo(b.Text));
        Assert.That(tokensA.Tokens, Is.Not.SameAs(tokensB.Tokens));
        // Digitar em uma aba não invalida a outra.
        cache.GetOrLex(a.Insert(a.Length, "x"));
        Assert.That(cache.GetOrLex(b).Tokens, Is.SameAs(tokensB.Tokens));
    }

    [Test]
    public void AnUnknownLineageIsNotReusedEvenWithTheSameVersionNumber()
    {
        var cache = new TokenCache();
        var source = new StringTextSnapshot("db.A.find({");
        var branch = new StringTextSnapshot("db.Z.find({", source.Version);

        var first = cache.GetOrLex(source);
        var second = cache.GetOrLex(branch);

        Assert.That(second.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(second.Text, Is.EqualTo(branch.Text));
        Assert.That(second.Tokens, Is.Not.SameAs(first.Tokens));
    }

    [Test]
    public void ChangingTheModeRelexesAndKeepsOnlyTheRequestedMode()
    {
        var cache = new TokenCache();
        var snapshot = new StringTextSnapshot("""{ "a": 1 }""");

        var script = cache.GetOrLex(snapshot);
        var json = cache.GetOrLex(snapshot, MongoLexerMode.Json);

        Assert.That((script.Mode, json.Mode), Is.EqualTo((MongoLexerMode.Script, MongoLexerMode.Json)));
        Assert.That(json.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(cache.GetOrLex(snapshot, MongoLexerMode.Json).LexKind, Is.EqualTo(TokenCacheLexKind.Reused));
        Assert.That(cache.GetOrLex(snapshot).LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
    }

    [Test]
    public void RemovingTheDocumentReleasesTextAndTokensAndTheNextRequestStartsCold()
    {
        var cache = new TokenCache();
        var source = new StringTextSnapshot("db.Clientes.find({");
        var probe = new ProbeSnapshot(source);
        _ = cache.GetOrLex(probe);

        Assert.That(cache.RemoveDocument(source.Version.DocumentId), Is.True);
        Assert.That(cache.RemoveDocument(source.Version.DocumentId), Is.False);
        var cold = cache.GetOrLex(probe);

        Assert.That(cold.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(probe.ReadCount, Is.EqualTo(2));
    }

    [Test]
    public void InvalidArgumentsAreRejectedWithoutDisturbingTheCache()
    {
        var cache = new TokenCache();
        var snapshot = new StringTextSnapshot("db.A.find({");
        var first = cache.GetOrLex(snapshot);

        Assert.Throws<ArgumentNullException>(() => cache.GetOrLex(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.GetOrLex(snapshot, (MongoLexerMode)255));
        Assert.That(cache.GetOrLex(snapshot).Tokens, Is.SameAs(first.Tokens));
    }

    [Test]
    public void CancellationLeavesNothingHalfPublished()
    {
        var cache = new TokenCache();
        var snapshot = new StringTextSnapshot("db.A.find({");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => cache.GetOrLex(snapshot, cancellationToken: cancellation.Token));
        var retry = cache.GetOrLex(snapshot);

        Assert.That(retry.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        Assert.That(cache.GetOrLex(snapshot).Tokens, Is.SameAs(retry.Tokens));
    }

    [Test]
    public async Task ANewVersionDoesNotWaitForTheLexingOfAnOlderOne()
    {
        var cache = new TokenCache();
        var source = new StringTextSnapshot("db.A.find({");
        using var started = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        var slow = new ProbeSnapshot(source, onRead: () =>
        {
            started.Release();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        var pending = Task.Run(() => cache.GetOrLex(slow));
        DocumentTokens newest;
        try
        {
            await started.WaitAsync(TimeSpan.FromSeconds(5));
            newest = await Task.Run(() => cache.GetOrLex(source.Insert(source.Length, "b"))).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Release();
        }

        var late = await pending;
        Assert.That(newest.LexKind, Is.EqualTo(TokenCacheLexKind.Lexed));
        // A lexificação antiga termina, devolve o resultado correto da sua versão e não substitui a nova.
        Assert.That(late.Version, Is.EqualTo(source.Version));
        Assert.That(late.LexKind, Is.EqualTo(TokenCacheLexKind.Superseded));
        Assert.That(cache.GetOrLex(source).LexKind, Is.EqualTo(TokenCacheLexKind.Superseded));
    }

    private sealed class ProbeSnapshot(ITextSnapshot inner, Action? onRead = null) : ITextSnapshot
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
        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous) =>
            _inner.GetChangesSince(previous is ProbeSnapshot probe ? probe._inner : previous);
    }
}
