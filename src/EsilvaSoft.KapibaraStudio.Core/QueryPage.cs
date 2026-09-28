namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record QueryPage(
    IReadOnlyList<string> Documents,
    TimeSpan Duration,
    bool IsTruncated);
