using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelCustomRoleTests
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

    [TestCase("database")]
    [TestCase("profile")]
    public async Task PreviewIsDiscardedWhenTargetChangesDuringRead(string changedTarget)
    {
        using var context = new WorkspaceTestContext();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        object?[]? capturedArguments = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method != "GetCustomRoleDefinitionAsync")
                throw new InvalidOperationException(method);

            capturedArguments = arguments;
            readStarted.TrySetResult();
            return releaseRead.Task;
        };

        var originalProfile = ConnectionProfile.Create("first", "mongodb://localhost");
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = originalProfile,
            SelectedDatabase = "sample_mflix",
            CustomRoleName = "__kapibara_f6_preview_race",
            CustomRolePrivileges = "[]",
            CustomRoleInheritedRoles = "[]"
        };

        var preview = viewModel.PreviewCreateCustomRoleCommand.ExecuteAsync(null);
        await readStarted.Task;
        if (changedTarget == "profile")
            viewModel.SelectedProfile = ConnectionProfile.Create("second", "mongodb://localhost");
        viewModel.SelectedDatabase = "other_database";
        releaseRead.SetResult("null");
        await preview;
        viewModel.CustomRoleConfirmation = "__kapibara_f6_preview_race";

        Assert.Multiple(() =>
        {
            Assert.That(capturedArguments, Is.Not.Null);
            Assert.That(capturedArguments![1], Is.EqualTo("sample_mflix"));
            Assert.That(viewModel.CustomRolePreview, Is.Empty);
            Assert.That(viewModel.CanApplyCustomRole, Is.False);
        });
    }

    [Test]
    public async Task RolePreviewFailureDoesNotEchoCommandOrUri()
    {
        using var context = new WorkspaceTestContext();
        const string sensitive = "mongodb://user:synthetic-secret@localhost/private";
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetCustomRoleDefinitionAsync" => Task.FromException<string>(
                new InvalidOperationException($"rolesInfo {sensitive}")),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("local", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            CustomRoleName = "fixture-role",
            CustomRolePrivileges = "[]",
            CustomRoleInheritedRoles = "[]"
        };

        await viewModel.PreviewCreateCustomRoleCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.StatusMessage, Does.Not.Contain(sensitive));
            Assert.That(viewModel.StatusMessage, Does.Not.Contain("rolesInfo"));
            Assert.That(viewModel.CustomRolePreview, Is.Empty);
        });
    }
}
