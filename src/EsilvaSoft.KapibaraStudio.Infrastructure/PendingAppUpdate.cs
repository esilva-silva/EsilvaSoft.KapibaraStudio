namespace EsilvaSoft.KapibaraStudio.Infrastructure;

internal sealed record PendingAppUpdate(string Version, string PayloadDirectory, string TargetDirectory, string ExecutableName, string? LastError = null);
