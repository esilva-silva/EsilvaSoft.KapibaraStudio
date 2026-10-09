using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelRuntimeParameterTests
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
    public async Task PreviewCapturesParameterAndSelectionChangeInvalidatesIt()
    {
        using var context = new WorkspaceTestContext();
        string? readParameter = null;
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetRuntimeServerParameterAsync" => Read((string)arguments![1]!),
            _ => throw new InvalidOperationException(method)
        };
        Task<int> Read(string parameterName)
        {
            readParameter = parameterName;
            return Task.FromResult(parameterName == RuntimeServerParameters.MaxLogSizeKb ? 10 : 0);
        }

        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            RuntimeParameterName = RuntimeServerParameters.MaxLogSizeKb,
            RuntimeParameterValue = 8
        };

        await viewModel.PreviewRuntimeParameterCommand.ExecuteAsync(null);
        viewModel.RuntimeParameterConfirmation = RuntimeServerParameters.MaxLogSizeKb;

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeServerParameters.Allowlist, Has.Count.EqualTo(2));
            Assert.That(RuntimeServerParameters.Allowlist, Does.Contain(RuntimeServerParameters.LogLevel));
            Assert.That(RuntimeServerParameters.Allowlist, Does.Contain(RuntimeServerParameters.MaxLogSizeKb));
            Assert.That(viewModel.RuntimeParameterMinimum, Is.EqualTo(1));
            Assert.That(viewModel.RuntimeParameterMaximum, Is.EqualTo(10));
            Assert.That(readParameter, Is.EqualTo(RuntimeServerParameters.MaxLogSizeKb));
            Assert.That(viewModel.CanApplyRuntimeParameter, Is.True);
        });

        viewModel.RuntimeParameterName = RuntimeServerParameters.LogLevel;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanApplyRuntimeParameter, Is.False);
            Assert.That(viewModel.RuntimeParameterPreview, Is.Empty);
            Assert.That(viewModel.RuntimeParameterMinimum, Is.EqualTo(0));
            Assert.That(viewModel.RuntimeParameterMaximum, Is.EqualTo(5));
            Assert.That(viewModel.RuntimeParameterValue, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ApplySendsSelectedAllowlistedParameterAndCapturedBeforeValue()
    {
        using var context = new WorkspaceTestContext();
        RuntimeServerParameterRequest? applied = null;
        context.Mongo.Handler = (method, arguments) => method switch
        {
            "GetRuntimeServerParameterAsync" => Task.FromResult(10),
            "SetRuntimeServerParameterAsync" => Apply((RuntimeServerParameterRequest)arguments![1]!),
            _ => throw new InvalidOperationException(method)
        };
        Task<RuntimeServerParameterMutationResult> Apply(RuntimeServerParameterRequest request)
        {
            applied = request;
            return Task.FromResult(new RuntimeServerParameterMutationResult(request.ParameterName,
                request.PreviousValue, request.RequestedValue, DateTimeOffset.UtcNow));
        }

        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            RuntimeParameterName = RuntimeServerParameters.MaxLogSizeKb,
            RuntimeParameterValue = 9
        };
        await viewModel.PreviewRuntimeParameterCommand.ExecuteAsync(null);
        viewModel.RuntimeParameterConfirmation = RuntimeServerParameters.MaxLogSizeKb;
        Assert.That(viewModel.CanApplyRuntimeParameter, Is.True);

        await viewModel.ApplyRuntimeParameterCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.Not.Null);
            Assert.That(applied!.ParameterName, Is.EqualTo(RuntimeServerParameters.MaxLogSizeKb));
            Assert.That(applied.PreviousValue, Is.EqualTo(10));
            Assert.That(applied.RequestedValue, Is.EqualTo(9));
            Assert.That(applied.ConfirmationParameterName, Is.EqualTo(RuntimeServerParameters.MaxLogSizeKb));
        });
    }
}
