namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record AppReleaseCandidate(string Tag, bool IsDraft, bool IsPrerelease, Uri PageUrl, IReadOnlyList<AppReleaseAsset> Assets);
