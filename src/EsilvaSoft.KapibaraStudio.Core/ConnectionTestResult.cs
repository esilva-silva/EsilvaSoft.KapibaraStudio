namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConnectionTestResult(
    bool IsSuccess,
    string Message,
    TimeSpan Duration,
    string? ServerVersion = null);
