namespace EsilvaSoft.KapibaraStudio.Core;

public static class AppUpdateSelector
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    /// <summary>Mirrors the package names produced by release.yml and build-release.ps1.</summary>
    public static string GetAssetName(AppVersion version, string rid)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(rid);
        return $"EsilvaSoft.KapibaraStudio-{version}-{rid}{(rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz")}";
    }

    /// <summary>Migration naming first, followed by packages from existing installations.</summary>
    public static IReadOnlyList<string> GetAssetNames(AppVersion version, string rid)
    {
        var current = GetAssetName(version, rid);
        var legacy = current.Replace("EsilvaSoft.KapibaraStudio-", "EsilvaSoft.SlopStudio-", StringComparison.Ordinal);
        return [current, legacy];
    }

    /// <summary>
    /// Newest non-draft release above <paramref name="current"/> that publishes a package for <paramref name="rid"/>.
    /// Pre-releases are offered only to an installation that already runs a pre-release.
    /// </summary>
    public static AppUpdateRelease? SelectNewest(AppVersion current, IEnumerable<AppReleaseCandidate> releases, string rid)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(releases);
        AppUpdateRelease? newest = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || !AppVersion.TryParse(release.Tag, out var version)) continue;
            if ((release.IsPrerelease || version.IsPrerelease) && !current.IsPrerelease) continue;
            if (version <= current || newest is not null && version <= newest.Version) continue;
            var package = GetAssetNames(version, rid)
                .Select(name => release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)))
                .FirstOrDefault(asset => asset is not null);
            if (package is null) continue;
            var checksums = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, ChecksumsAssetName, StringComparison.Ordinal));
            newest = new(version, release.Tag, package.Name, package.DownloadUrl, package.Size, NormalizeSha256(package.Digest), release.PageUrl, checksums?.DownloadUrl);
        }
        return newest;
    }

    /// <summary>Reads GitHub's <c>sha256:hex</c> asset digest; anything else is treated as absent.</summary>
    public static string? NormalizeSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..];
        return hex.Length == 64 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>Finds a package in a <c>sha256sum</c> listing ("hash  name" or "hash *name").</summary>
    public static string? FindChecksum(string listing, string assetName)
    {
        ArgumentNullException.ThrowIfNull(listing);
        foreach (var line in listing.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*'), assetName, StringComparison.Ordinal))
                return NormalizeSha256("sha256:" + parts[0]);
        }
        return null;
    }
}
