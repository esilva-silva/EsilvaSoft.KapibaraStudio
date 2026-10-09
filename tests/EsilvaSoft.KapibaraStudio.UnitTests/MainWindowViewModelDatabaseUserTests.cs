using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MainWindowViewModelDatabaseUserTests
{
    private static readonly string[] ExpectedReadRolesMutationOrder = ["GetUserRolesAsync", "UpdateUserRolesAsync"];
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
    [TestCase("payload")]
    public async Task RolePreviewIsDiscardedWhenTargetOrInputChangesDuringRead(string changed)
    {
        using var context = new WorkspaceTestContext();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        object?[]? captured = null;
        context.Mongo.Handler = (method, arguments) =>
        {
            if (method != "GetUserRolesAsync") throw new InvalidOperationException(method);
            captured = arguments;
            readStarted.TrySetResult();
            return releaseRead.Task;
        };

        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            DatabaseRoleUsername = "fixture-user",
            DatabaseRolePayload = "[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"
        };

        var preview = viewModel.PreviewDatabaseUserRolesCommand.ExecuteAsync(null);
        await readStarted.Task;
        if (changed == "profile")
            viewModel.SelectedProfile = ConnectionProfile.Create("second", "mongodb://localhost");
        if (changed == "database")
            viewModel.SelectedDatabase = "other_database";
        if (changed == "payload")
            viewModel.DatabaseRolePayload = "[{\"role\":\"readWrite\",\"db\":\"sample_mflix\"}]";
        releaseRead.SetResult("[]");
        await preview;

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured![1], Is.EqualTo("sample_mflix"));
            Assert.That(viewModel.DatabaseUserRolesPreview, Is.Empty);
            Assert.That(viewModel.CanUpdateDatabaseUserRoles, Is.False);
        });
    }

    [Test]
    public async Task ApplyingRolesRequiresPreviewAndExactConfirmationAndUsesCapturedSnapshot()
    {
        using var context = new WorkspaceTestContext();
        var observed = new List<(string Method, object?[] Arguments)>();
        context.Mongo.Handler = (method, arguments) =>
        {
            observed.Add((method, arguments));
            return method switch
            {
                "GetUserRolesAsync" => Task.FromResult<string?>("[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"),
                "UpdateUserRolesAsync" => Task.CompletedTask,
                _ => throw new InvalidOperationException(method)
            };
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            DatabaseRoleUsername = "fixture-user",
            DatabaseRolePayload = "[{\"role\":\"readWrite\",\"db\":\"sample_mflix\"}]"
        };

        Assert.That(viewModel.CanUpdateDatabaseUserRoles, Is.False);
        await viewModel.PreviewDatabaseUserRolesCommand.ExecuteAsync(null);
        viewModel.DatabaseRoleConfirmation = "wrong-user";
        Assert.That(viewModel.CanUpdateDatabaseUserRoles, Is.False);
        viewModel.DatabaseRoleConfirmation = "fixture-user";
        Assert.That(viewModel.CanUpdateDatabaseUserRoles, Is.True);
        await viewModel.UpdateDatabaseUserRolesCommand.ExecuteAsync(null);

        var mutation = observed.Single(call => call.Method == "UpdateUserRolesAsync");
        var request = (DatabaseUserRoleRequest)mutation.Arguments[1]!;
        Assert.Multiple(() =>
        {
            Assert.That(observed.Select(call => call.Method), Is.EqualTo(ExpectedReadRolesMutationOrder));
            Assert.That(request.ExpectedRolesJson, Is.EqualTo("[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"));
            Assert.That(request.ConfirmationUsername, Is.EqualTo("fixture-user"));
        });
    }

    [Test]
    public async Task CreateUserClearsVolatilePasswordAfterFailureWithoutPublishingIt()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "CreateUserAsync" => Task.FromException(new InvalidOperationException("write failed")),
            _ => throw new InvalidOperationException(method)
        };
        const string password = "synthetic-secret-only";
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            NewDatabaseUsername = "fixture-user",
            NewDatabaseUserPassword = password,
            NewDatabaseUserConfirmation = "fixture-user",
            NewDatabaseUserRoles = "[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"
        };

        await viewModel.CreateDatabaseUserCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NewDatabaseUserPassword, Is.Empty);
            Assert.That(viewModel.StatusMessage, Does.Not.Contain(password));
            Assert.That(viewModel.AdministrationResults, Does.Not.Contain(password));
        });
    }

    [Test]
    public async Task UnexpectedUserFailureDoesNotExposeEmbeddedPasswordOrCommand()
    {
        using var context = new WorkspaceTestContext();
        const string password = "synthetic-secret-only";
        context.Mongo.Handler = (method, _) => method switch
        {
            "CreateUserAsync" => Task.FromException(new InvalidOperationException(
                $"createUser pwd={password} mongodb://user:{password}@localhost/private")),
            _ => throw new InvalidOperationException(method)
        };
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            NewDatabaseUsername = "fixture-user",
            NewDatabaseUserPassword = password,
            NewDatabaseUserConfirmation = "fixture-user",
            NewDatabaseUserRoles = "[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"
        };

        await viewModel.CreateDatabaseUserCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NewDatabaseUserPassword, Is.Empty);
            Assert.That(viewModel.StatusMessage, Does.Not.Contain(password));
            Assert.That(viewModel.StatusMessage, Does.Not.Contain("createUser"));
            Assert.That(viewModel.StatusMessage, Does.Contain("não concluída"));
        });
    }

    [Test]
    public async Task UserAuditOmitsFreeFormUsernameAndPassword()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "CreateUserAsync" => Task.CompletedTask,
            _ => throw new InvalidOperationException(method)
        };
        const string username = "mongodb://private-target";
        const string password = "synthetic-secret-only";
        var viewModel = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = ConnectionProfile.Create("first", "mongodb://localhost"),
            SelectedDatabase = "sample_mflix",
            NewDatabaseUsername = username,
            NewDatabaseUserPassword = password,
            NewDatabaseUserConfirmation = username,
            NewDatabaseUserRoles = "[{\"role\":\"read\",\"db\":\"sample_mflix\"}]"
        };

        await viewModel.CreateDatabaseUserCommand.ExecuteAsync(null);
        var audit = (await context.Repository.GetRecentAuditAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(audit.Action, Is.EqualTo("user.create"));
            Assert.That(audit.Summary, Does.Not.Contain(username));
            Assert.That(audit.Summary, Does.Not.Contain(password));
            Assert.That(audit.Database, Is.EqualTo("sample_mflix"));
        });
    }
}
