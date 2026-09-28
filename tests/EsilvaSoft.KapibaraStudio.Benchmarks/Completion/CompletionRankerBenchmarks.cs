using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Completion;

/// <summary>Measures deterministic top-100 completion ranking over synthetic catalog candidates only.</summary>
[MemoryDiagnoser]
public class CompletionRankerBenchmarks
{
    private readonly CompletionRanker _ranker = new();
    private IReadOnlyList<CompletionItem> _candidates = [];

    [Params(20, 200, 2_000)]
    public int CandidateCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var candidates = new CompletionItem[CandidateCount];
        for (var index = 0; index < candidates.Length; index++)
        {
            var label = (index & 3) switch
            {
                0 => $"customerAddress{index:D4}",
                1 => $"CustomerAccount{index:D4}",
                2 => $"accountCustomer{index:D4}",
                _ => $"catalogField{index:D4}"
            };
            candidates[index] = new CompletionItem(
                $"synthetic-field-{index:D4}",
                label,
                "campo sintético",
                CompletionItemKind.Field,
                new CompletionEdit(new TextSpan(0, 0), new TextSpan(0, 0), label),
                label,
                0,
                CompletionSource.Catalog);
        }

        _candidates = candidates;
    }

    [Benchmark(Description = "CompletionRanker.Rank (top 100)")]
    public int RankTop100() => _ranker.Rank(_candidates, "CA", maximum: 100).Count;
}
