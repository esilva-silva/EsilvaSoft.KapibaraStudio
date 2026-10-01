using System.Diagnostics;
using NUnit.Framework;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Completion;

/// <summary>Medição manual; não participa de CI ou release.</summary>
[TestFixture]
public sealed class TraditionalPreemptiveLatencyTests
{
    private static CompletionContext Context(string prefix, int caret = 10) =>
        new(new(1, 1), EditorDialects.Console, SymbolKinds.Field, prefix, new(caret - prefix.Length, prefix.Length));

    /// <summary>
    /// Mede a computação determinística com o catálogo real de linguagem. O limite afirmado aqui é largo de propósito
    /// (máquina de teste compartilhada); o número medido é publicado no console para comparação com a meta de 5 ms.
    /// </summary>
    [Test]
    public async Task ComputationLatencyIsMeasuredAndReported()
    {
        var provider = new TraditionalPreemptiveCompletionProvider(
            new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
        var context = new CompletionContext(new(1, 1), EditorDialects.Console, SymbolKinds.DslRoot, "Conn", new(0, 4));
        for (var warmup = 0; warmup < 50; warmup++) await provider.CompleteAsync(new(context, warmup + 1, 0));

        var samples = new double[500];
        for (var index = 0; index < samples.Length; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await provider.CompleteAsync(new(context, index + 1, 0));
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(samples);
        var p95 = samples[(int)(samples.Length * .95)];
        TestContext.Out.WriteLine($"computação inline determinística (catálogo de linguagem): p50={samples[samples.Length / 2]:F3} ms, p95={p95:F3} ms, máx={samples[^1]:F3} ms");

        // Mesma medição com o teto de candidatos que o modo automático admite (200 campos de schema), que é o pior
        // caso previsto para o ranqueamento por tecla.
        var fields = Enumerable.Range(0, 200)
            .Select(index => new CatalogSymbol($"field/{index}", SymbolKind.Field, $"Campo{index:D4}", "string")).ToArray();
        var heavy = new TraditionalPreemptiveCompletionProvider(new CompletionService(new StaticCatalog(CatalogCompleteness.Complete, fields)));
        var heavyContext = Context("Campo0001", caret: 9);
        for (var warmup = 0; warmup < 50; warmup++) await heavy.CompleteAsync(new(heavyContext, warmup + 1, 0));
        var heavySamples = new double[500];
        for (var index = 0; index < heavySamples.Length; index++)
        {
            var started = Stopwatch.GetTimestamp();
            await heavy.CompleteAsync(new(heavyContext, index + 1, 0));
            heavySamples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(heavySamples);
        TestContext.Out.WriteLine($"computação inline determinística (200 campos): p50={heavySamples[heavySamples.Length / 2]:F3} ms, "
            + $"p95={heavySamples[(int)(heavySamples.Length * .95)]:F3} ms, máx={heavySamples[^1]:F3} ms");

        Assert.That(p95, Is.LessThan(50), "A computação automática não pode chegar perto de travar a digitação.");
        Assert.That(heavySamples[(int)(heavySamples.Length * .95)], Is.LessThan(50));
    }
    private sealed class StaticCatalog(CatalogCompleteness completeness, params CatalogSymbol[] symbols) : IKnowledgeCatalog
    {
        public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default) =>
            new(symbols.Select(symbol => new CatalogCandidate(symbol, CatalogMatch.Prefix)).ToArray(), completeness);
    }
}
