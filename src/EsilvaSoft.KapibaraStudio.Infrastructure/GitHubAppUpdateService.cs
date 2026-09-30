using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Checks GitHub Releases, stages a SHA-256 verified package for <see cref="AppUpdateInstaller"/> to swap in.
/// </summary>
public sealed class GitHubAppUpdateService : IAppUpdateService, IDisposable
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly AppUpdateOptions _options;
    private readonly HttpClient _http;
    private readonly IAppUpdateStorage _storage;

    public GitHubAppUpdateService(AppUpdateOptions options, IAppUpdateStorage storage, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(storage);
        _options = options;
        _storage = storage;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // Per-request limits: a short timeout for the feed, a stall timeout for the package.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EsilvaSoft.KapibaraStudio", options.CurrentVersion.ToString()));
    }

    public AppUpdateAvailability Availability => _options.Availability;
    public AppVersion CurrentVersion => _options.CurrentVersion;

    public StagedAppUpdate? GetStagedUpdate() => Availability == AppUpdateAvailability.Disabled ? null : _storage.GetStagedUpdate(_options);

    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        if (Availability == AppUpdateAvailability.Disabled) return null;
        var primary = await CheckFeedAsync(_options.ReleasesApi, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (primary is not null) return primary;
        return _options.FallbackReleasesApi is { } fallback && fallback != _options.ReleasesApi
            ? await CheckFeedAsync(fallback, cancellationToken)
            : null;
    }

    private async Task<AppUpdateRelease?> CheckFeedAsync(Uri api, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Missing repositories, rate limits and outages allow the legacy feed to be tried.
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, ApiJson, timeout.Token) ?? [];
            return AppUpdateSelector.SelectNewest(CurrentVersion, releases.Select(ToCandidate).OfType<AppReleaseCandidate>(), _options.Rid);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException) { return null; }
    }

    public async Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(operation);
        if (Availability != AppUpdateAvailability.Supported) throw new InvalidOperationException("A atualização automática não está disponível nesta instalação.");
        var token = operation.Token;
        using var staging = _storage.BeginStaging(_options, release, token);
        var expected = release.Sha256 ?? await DownloadChecksumAsync(release, token)
            ?? throw new InvalidDataException("O release não publica SHA-256 para este pacote; a atualização não foi baixada.");
        var actual = await DownloadPackageAsync(release, staging, operation);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O pacote baixado não confere com o SHA-256 publicado e foi descartado.");
        staging.Commit(token);
        return new StagedAppUpdate(release.Version, null);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Entry point hook after the UI lifetime ended. Exiting must never fail because of an update.</summary>
    public static void ApplyPendingOnExit(bool restart) =>
        LocalAppUpdateStorage.ApplyPendingOnExit(AppUpdateOptions.FromProcess(new LocalWorkspacePathResolver()), restart);

    /// <summary>Removes leftovers of a previous swap and staging folders no longer referenced.</summary>
    public static void CleanupAfterStart() => LocalAppUpdateStorage.CleanupAfterStart(AppUpdateOptions.FromProcess(new LocalWorkspacePathResolver()));
    private async Task<string?> DownloadChecksumAsync(AppUpdateRelease release, CancellationToken token) =>
        release.ChecksumsUrl is null ? null : AppUpdateSelector.FindChecksum(await _http.GetStringAsync(release.ChecksumsUrl, token), release.AssetName);

    private async Task<string> DownloadPackageAsync(AppUpdateRelease release, IAppUpdateStaging staging, ApplicationOperationScope operation)
    {
        var token = operation.Token;
        using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.Size;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = staging.CreatePackageStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        var buffer = new byte[81920];
        long completed = 0;
        try
        {
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                var read = await source.ReadAsync(buffer, stall.Token);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                completed += read;
                operation.Report(completed, total);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("O download da atualização parou de responder.");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static AppReleaseCandidate? ToCandidate(GitHubRelease release)
    {
        if (release.TagName is null || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page)) return null;
        var assets = new List<AppReleaseAsset>();
        foreach (var asset in release.Assets ?? [])
            if (asset.Name is not null && Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var download))
                assets.Add(new AppReleaseAsset(asset.Name, download, asset.Size, asset.Digest));
        return new AppReleaseCandidate(release.TagName, release.Draft, release.Prerelease, page, assets);
    }

    private sealed record GitHubRelease(string? TagName, bool Draft, bool Prerelease, string? HtmlUrl, GitHubAsset[]? Assets);
    private sealed record GitHubAsset(string? Name, long Size, string? BrowserDownloadUrl, string? Digest);
}
