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

    // Prompt de autocomplete em ids: envelope estável (cabeçalho + nomes) seguido do código antes do cursor e do fim FIM.
    private static int[] Prompt(int header, params int[] tail) => [.. Enumerable.Range(1000, header), .. tail];

    /// <summary>
    /// Digitação: só o fim do prompt muda (código + sufixo + marcador). O envelope inteiro é reaproveitado e apenas
    /// a parte diferente é reprocessada, nunca menos que o bloco mínimo.
    /// </summary>
    [Test]
    public void WhenOnlyTheEndChangesTheWholeStablePrefixIsReused()
    {
        int[] suffix = [.. Enumerable.Repeat(9, 40)];
        var previous = Prompt(200, [1, 2, 3, .. suffix]);
        var typed = Prompt(200, [1, 2, 3, 4, .. suffix]);
        var typedNearTheEnd = Prompt(200, [1, 2, 3, 4, 9, 9]);

        Assert.Multiple(() =>
        {
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(previous, typed, cap: null), Is.EqualTo(203),
                "Envelope de 200 ids mais o código comum até o ponto da edição.");
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(Prompt(200, 1, 2, 3, 9, 9), typedNearTheEnd, cap: null),
                Is.EqualTo(typedNearTheEnd.Length - PrefixSessionCache<Resource>.MinimumRecomputedTokens),
                "Com fim curto, o bloco mínimo é que limita: os últimos 32 ids sempre são reprocessados.");
        });
    }

    /// <summary>
    /// Mudança no começo (cabeçalho, nomes conhecidos, linguagem): o maior prefixo comum para no primeiro id
    /// diferente, e com o primeiro id trocado nada é reaproveitado — prefill completo.
    /// </summary>
    [Test]
    public void AChangeAtTheBeginningInvalidatesTheReuse()
    {
        var previous = Prompt(200, 1, 2, 3);
        var otherHeader = (int[])previous.Clone();
        otherHeader[0] = 7;
        var otherNames = (int[])previous.Clone();
        otherNames[60] = 7;

        Assert.Multiple(() =>
        {
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(previous, otherHeader, cap: null), Is.Zero);
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(previous, otherNames, cap: null), Is.EqualTo(60));
        });
    }

    /// <summary>Prompt repetido, prompt curto e teto do chamador.</summary>
    [Test]
    public void TheMinimumRecomputedBlockAndTheCallerCapBoundTheReuse()
    {
        var prompt = Prompt(200, 1, 2, 3);
        Assert.Multiple(() =>
        {
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(prompt, prompt, cap: null),
                Is.EqualTo(prompt.Length - PrefixSessionCache<Resource>.MinimumRecomputedTokens));
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(Prompt(20), Prompt(20), cap: null), Is.Zero,
                "Prompt mais curto que o bloco mínimo vai pelo caminho completo.");
            Assert.That(PrefixSessionCache<Resource>.ReusableTokensForPrompt(prompt, prompt, cap: 50), Is.EqualTo(50));
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
