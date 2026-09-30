namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

internal sealed record PendingAppUpdate(string Version, string PayloadDirectory, string TargetDirectory, string ExecutableName, string? LastError = null);
