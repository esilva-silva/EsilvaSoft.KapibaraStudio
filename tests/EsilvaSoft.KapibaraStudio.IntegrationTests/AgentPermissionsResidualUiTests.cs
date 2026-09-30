using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using NUnit.Framework;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentPermissionsResidualUiTests
{
    [Test]
    [NonParallelizable]
    public async Task PermissionsWindowRendersLocalizedSectionsInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var repository = new PermissionsRepository
            {
                LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription)),
            };
            var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription, "GitHub Copilot de teste", null,
                [new(Guid.NewGuid(), "Produção"), new(Guid.NewGuid(), "Homologação")], productToolsAvailable: true, clock: TimeProvider.System);
            var window = new AgentPermissionsWindow { DataContext = vm, Width = 660, Height = 760 };
            window.Show();
            await vm.LoadTask;
            foreach (var (theme, name) in new[] { (ThemeVariant.Light, "Light"), (ThemeVariant.Dark, "Dark") })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                foreach (var (size, suffix) in new[] { (new Size(660, 760), "660x760"), (new Size(960, 760), "960x760") })
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    var directory = UiEvidenceDirectory.Current();
                    Directory.CreateDirectory(directory);
                    frame!.Save(Path.Combine(directory, $"agent-permissions-{name}-{suffix}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
            }
            window.Close();
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task LoadFailureKeepsPermissionEditorAndSaveFailClosedUntilRetrySucceeds()
    {
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.Unreadable),
        };
        var vm = Create(repository);

        await vm.LoadTask;

        Assert.That(vm.CanEdit, Is.False);
        Assert.That(vm.CanSave, Is.False);
        Assert.That(vm.CanRetryLoad, Is.True);
        Assert.That(vm.ActiveFile, Is.True, "defaults may be displayed, but the unreadable record cannot be edited or saved");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.That(repository.Saved, Is.Null);

        repository.LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound);
        await vm.RetryLoadCommand.ExecuteAsync(null);

        Assert.That(vm.CanEdit, Is.True);
        Assert.That(vm.CanSave, Is.True);
        Assert.That(vm.CanRetryLoad, Is.False);
    }

    [Test]
    public async Task NotFoundEnablesEditingButDoesNotGrantConsent()
    {
        var vm = Create(new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound),
        });

        await vm.LoadTask;

        Assert.That(vm.CanEdit, Is.True);
        Assert.That(vm.CanSave, Is.True);
        Assert.That(vm.HasConsent, Is.False);
        Assert.That(vm.ShowMongoDocumentConsent, Is.False);
        Assert.That(vm.ShowNativeToolOptions, Is.True);
        Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Not.Contain(AgentProductToolNames.MongoFind));
        Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Not.Contain(AgentProductToolNames.MongoCount));
        Assert.That(vm.ProductToolsStatus, Does.Not.Contain(AgentProductToolNames.MongoFind));
        Assert.That(vm.ProductToolsStatus, Does.Not.Contain(AgentProductToolNames.MongoCount));
    }

    [Test]
    public async Task MongoDocumentConsentIsCopilotSpecificAndPersistedSeparately()
    {
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound),
        };
        var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription,
            "GitHub Copilot", null, [], productToolsAvailable: true, clock: TimeProvider.System);

        await vm.LoadTask;
        Assert.That(vm.ShowMongoDocumentConsent, Is.True);
        Assert.That(vm.ShowNativeToolOptions, Is.False,
            "Copilot uses product registry tools and must not present unavailable native CLI permissions.");
        Assert.That(vm.MongoDocuments, Is.False);
        Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Contain(AgentProductToolNames.MongoFind));
        Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Contain(AgentProductToolNames.MongoCount));
        Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Not.Contain(AgentProductToolNames.GetCollectionSchema),
            "Copilot must not offer live sampling before its dedicated local-consent UI exists.");
        Assert.That(vm.ProductToolsStatus, Does.Contain(AgentProductToolNames.MongoFind));
        Assert.That(vm.ProductToolsStatus, Does.Contain(AgentProductToolNames.MongoCount));
        vm.MongoDocuments = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.That(repository.Saved!.DataSending.MongoDocuments, Is.True);
    }

    [Test]
    public async Task ConnectionScopeAndReadToolSelectionsUpdatePersistedPermissionModel()
    {
        var connectionA = Guid.NewGuid();
        var connectionB = Guid.NewGuid();
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default("claude")),
        };
        var vm = new AgentPermissionsViewModel(repository, null, "claude", "Claude", null,
            [new(connectionA, "A"), new(connectionB, "B")], productToolsAvailable: true, clock: TimeProvider.System);

        await vm.LoadTask;
        vm.LimitConnections = true;
        vm.Connections.Single(item => item.Id == connectionB).IsSelected = false;
        var toolName = vm.ReadTools[0].Name;
        vm.ReadTools[0].IsEnabled = false;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.That(repository.Saved, Is.Not.Null);
        Assert.That(repository.Saved!.ConnectionScope, Is.EqualTo(AgentConnectionScope.Selected));
        Assert.That(repository.Saved.SelectedConnectionIds, Is.EquivalentTo(new[] { connectionA }));
        Assert.That(repository.Saved.EnabledReadTools, Does.Not.Contain(toolName));
    }

    private static AgentPermissionsViewModel Create(PermissionsRepository repository) =>
        new(repository, null, "claude", "Claude", null, [], productToolsAvailable: false, clock: TimeProvider.System);

    private sealed class PermissionsRepository : IAgentProviderPermissionsRepository
    {
        public AgentPersistenceResult<AgentProviderPermissions> LoadResult { get; set; } =
            AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound);
        public AgentProviderPermissions? Saved { get; private set; }

        public Task<AgentPersistenceResult<AgentProviderPermissions>> LoadAsync(string providerId,
            CancellationToken cancellationToken) => Task.FromResult(LoadResult);

        public Task<AgentPersistenceResult<AgentProviderPermissions>> SaveAsync(AgentProviderPermissions permissions,
            long expectedRevision, CancellationToken cancellationToken)
        {
            Saved = permissions with { Revision = expectedRevision + 1 };
            return Task.FromResult(AgentPersistenceResult.Success(Saved));
        }
    }
}
