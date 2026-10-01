using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AppUpdateServiceTests
{
    private const string WindowsExecutable = "EsilvaSoft.SlopStudio.Desktop.exe";
    private const string LinuxExecutable = "EsilvaSoft.SlopStudio.Desktop";

    [Test]
    public void ProcessDefaultsUseOnlyKapibaraRepository()
    {
        var options = AppUpdateOptions.FromProcess(new LocalWorkspacePathResolver());
        Assert.That(options.ReleasesApi.AbsoluteUri, Is.EqualTo("https://api.github.com/repos/esilva-silva/EsilvaSoft.KapibaraStudio/releases?per_page=20"));
    }

    [Test]
    public void CleanupRemovesTemporaryExecutablesOfBothBrandsButPreservesInstalledFiles()
    {
        using var fixture = new UpdateFixture();
        var names = new[] { WindowsExecutable, WindowsExecutable.Replace("SlopStudio", "KapibaraStudio", StringComparison.Ordinal) };
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(fixture.Target, name), "installed");
            File.WriteAllText(Path.Combine(fixture.Target, name + ".old"), "old");
            File.WriteAllText(Path.Combine(fixture.Target, name + ".new"), "partial");
        }
        AppUpdateInstaller.Cleanup(fixture.Options());
        Assert.That(Directory.GetFiles(fixture.Target).Select(Path.GetFileName), Is.EquivalentTo(names));
    }

    [TestCase("missing")]
    [TestCase("rate-limit")]
    [TestCase("invalid-json")]
    [TestCase("timeout")]
    [TestCase("network")]
    [TestCase("current")]
    [TestCase("incompatible")]
    public async Task UnusableFeedNeverRequestsAnotherRepository(string failure)
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")));
        if (failure is "current" or "incompatible")
            fixture.PublishRelease(failure == "current" ? "v0.5.0" : "v0.7.0", failure == "incompatible" ? "linux-x64" : "win-x64", [], api: UpdateFixture.PrimaryApi);
        else if (failure != "missing")
            fixture.Routes[UpdateFixture.PrimaryApi.AbsoluteUri] = failure switch
            {
                "rate-limit" => () => new HttpResponseMessage(HttpStatusCode.Forbidden),
                "invalid-json" => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{invalid") },
                "timeout" => () => throw new TaskCanceledException("Simulated HTTP timeout"),
                _ => () => throw new HttpRequestException("Simulated network failure")
            };
        using var service = fixture.Service(fixture.Options() with { ReleasesApi = UpdateFixture.PrimaryApi });
        Assert.That(await service.CheckAsync(CancellationToken.None), Is.Null);
        Assert.That(fixture.Requests, Is.EqualTo(new[] { UpdateFixture.PrimaryApi.AbsoluteUri }));
    }

    [Test]
    public async Task EligibleReleaseComesOnlyFromConfiguredRepository()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.8.0", "win-x64", Zip((WindowsExecutable, "legacy")));
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "primary")), api: UpdateFixture.PrimaryApi, kapibara: true);
        using var service = fixture.Service(fixture.Options() with { ReleasesApi = UpdateFixture.PrimaryApi });
        Assert.That((await service.CheckAsync(CancellationToken.None))?.AssetName, Is.EqualTo("EsilvaSoft.KapibaraStudio-0.6.0-win-x64.zip"));
        Assert.That(fixture.Requests, Is.EqualTo(new[] { UpdateFixture.PrimaryApi.AbsoluteUri }));
    }

    [Test]
    public void CallerCancellationStopsFeedRequest()
    {
        using var fixture = new UpdateFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Routes[UpdateFixture.PrimaryApi.AbsoluteUri] = () =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        using var service = fixture.Service(fixture.Options() with { ReleasesApi = UpdateFixture.PrimaryApi });
        Assert.CatchAsync<OperationCanceledException>(() => service.CheckAsync(cancellation.Token));
        Assert.That(fixture.Requests, Is.EqualTo(new[] { UpdateFixture.PrimaryApi.AbsoluteUri }));
    }

    [TestCase("win-x64", false)]
    [TestCase("win-x64", true)]
    [TestCase("linux-x64", false)]
    [TestCase("linux-x64", true)]
    public async Task CrossBrandPackagePreservesInstalledLaunchNameAndIncomingExecutable(string rid, bool installedKapibara)
    {
        using var fixture = new UpdateFixture();
        var legacy = rid.StartsWith("win-", StringComparison.Ordinal) ? WindowsExecutable : LinuxExecutable;
        var modern = legacy.Replace("SlopStudio", "KapibaraStudio", StringComparison.Ordinal);
        var installed = installedKapibara ? modern : legacy;
        var incoming = installedKapibara ? legacy : modern;
        File.WriteAllText(Path.Combine(fixture.Target, installed), "old");
        var package = rid.StartsWith("win-", StringComparison.Ordinal) ? Zip((incoming, "new")) : TarGz(incoming, "new");
        fixture.PublishRelease("v0.6.0", rid, package, kapibara: !installedKapibara);
        var options = fixture.Options(rid) with { ExecutableName = installed };
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);
        Assert.That(File.ReadAllText(Path.Combine(fixture.Target, installed)), Is.EqualTo("old"));
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(fixture.Target, installed)), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Target, incoming)), Is.EqualTo("new"));
        if (!OperatingSystem.IsWindows() && rid.StartsWith("linux-", StringComparison.Ordinal))
            Assert.That(File.GetUnixFileMode(Path.Combine(fixture.Target, installed)).HasFlag(UnixFileMode.UserExecute), Is.True);
    }

    [Test]
    public async Task UnrecognizedExecutableIsRejectedWithoutStaging()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip(("other.exe", "new")), kapibara: true);
        using var service = fixture.Service(fixture.Options());
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using var operation = new ApplicationOperationService().Begin("Baixando");
        Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release, operation));
        Assert.That(service.GetStagedUpdate(), Is.Null);
    }

    [Test]
    public async Task CheckSelectsPackageForRuntimeAndTreatsRateLimitAsNoUpdate()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")));
        using var service = fixture.Service(fixture.Options());
        var release = await service.CheckAsync(CancellationToken.None);
        Assert.That(release?.Version, Is.EqualTo(AppVersion.Parse("0.6.0")));
        Assert.That(release!.Sha256, Has.Length.EqualTo(64));
        fixture.Routes[UpdateFixture.Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.That(await service.CheckAsync(CancellationToken.None), Is.Null, "A rate limit is not an error for the user.");
    }

    [Test]
    public async Task VerifiedZipIsStagedAndAppliedByRenameWhileTheExecutableIsOpen()
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, WindowsExecutable);
        File.WriteAllText(executable, "old");
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")));
        var options = fixture.Options();
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            Assert.That((await service.DownloadAsync(release, operation)).Version, Is.EqualTo(release.Version));
        Assert.That(service.GetStagedUpdate()?.Version, Is.EqualTo(release.Version));
        Assert.That(File.ReadAllText(executable), Is.EqualTo("old"), "Nothing is replaced before the application exits.");

        // A running image is opened with delete sharing: it can be renamed but not overwritten.
        using (new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(executable + ".old"), Is.EqualTo("old"));
        Assert.That(service.GetStagedUpdate(), Is.Null);
        Assert.That(Directory.GetDirectories(fixture.Updates), Is.Empty);
        AppUpdateInstaller.Cleanup(options);
        Assert.That(File.Exists(executable + ".old"), Is.False);
    }

    [Test]
    public async Task TarGzPackageIsStagedAndKeepsTheExecutableRunnable()
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, LinuxExecutable);
        File.WriteAllText(executable, "old");
        fixture.PublishRelease("v0.6.0", "linux-x64", TarGz(LinuxExecutable, "new"));
        var options = fixture.Options("linux-x64");
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        Assert.That(release.AssetName, Does.EndWith(".tar.gz"));
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute), Is.True);
    }

    [Test]
    public async Task HashMismatchOrCancellationStagesNothing()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")), digest: "sha256:" + new string('0', 64));
        using var service = fixture.Service(fixture.Options());
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release, operation));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False, "The partial and the package are removed.");
        Assert.That(service.GetStagedUpdate(), Is.Null);
        using (var cancelled = new ApplicationOperationService().Begin("Baixando", cancellationToken: new CancellationToken(true)))
            Assert.CatchAsync<OperationCanceledException>(() => service.DownloadAsync(release, cancelled));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False);
        Assert.That(File.Exists(Path.Combine(fixture.Updates, "pending.json")), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedReplacementRestoresOriginalsAndKeepsTheUpdateWithItsError(bool crossBrand)
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, WindowsExecutable);
        var data = Path.Combine(fixture.Target, "a.dat");
        File.WriteAllText(executable, "old");
        File.WriteAllText(data, "old-data");
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((crossBrand ? WindowsExecutable.Replace("KapibaraStudio", "KapibaraStudio", StringComparison.Ordinal) : WindowsExecutable, "new"), ("a.dat", "new-data")));
        var options = fixture.Options();
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);

        // Blocks staging the executable after the data file was already swapped.
        Directory.CreateDirectory(executable + ".new");
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.False);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(data), Is.EqualTo("old-data"), "Files swapped before the failure are rolled back.");
        Assert.That(service.GetStagedUpdate()?.LastApplyError, Is.Not.Null.And.Not.Empty);

        Directory.Delete(executable + ".new");
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True, "The kept update is retried on the next exit.");
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(data), Is.EqualTo("new-data"));
    }

    [Test]
    public void StalePendingUpdateIsDiscardedAndOnlyThePublishedExecutableUpdatesItself()
    {
        using var fixture = new UpdateFixture();
        var options = fixture.Options();
        var payload = Path.Combine(fixture.Updates, "0.5.0", "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, WindowsExecutable), "same");
        AppUpdateInstaller.WritePending(fixture.Updates, new PendingAppUpdate("0.5.0", payload, fixture.Target, WindowsExecutable));
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.False);
        Assert.That(File.Exists(Path.Combine(fixture.Updates, "pending.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.5.0")), Is.False);

        var host = Path.Combine(fixture.Target, WindowsExecutable);
        var version = AppVersion.Parse("0.5.0");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Supported));
        Assert.That(AppUpdateInstaller.DetectAvailability(host, AppVersion.Parse("0.0.0-local"), "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled));
        Assert.That(AppUpdateInstaller.DetectAvailability(Path.Combine(fixture.Target, "testhost.exe"), version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled));
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, null), Is.EqualTo(AppUpdateAvailability.Disabled));
        File.WriteAllText(Path.Combine(fixture.Target, "EsilvaSoft.KapibaraStudio.Desktop.dll"), "");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled), "dotnet run/build output never replaces itself.");
    }

    [TestCase("win-x64", "EsilvaSoft.KapibaraStudio.Desktop.exe")]
    [TestCase("win-arm64", "EsilvaSoft.KapibaraStudio.Desktop.exe")]
    [TestCase("linux-x64", "EsilvaSoft.KapibaraStudio.Desktop")]
    [TestCase("linux-arm64", "EsilvaSoft.KapibaraStudio.Desktop")]
    public void PublishedExecutablesOfBothBrandsAreSupported(string rid, string name)
    {
        using var fixture = new UpdateFixture();
        var host = Path.Combine(fixture.Target, name);
        var version = AppVersion.Parse("0.5.0");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, rid), Is.EqualTo(AppUpdateAvailability.Supported));
        File.WriteAllText(Path.Combine(fixture.Target, "EsilvaSoft.KapibaraStudio.Desktop.dll"), "");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, rid), Is.EqualTo(AppUpdateAvailability.Disabled));
    }

    [Test]
    public async Task BothPackagedExecutablesUseKapibaraContentAndRefreshExistingLegacyAlias()
    {
        using var fixture = new UpdateFixture();
        var modern = WindowsExecutable.Replace("SlopStudio", "KapibaraStudio", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(fixture.Target, WindowsExecutable), "old-legacy");
        File.WriteAllText(Path.Combine(fixture.Target, modern), "old-modern");
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "legacy-content"), (modern, "modern-content")), kapibara: true);
        using var service = fixture.Service(fixture.Options());
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);
        Assert.That(AppUpdateInstaller.ApplyPending(fixture.Options()), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(fixture.Target, WindowsExecutable)), Is.EqualTo("modern-content"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Target, modern)), Is.EqualTo("modern-content"));
    }

    [Test]
    public void ConcurrentStagingCannotDeleteAnotherDownloadsPartialPackage()
    {
        using var fixture = new UpdateFixture();
        var adapter = new LocalAppUpdateStorage();
        var release = StorageRelease();
        using (var staging = adapter.BeginStaging(fixture.Options(), release, CancellationToken.None))
        {
            using (var target = staging.CreatePackageStream()) target.Write([1, 2, 3]);
            Assert.Throws<IOException>(() => adapter.BeginStaging(fixture.Options(), release, CancellationToken.None));
            Assert.That(File.ReadAllBytes(Path.Combine(fixture.Updates, "0.6.0", release.AssetName + ".partial")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(AppUpdateInstaller.ApplyPending(fixture.Options()), Is.False);
            Assert.Throws<IOException>(() => AppUpdateInstaller.Cleanup(fixture.Options()));
        }
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False);
        using var next = adapter.BeginStaging(fixture.Options(), release, CancellationToken.None);
        Assert.That(next, Is.Not.Null, "Disposal releases the exclusive staging lease.");
    }

    [Test]
    public void CancellationBeforeCommitDiscardsPackageAndPreservesPreviousPendingUpdate()
    {
        using var fixture = new UpdateFixture();
        var previous = Path.Combine(fixture.Updates, "0.5.1", "payload");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, WindowsExecutable), "previous");
        AppUpdateInstaller.WritePending(fixture.Updates, new PendingAppUpdate("0.5.1", previous, fixture.Target, WindowsExecutable));
        var adapter = new LocalAppUpdateStorage();
        using (var staging = adapter.BeginStaging(fixture.Options(), StorageRelease(), CancellationToken.None))
        {
            using (var target = staging.CreatePackageStream()) target.Write(Zip((WindowsExecutable, "new")));
            Assert.Catch<OperationCanceledException>(() => staging.Commit(new CancellationToken(true)));
        }
        Assert.That(adapter.GetStagedUpdate(fixture.Options())?.Version, Is.EqualTo(AppVersion.Parse("0.5.1")));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(previous, WindowsExecutable)), Is.EqualTo("previous"));
    }

    [Test]
    public void ZipTraversalCannotEscapePayloadAndFailedExtractionIsDiscarded()
    {
        using var fixture = new UpdateFixture();
        var adapter = new LocalAppUpdateStorage();
        var sentinel = Path.Combine(fixture.Updates, "protected.txt");
        Directory.CreateDirectory(fixture.Updates);
        File.WriteAllText(sentinel, "keep");
        using (var staging = adapter.BeginStaging(fixture.Options(), StorageRelease(), CancellationToken.None))
        {
            using (var target = staging.CreatePackageStream()) target.Write(Zip(("../../protected.txt", "overwrite"), (WindowsExecutable, "new")));
            Assert.Throws<IOException>(() => staging.Commit(CancellationToken.None));
        }
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False);
        Assert.That(adapter.GetStagedUpdate(fixture.Options()), Is.Null);
    }

    private static AppUpdateRelease StorageRelease() => new(AppVersion.Parse("0.6.0"), "v0.6.0", "package.zip",
        new Uri("https://download.test/package.zip"), 0, null, new Uri("https://github.test/release"), null);

    [Test]
    public async Task VerifiedPackageInstallsInteractiveCliAndCompleteHeadlessRuntimeTogether()
    {
        using var fixture = new UpdateFixture();
        const string runtime = "runtimes/win-x64/native/copilot.exe";
        const string runtimeNode = "runtimes/win-x64/native/runtime.node";
        const string accountCli = "runtimes/win-x64/copilot-cli/copilot.exe";
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "app"),
            (runtime, "wrapper"), (runtimeNode, "native-runtime"), (accountCli, "interactive-cli")), kapibara: true);
        using var service = fixture.Service(fixture.Options());
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);
        Assert.That(AppUpdateInstaller.ApplyPending(fixture.Options()), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(fixture.Target, runtime)), Is.EqualTo("wrapper"));
            Assert.That(File.ReadAllText(Path.Combine(fixture.Target, runtimeNode)), Is.EqualTo("native-runtime"));
            Assert.That(File.ReadAllText(Path.Combine(fixture.Target, accountCli)), Is.EqualTo("interactive-cli"));
        });
    }

    [Test]
    public void LinuxReplacementPreservesChildExecutableModes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Permissões Unix exigem execução Linux no CI.");
            return;
        }
        using var fixture = new UpdateFixture();
        var payload = Path.Combine(fixture.Updates, "payload");
        var child = Path.Combine("runtimes", "linux-x64", "copilot-cli", "copilot");
        Directory.CreateDirectory(Path.Combine(payload, Path.GetDirectoryName(child)!));
        File.WriteAllText(Path.Combine(payload, LinuxExecutable), "app");
        File.WriteAllText(Path.Combine(payload, child), "cli");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(Path.Combine(payload, child), mode);
        AppUpdateInstaller.ReplaceFiles(payload, fixture.Target, LinuxExecutable);
        Assert.That(File.GetUnixFileMode(Path.Combine(fixture.Target, child)), Is.EqualTo(mode));
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        return memory.ToArray();
    }

    private static byte[] TarGz(string name, string content)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = data,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            });
        }
        return memory.ToArray();
    }

    private sealed class UpdateFixture : IDisposable
    {
        public static readonly Uri Api = new("https://api.test/repos/slop/releases");
        public static readonly Uri PrimaryApi = new("https://api.test/repos/kapibara/releases");
        private readonly SyntheticDirectory _root = new();
        public UpdateFixture() => Directory.CreateDirectory(Target);
        public string Target => Path.Combine(_root.Path, "app");
        public string Updates => Path.Combine(_root.Path, "updates");
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
        public List<string> Requests { get; } = [];

        public AppUpdateOptions Options(string rid = "win-x64") => new(AppUpdateAvailability.Supported, AppVersion.Parse("0.5.0"), rid, Target,
            rid.StartsWith("win-", StringComparison.Ordinal) ? WindowsExecutable : LinuxExecutable, Updates, Api);

        public GitHubAppUpdateService Service(AppUpdateOptions options) => new(options, new LocalAppUpdateStorage(), new RouteHandler(this));

        public void PublishRelease(string tag, string rid, byte[] package, string? digest = null, Uri? api = null, bool kapibara = false)
        {
            var version = AppVersion.Parse(tag);
            var names = AppUpdateSelector.GetAssetNames(version, rid);
            var name = kapibara ? names[0] : names[1];
            var url = $"https://downloads.test/{tag}/{name}";
            Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
            var json = JsonSerializer.Serialize(new[]
            {
                new
                {
                    tag_name = tag, draft = false, prerelease = version.IsPrerelease, html_url = $"https://github.test/releases/{tag}",
                    assets = new[] { new { name, size = package.LongLength, browser_download_url = url, digest = digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(package)) } }
                }
            });
            Routes[(api ?? Api).AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        public void Dispose() => _root.Dispose();
    }

    private sealed class RouteHandler(UpdateFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fixture.Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(fixture.Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
