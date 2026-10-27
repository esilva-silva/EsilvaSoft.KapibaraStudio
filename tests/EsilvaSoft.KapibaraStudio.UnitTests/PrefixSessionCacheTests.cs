using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class PrefixSessionCacheTests
{
    [Test]
    public void SessionIdentityAndCancellationAreNeverSerializedWithEditorRequests()
    {
        using var lifetime = new CancellationTokenSource();
        var request = new EsilvaSoft.KapibaraStudio.Autocomplete.Core.AutocompleteRequest("db.", "")
            { PrefixCacheSessionKey = "transient-session", PrefixCacheSessionLifetime = lifetime.Token };
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        Assert.That(json, Does.Not.Contain("transient-session").And.Not.Contain("Lifetime"));
    }

    private sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    [Test]
    public void ReuseStopsBeforeTheLastTokenAndAtChangedIdsOrCap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrefixSessionCache<Resource>.ReusableTokens([1, 2, 3], [1, 2, 3], null), Is.EqualTo(2));
            Assert.That(PrefixSessionCache<Resource>.ReusableTokens([1, 2, 3], [1, 4, 3], null), Is.EqualTo(1));
            Assert.That(PrefixSessionCache<Resource>.ReusableTokens([1, 2, 3], [1, 2, 3], 1), Is.EqualTo(1));
            Assert.That(PrefixSessionCache<Resource>.ReusableTokens([1], [2], null), Is.Zero);
            Assert.That(PrefixSessionCache<Resource>.ReusableTokens([], [], null), Is.Zero);
        });
    }

    [Test]
    public void LruEvictsTheIdleLeastRecentlyUsedSession()
    {
        using var cache = new PrefixSessionCache<Resource>();
        var first = cache.Acquire("a", 2, 64, () => new Resource()); first.Dispose();
        var second = cache.Acquire("b", 2, 64, () => new Resource()); second.Dispose();
        using (var hit = cache.Acquire("a", 2, 64, () => throw new AssertionException("Cache miss")))
            Assert.That(hit.Generator, Is.SameAs(first.Generator));
        using var third = cache.Acquire("c", 2, 64, () => new Resource());
        Assert.That(second.Generator.Disposals, Is.EqualTo(1));
        Assert.That(first.Generator.Disposals, Is.Zero);
    }

    [Test]
    public void SessionCloseWhileActiveDefersDisposalUntilTheWorkerReturns()
    {
        using var lifetime = new CancellationTokenSource();
        using var cache = new PrefixSessionCache<Resource>();
        var lease = cache.Acquire("a", 4, 64, () => new Resource(), lifetime.Token);
        lifetime.Cancel();
        Assert.That(lease.Generator.Disposals, Is.Zero);
        lease.Dispose();
        Assert.That(lease.Generator.Disposals, Is.EqualTo(1));
        Assert.Throws<OperationCanceledException>(() => cache.Acquire("a", 4, 64, () => new Resource(), lifetime.Token));
    }

    [Test]
    public void FailureInvalidatesOnlyTheOriginatingSessionAndAllowsRecovery()
    {
        using var cache = new PrefixSessionCache<Resource>();
        var other = cache.Acquire("other", 4, 64, () => new Resource()); other.Dispose();
        using (var failed = cache.Acquire("failed", 4, 64, () => new Resource())) cache.Invalidate(failed);
        using var next = cache.Acquire("failed", 4, 64, () => new Resource());
        using var retained = cache.Acquire("other", 4, 64, () => throw new AssertionException("Outra aba invalidada"));
        Assert.That(retained.Generator, Is.SameAs(other.Generator));
    }

    [Test]
    public void ContextResizeAndModelReleaseDisposeGeneratorsExactlyOnce()
    {
        var cache = new PrefixSessionCache<Resource>();
        var first = cache.Acquire("a", 4, 64, () => new Resource()); first.Dispose();
        var resized = cache.Acquire("a", 4, 128, () => new Resource()); resized.Dispose();
        cache.Dispose(); cache.Dispose();
        Assert.That(first.Generator.Disposals, Is.EqualTo(1));
        Assert.That(resized.Generator.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void ConcurrentRequestsCannotUseTheSameNativeGenerator()
    {
        using var cache = new PrefixSessionCache<Resource>();
        using var active = cache.Acquire("a", 4, 64, () => new Resource());
        Assert.Throws<InvalidOperationException>(() => cache.Acquire("a", 4, 64, () => new Resource()));
    }

    [Test]
    public void AbandoningBufferedOutputInvalidatesAnAlreadyReturnedSession()
    {
        using var cache = new PrefixSessionCache<Resource>();
        var completed = cache.Acquire("tab", 4, 64, () => new Resource());
        completed.Tokens = [1, 2, 3];
        completed.Dispose();
        cache.InvalidateSession("tab", "abandoned");
        Assert.That(completed.Generator.Disposals, Is.EqualTo(1));
        using var next = cache.Acquire("tab", 4, 64, () => new Resource());
        Assert.That(next.Tokens, Is.Empty);
    }

    [Test]
    public void EosWithoutSequenceGrowthNeverRepeatsThePreviousToken()
    {
        Assert.That(GeneratedTokenReader.TryRead([10, 20], 2, out _), Is.False);
        Assert.That(GeneratedTokenReader.TryRead([], 0, out _), Is.False);
        Assert.That(GeneratedTokenReader.TryRead([10, 20, 30], 2, out var token), Is.True);
        Assert.That(token, Is.EqualTo(30), "O último token válido é preservado mesmo no passo que conclui a geração.");
        Assert.Throws<InvalidOperationException>(() => GeneratedTokenReader.TryRead([10, 20, 30], 1, out _));
    }
}
