namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Ordered providers to try. Fallback to the next candidate is allowed only in Automatic mode.</summary>
public sealed record AiExecutionPlan(IReadOnlyList<AiProviderCandidate> Candidates, bool AllowFallback);
