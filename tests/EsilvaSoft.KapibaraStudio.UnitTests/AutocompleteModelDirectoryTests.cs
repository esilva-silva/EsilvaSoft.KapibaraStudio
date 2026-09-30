using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
[Category("Unit")]
public sealed class AutocompleteModelDirectoryTests
{
    [Test]
    public async Task OpeningUsesTheDirectoryReturnedByTheCreationPort()
    {
        var directories = new DirectoryFake { Destination = "normalized-models" };
        var launcher = new LauncherFake();
        var preferences = Preferences(directories, launcher);

        await preferences.OpenModelsDirectoryAsync();

        Assert.Multiple(() =>
        {
            Assert.That(directories.Created, Is.EqualTo("configured-models"));
            Assert.That(launcher.Target, Is.EqualTo("normalized-models"));
            Assert.That(launcher.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FailedCreationNeverCallsTheDirectoryLauncher()
    {
        var directories = new DirectoryFake { Failure = new UnauthorizedAccessException("creation refused") };
        var launcher = new LauncherFake();
        var preferences = Preferences(directories, launcher);

        await preferences.OpenModelsDirectoryAsync();

        Assert.That(launcher.Calls, Is.Zero);
        Assert.That(preferences.OperationStatus, Does.Contain("creation refused"));
    }

    [Test]
    public async Task MissingLauncherFailsClosedBeforeCreatingAnyDirectory()
    {
        var directories = new DirectoryFake();
        var preferences = Preferences(directories, null);

        await preferences.OpenModelsDirectoryAsync();

        Assert.That(directories.Created, Is.Null);
        Assert.That(preferences.OperationStatus, Is.Not.Empty);
    }

    [Test]
    public async Task RefusedNativeLaunchReportsFailureWithoutRetrying()
    {
        var directories = new DirectoryFake { Destination = "normalized-models" };
        var launcher = new LauncherFake { Accepted = false };
        var preferences = Preferences(directories, launcher);

        await preferences.OpenModelsDirectoryAsync();

        Assert.That(preferences.OperationStatus, Is.EqualTo(LocalizationViewModel.Current.Format("createDirectoryFailed", "normalized-models", "")));
        Assert.That(launcher.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task NativeLaunchFailureIsVisible()
    {
        var launcher = new LauncherFake { Failure = new InvalidOperationException("launch refused") };
        var preferences = Preferences(new DirectoryFake(), launcher);

        await preferences.OpenModelsDirectoryAsync();

        Assert.That(preferences.OperationStatus, Does.Contain("launch refused"));
        Assert.That(launcher.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task PreferenceChangeDoesNotRedirectAPendingDirectoryLaunch()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directories = new DirectoryFake { Destination = "normalized-first" };
        var launcher = new LauncherFake { Pending = pending };
        var preferences = Preferences(directories, launcher);

        var operation = preferences.OpenModelsDirectoryAsync();
        preferences.ModelDirectory = "another-models-directory";
        pending.SetResult(true);
        await operation;

        Assert.That(directories.Created, Is.EqualTo("configured-models"));
        Assert.That(launcher.Target, Is.EqualTo("normalized-first"));
        Assert.That(launcher.Calls, Is.EqualTo(1));
    }

    [Test]
    public void CancellationBeforeTheActionCreatesAndLaunchesNothing()
    {
        var directories = new DirectoryFake();
        var launcher = new LauncherFake();
        var preferences = Preferences(directories, launcher);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => preferences.OpenModelsDirectoryAsync(cancellation.Token));
        Assert.That(directories.Created, Is.Null);
        Assert.That(launcher.Calls, Is.Zero);
    }

    private static AutocompleteSettingsViewModel Preferences(DirectoryFake directories, LauncherFake? launcher)
    {
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), null, _ => Task.CompletedTask,
            directories: directories, directoryLauncher: launcher);
        preferences.Load(new AutocompleteSettings { ModelDirectory = "configured-models" });
        return preferences;
    }

    private sealed class LauncherFake : ILocalDirectoryLauncher
    {
        public string? Target { get; private set; }
        public int Calls { get; private set; }
        public bool Accepted { get; init; } = true;
        public Exception? Failure { get; init; }
        public TaskCompletionSource<bool>? Pending { get; init; }

        public Task<bool> OpenAsync(string directory, CancellationToken cancellationToken = default)
        {
            Calls++;
            Target = directory;
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is { } failure ? Task.FromException<bool>(failure)
                : Pending?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(Accepted);
        }
    }

    [Test]
    public void OpenDirectoryUsesCapturedPreferenceAndReturnsAdapterDestination()
    {
        var directories = new DirectoryFake { Destination = "normalized-models" };
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), null, _ => Task.CompletedTask,
            directories: directories);
        preferences.Load(new AutocompleteSettings { ModelDirectory = "  configured-models  " });

        Assert.That(preferences.EnsureModelsDirectory(), Is.EqualTo("normalized-models"));
        Assert.That(directories.Created, Is.EqualTo("configured-models"));
    }

    [Test]
    public void RefusedDirectoryCreationReportsFailureAndDoesNotReturnLaunchTarget()
    {
        var directories = new DirectoryFake { Failure = new UnauthorizedAccessException("permission denied") };
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), null, _ => Task.CompletedTask,
            directories: directories);
        preferences.Load(new AutocompleteSettings { ModelDirectory = "configured-models" });

        Assert.That(preferences.EnsureModelsDirectory(), Is.Null);
        Assert.That(preferences.OperationStatus, Does.Contain("permission denied"));
    }

    [Test]
    public async Task DirectoryPreferenceChangeRefreshesInstalledStateThroughTheAdapter()
    {
        var directories = new DirectoryFake();
        directories.Existing.Add(Path.Combine("first", "model"));
        var preferences = new AutocompleteSettingsViewModel(new CompletionServiceFake(), null, _ => Task.CompletedTask,
            remote: new RemoteFake(), directories: directories);
        preferences.Load(new AutocompleteSettings { ModelDirectory = "first" });

        await preferences.LoadRemoteModelsIfNeededAsync();
        Assert.That(preferences.RemoteModels.Single().IsInstalled, Is.True);
        preferences.ModelDirectory = "second";
        Assert.That(preferences.RemoteModels.Single().IsInstalled, Is.False);
        Assert.That(directories.Probed, Does.Contain(Path.Combine("second", "model")));
    }

    private sealed class DirectoryFake : IModelDirectoryService
    {
        public string Destination { get; init; } = "models";
        public Exception? Failure { get; init; }
        public string? Created { get; private set; }
        public HashSet<string> Existing { get; } = [];
        public List<string> Probed { get; } = [];
        public bool Exists(string path) { Probed.Add(path); return Existing.Contains(path); }
        public string EnsureExists(string path)
        {
            Created = path;
            if (Failure is { } failure) throw failure;
            return Destination;
        }
    }

    private sealed class RemoteFake : IRemoteModelSource
    {
        public IReadOnlyList<Uri> RepositoryUrls => [];
        public Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteModelVariant>>([new("fixture", "revision", "CPU", "model", 1, "MIT", new("https://example.invalid/model"), [])]);
        public Task<string> DownloadAsync(RemoteModelVariant variant, string directory, IProgress<RemoteModelProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Download is outside this scenario.");
    }
}
