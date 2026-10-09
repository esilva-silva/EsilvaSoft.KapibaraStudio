using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelProfilerTests
{
    private string _previousLanguage = "pt-BR";

    [SetUp]
    public void SetUp()
    {
        _previousLanguage = LocalizationViewModel.Current.Language;
        LocalizationViewModel.Current.Language = "pt-BR";
    }

    [TearDown]
    public void TearDown() => LocalizationViewModel.Current.Language = _previousLanguage;

    [Test]
    public async Task PreviewCapturesTargetAndDoesNotDisplayFilterPayload()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult("""{"was":0,"slowms":100,"sampleRate":1,"filter":{"comment":"private-value"}}"""),
            "GetTopologyAsync" => Task.FromResult("""{"me":"server-a:27017"}"""),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            ProfilerLevelText = "1",
            ProfilerSlowMsText = "200",
            ProfilerSampleRateText = "0.5",
            ProfilerConfirmation = "sample_mflix"
        };

        await viewModel.PreviewProfilerConfigurationCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanConfigureProfiler, Is.True);
            Assert.That(viewModel.ProfilerConfigurationPreviewText, Does.Contain("sample_mflix"));
            Assert.That(viewModel.ProfilerConfigurationPreviewText, Does.Not.Contain("private-value"));
        });

        viewModel.SelectedDatabase = "other";
        Assert.That(viewModel.CanConfigureProfiler, Is.False);
    }

    [Test]
    public async Task ApplyUsesCapturedDatabaseAndAuditsWithoutFilter()
    {
        using var context = new WorkspaceTestContext();
        ProfilerConfigurationRequest? applied = null;
        context.Mongo.Handler = (method, args) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult("""{"was":0,"slowms":100,"sampleRate":1}"""),
            "GetTopologyAsync" => Task.FromResult("""{"me":"server-a:27017"}"""),
            "ConfigureProfilerAsync" => Apply((ProfilerConfigurationRequest)args[1]!),
            _ => throw new InvalidOperationException(method)
        };
        Task<ProfilerConfigurationResult> Apply(ProfilerConfigurationRequest request)
        {
            applied = request;
            return Task.FromResult(new ProfilerConfigurationResult(1, 200, 0.5m, false));
        }
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            ProfilerLevelText = "1",
            ProfilerSlowMsText = "200",
            ProfilerSampleRateText = "0.5",
            ProfilerConfirmation = "sample_mflix"
        };

        await viewModel.PreviewProfilerConfigurationCommand.ExecuteAsync(null);
        await viewModel.ConfigureProfilerCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(applied?.Database, Is.EqualTo("sample_mflix"));
            Assert.That(applied?.Level, Is.EqualTo(1));
            Assert.That(applied?.FilterMode, Is.EqualTo(ProfilerFilterMode.Unset));
            Assert.That(viewModel.CanConfigureProfiler, Is.False);
            Assert.That(viewModel.StatusMessage, Does.Contain("verificada"));
        });
    }

    [Test]
    public async Task PreviewDiscardsResponseAfterDatabaseChanges()
    {
        using var context = new WorkspaceTestContext();
        var delayedStatus = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetProfilerStatusAsync" => delayedStatus.Task,
            "GetTopologyAsync" => Task.FromResult("""{"me":"server-a:27017"}"""),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            ProfilerConfirmation = "sample_mflix"
        };

        var preview = viewModel.PreviewProfilerConfigurationCommand.ExecuteAsync(null);
        viewModel.SelectedDatabase = "other";
        delayedStatus.SetResult("""{"was":0,"slowms":100,"sampleRate":1}""");
        await preview;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanConfigureProfiler, Is.False);
            Assert.That(viewModel.ProfilerConfigurationPreviewText, Is.Empty);
        });
    }

    [Test]
    public async Task InvalidStatusLeavesNoActionablePreview()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult("not-json"),
            "GetTopologyAsync" => Task.FromResult("""{"me":"server-a:27017"}"""),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            ProfilerConfirmation = "sample_mflix"
        };

        await viewModel.PreviewProfilerConfigurationCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanConfigureProfiler, Is.False);
            Assert.That(viewModel.ProfilerConfigurationPreviewText, Is.Empty);
            Assert.That(viewModel.StatusMessage, Does.Contain("Erro"));
        });
    }
}
