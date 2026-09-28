namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record CollectionCountResult(long Count, bool IsEstimated, TimeSpan Duration);
