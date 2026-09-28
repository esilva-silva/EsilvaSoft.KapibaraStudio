namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>The package of a newer release that matches the running platform.</summary>
public sealed record AppUpdateRelease(AppVersion Version, string Tag, string AssetName, Uri DownloadUrl, long Size, string? Sha256, Uri PageUrl, Uri? ChecksumsUrl);
