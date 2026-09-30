using System.Net;
using System.Security.Cryptography;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AppUpdateStorageContractTests
{
    private static readonly byte[] Package = [1, 2, 3, 4];
    private static readonly AppUpdateOptions Options = new(AppUpdateAvailability.Supported, AppVersion.Parse("0.5.0"),
        "win-x64", "application", "EsilvaSoft.KapibaraStudio.Desktop.exe", "updates", new Uri("https://api.test/releases"));

    [Test]
    public async Task VerifiedDownloadClosesPackageBeforeCommitAndUsesCapturedInstallation()
    {
        var storage = new RecordingStorage();
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        using var operation = new ApplicationOperationService().Begin("Baixando");
        var release = Release();
        Assert.That((await service.DownloadAsync(release, operation)).Version, Is.EqualTo(release.Version));
        Assert.Multiple(() =>
        {
            Assert.That(storage.Installation, Is.SameAs(Options));
            Assert.That(storage.Release, Is.SameAs(release));
            Assert.That(storage.Session.Bytes, Is.EqualTo(Package));
            Assert.That(storage.Session.Committed, Is.True);
            Assert.That(storage.Session.Disposed, Is.True);
        });
    }

    [Test]
    public void HashMismatchDisposesStagingWithoutExtractingOrPublishingPendingState()
    {
        var storage = new RecordingStorage();
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        using var operation = new ApplicationOperationService().Begin("Baixando");
        Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(new string('0', 64)), operation));
        Assert.Multiple(() =>
        {
            Assert.That(storage.Session.Committed, Is.False);
            Assert.That(storage.Session.Disposed, Is.True);
            Assert.That(storage.Session.PackageClosed, Is.True);
        });
    }

    [Test]
    public void StorageWriteFailureDiscardsTransactionAndPreservesError()
    {
        var storage = new RecordingStorage { Session = new RecordingStaging { WriteFailure = new IOException("disk full") } };
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        using var operation = new ApplicationOperationService().Begin("Baixando");
        Assert.That(Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(Release(), operation))?.Message, Is.EqualTo("disk full"));
        Assert.That(storage.Session.Disposed, Is.True);
        Assert.That(storage.Session.Committed, Is.False);
    }

    [Test]
    public void ExtractionFailureDiscardsTransactionAndPreservesError()
    {
        var storage = new RecordingStorage { Session = new RecordingStaging { CommitFailure = new InvalidDataException("invalid archive") } };
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        using var operation = new ApplicationOperationService().Begin("Baixando");
        Assert.That(Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Release(), operation))?.Message,
            Is.EqualTo("invalid archive"));
        Assert.That(storage.Session.Disposed, Is.True);
        Assert.That(storage.Session.Committed, Is.False);
    }

    [Test]
    public void CallerCancellationClosesPackageAndDiscardsTransaction()
    {
        using var cancellation = new CancellationTokenSource();
        var storage = new RecordingStorage();
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler(() => cancellation.Cancel()));
        using var operation = new ApplicationOperationService().Begin("Baixando", cancellationToken: cancellation.Token);
        Assert.CatchAsync<OperationCanceledException>(() => service.DownloadAsync(Release(), operation));
        Assert.That(storage.Session.Disposed, Is.True);
        Assert.That(storage.Session.Committed, Is.False);
    }

    [Test]
    public void ConcurrentStagingRefusalIsVisibleAndDoesNotDisposeAnotherOperation()
    {
        var storage = new RecordingStorage { BeginFailure = new IOException("another staging operation owns the lease") };
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        using var operation = new ApplicationOperationService().Begin("Baixando");
        Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(Release(), operation));
        Assert.That(storage.Session.Disposed, Is.False);
        Assert.That(storage.Session.Committed, Is.False);
    }

    [Test]
    public void PendingStateComesFromInjectedStorage()
    {
        var expected = new StagedAppUpdate(AppVersion.Parse("0.6.0"), "previous replacement failed");
        var storage = new RecordingStorage { Pending = expected };
        using var service = new GitHubAppUpdateService(Options, storage, new PackageHandler());
        Assert.That(service.GetStagedUpdate(), Is.SameAs(expected));
    }

    private static AppUpdateRelease Release(string? hash = null) => new(AppVersion.Parse("0.6.0"),
        "v0.6.0", "package.zip", new Uri("https://download.test/package.zip"), Package.Length,
        hash ?? Convert.ToHexStringLower(SHA256.HashData(Package)), new Uri("https://github.test/release"), null);

    private sealed class RecordingStorage : IAppUpdateStorage
    {
        public RecordingStaging Session { get; init; } = new();
        public AppUpdateInstallation? Installation { get; private set; }
        public AppUpdateRelease? Release { get; private set; }
        public Exception? BeginFailure { get; init; }
        public StagedAppUpdate? Pending { get; init; }
        public StagedAppUpdate? GetStagedUpdate(AppUpdateInstallation installation) => Pending;
        public IAppUpdateStaging BeginStaging(AppUpdateInstallation installation, AppUpdateRelease release, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeginFailure is { } failure) throw failure;
            Installation = installation;
            Release = release;
            return Session;
        }
    }

    private sealed class RecordingStaging : IAppUpdateStaging
    {
        public byte[] Bytes { get; private set; } = [];
        public bool PackageClosed { get; private set; }
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? WriteFailure { get; init; }
        public Exception? CommitFailure { get; init; }
        public Stream CreatePackageStream() => new PackageStream(this);
        public void Commit(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(PackageClosed, Is.True, "The adapter must be able to rename the closed package.");
            if (CommitFailure is { } failure) throw failure;
            Committed = true;
        }
        public void Dispose() => Disposed = true;
        private sealed class PackageStream(RecordingStaging owner) : MemoryStream
        {
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (owner.WriteFailure is { } failure) throw failure;
                return base.WriteAsync(buffer, cancellationToken);
            }
            protected override void Dispose(bool disposing)
            {
                owner.Bytes = ToArray();
                owner.PackageClosed = true;
                base.Dispose(disposing);
            }
        }
    }

    private sealed class PackageHandler(Action? beforeResponse = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            beforeResponse?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://download.test/package.zip")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) });
        }
    }
}
