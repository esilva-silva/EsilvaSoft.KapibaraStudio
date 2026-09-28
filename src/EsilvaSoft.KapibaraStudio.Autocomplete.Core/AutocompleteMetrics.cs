using System.Collections.Frozen;
using System.Diagnostics.Metrics;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Local autocomplete instruments. Tags are restricted to <see cref="AllowedTags"/> and never carry editor text,
/// namespace names, prompts, model paths or document values. Nothing is uploaded.
/// </summary>
public static class AutocompleteMetrics
{
    public const string MeterName = "EsilvaSoft.KapibaraStudio.Autocomplete";

    private static readonly Meter Meter = new(MeterName, "1.0");

    public static IReadOnlySet<string> AllowedTags { get; } = new[]
    {
        "modality", "trigger", "dialect", "completeness", "count_bucket", "kind", "source", "rank_bucket", "reason", "kind_group",
        "candidate_bucket", "scope", "outcome", "result", "provider", "partial", "contract", "tokenizer", "handler", "tier", "pipeline", "role"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static Counter<long> CompletionRequested { get; } = Meter.CreateCounter<long>("completion.requested");
    public static Counter<long> CompletionReturned { get; } = Meter.CreateCounter<long>("completion.returned");
    public static Counter<long> CompletionAccepted { get; } = Meter.CreateCounter<long>("completion.accepted");
    public static Counter<long> CompletionCancelled { get; } = Meter.CreateCounter<long>("completion.cancelled");
    public static Histogram<double> CompletionLatency { get; } = Meter.CreateHistogram<double>("completion.latency", "ms");
    public static Histogram<double> ContextBuildDuration { get; } = Meter.CreateHistogram<double>("context_build.duration", "ms");
    public static Histogram<double> CatalogQueryDuration { get; } = Meter.CreateHistogram<double>("catalog.query.duration", "ms");
    public static Histogram<double> RankingDuration { get; } = Meter.CreateHistogram<double>("ranking.duration", "ms");
    public static Histogram<double> MetadataRefreshDuration { get; } = Meter.CreateHistogram<double>("metadata.refresh.duration", "ms");
    public static Counter<long> MetadataCacheLookup { get; } = Meter.CreateCounter<long>("metadata.cache.lookup");
    public static Counter<long> AiCompletionRequested { get; } = Meter.CreateCounter<long>("ai_completion.requested");
    public static Counter<long> AiCompletionGenerated { get; } = Meter.CreateCounter<long>("ai_completion.generated");
    public static Counter<long> AiCompletionAccepted { get; } = Meter.CreateCounter<long>("ai_completion.accepted");
    public static Counter<long> AiCompletionCancelled { get; } = Meter.CreateCounter<long>("ai_completion.cancelled");
    public static Histogram<double> AiCompletionLatency { get; } = Meter.CreateHistogram<double>("ai_completion.latency", "ms");
    public static Histogram<double> AiContextBuildDuration { get; } = Meter.CreateHistogram<double>("ai_context_build.duration", "ms");
    public static Histogram<double> TokenizationDuration { get; } = Meter.CreateHistogram<double>("tokenization.duration", "ms");
    public static Histogram<double> InferenceDuration { get; } = Meter.CreateHistogram<double>("inference.duration", "ms");
    public static Histogram<double> InferenceTimeToFirstToken { get; } = Meter.CreateHistogram<double>("inference.ttft", "ms");
    public static Histogram<double> InferenceTokensPerSecond { get; } = Meter.CreateHistogram<double>("inference.tokens_per_second");
    public static Histogram<long> InferencePromptTokens { get; } = Meter.CreateHistogram<long>("inference.prompt_tokens");
    public static Histogram<long> InferenceGeneratedTokens { get; } = Meter.CreateHistogram<long>("inference.generated_tokens");
    public static Histogram<long> PrefixCacheReusedTokens { get; } = Meter.CreateHistogram<long>("prefix_cache.reused_tokens");
    /// <summary>Time spent by autocomplete handlers on the UI thread for one editor event.</summary>
    public static Histogram<double> UiDispatcherTime { get; } = Meter.CreateHistogram<double>("ui.autocomplete.dispatcher_time", "ms");
}
