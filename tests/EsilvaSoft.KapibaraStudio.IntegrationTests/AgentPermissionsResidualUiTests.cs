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
    private static readonly string[] BudgetLanguages = ["pt-BR", "en", "es", "zh-CN"];
    private static readonly Size[] BudgetSizes = [new(520, 500), new(700, 720), new(960, 760)];
    private static readonly double[] BudgetScales = [1.0, 1.5, 2.0];
    private static readonly string[] BudgetInputs = ["100", "", "0"];
    private static readonly string[] BudgetTextNames = ["ToolCallLimitHelp", "ToolCallLimitError", "PermissionsStatus"];
    [Test, NonParallelizable]
    public async Task CopilotToolBudgetFieldRendersAcrossLocalesThemesSizesAndScales()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            try
            {
                foreach (var language in BudgetLanguages)
                {
                    LocalizationViewModel.Current.Language = language;
                    var repository = new PermissionsRepository
                    {
                        LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription)),
                    };
                    var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription, "GitHub Copilot", @"D:\synthetic-workspace",
                        [], true, TimeProvider.System);
                    var window = new AgentPermissionsWindow { DataContext = vm };
                    window.Show();
                    await vm.LoadTask;
                    var box = window.FindControl<TextBox>("ToolCallLimitBox")!;
                    try
                    {
                        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                            foreach (var size in BudgetSizes)
                                foreach (var scale in BudgetScales)
                                    foreach (var input in BudgetInputs)
                                    {
                                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                                        window.Width = size.Width;
                                        window.Height = size.Height;
                                        window.SetRenderScaling(scale);
                                        box.Text = input;
                                        window.UpdateLayout();
                                        window.FindControl<StackPanel>("ToolCallBudgetControls")!.BringIntoView();
                                        Dispatcher.UIThread.RunJobs();
                                        window.UpdateLayout();
                                        Assert.That(box.Text, Is.EqualTo(input));
                                        Assert.That(vm.ToolCallLimitText, Is.EqualTo(input), "The field must update the permission editor through its two-way binding.");
                                        Assert.That(vm.IsToolCallLimitValid, Is.EqualTo(input != "0"));
                                        Assert.That(window.FindControl<Button>("SavePermissionsButton")!.IsEffectivelyEnabled, Is.EqualTo(input != "0"),
                                            "Command availability controls effective enablement, not the local IsEnabled value.");
                                        foreach (var textName in BudgetTextNames)
                                        {
                                            var block = window.FindControl<TextBlock>(textName)!;
                                            if (!block.IsVisible) continue;
                                            Assert.That(block.TextLayout.Width, Is.LessThanOrEqualTo(block.Bounds.Width + 1));
                                            Assert.That(block.TextLayout.Height, Is.LessThanOrEqualTo(block.Bounds.Height + 1));
                                        }
                                        Assert.That(Avalonia.Automation.AutomationProperties.GetName(box),
                                            Is.EqualTo(LocalizationViewModel.Current.Resolve("agentPermissionsToolCallLimit")));
                                        using var frame = window.CaptureRenderedFrame();
                                        var directory = UiEvidenceDirectory.Current();
                                        Directory.CreateDirectory(directory);
                                        var state = input.Length == 0 ? "unlimited" : input == "0" ? "invalid" : "default";
                                        frame!.Save(Path.Combine(directory, $"copilot-tool-budget-{language}-{theme}-{size.Width}x{size.Height}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{state}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                                    }
                    }
                    finally { window.Close(); }
                }
            }
            finally
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            return true;
        }, CancellationToken.None);
    }

    [TestCase("100", 100)]
    [TestCase("7", 7)]
    [TestCase("", null)]
    [TestCase("   ", null)]
    public async Task ToolBudgetEditorSavesPositiveOrUnlimitedValues(string input, int? expected)
    {
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription)),
        };
        var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription, "Copilot", null,
            [], true, TimeProvider.System);
        await vm.LoadTask;
        Assert.That(vm.ToolCallLimitText, Is.EqualTo("100"));
        vm.ToolCallLimitText = input;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.That(repository.Saved!.MaximumToolCallsPerTurn, Is.EqualTo(expected));
        repository.LoadResult = AgentPersistenceResult.Success(repository.Saved);
        var reopened = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription, "Copilot", null,
            [], true, TimeProvider.System);
        await reopened.LoadTask;
        Assert.That(reopened.ToolCallLimitText, Is.EqualTo(expected?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("abc")]
    [TestCase("2147483648")]
    public async Task InvalidToolBudgetCannotSaveOtherPermissionChanges(string input)
    {
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription)),
        };
        var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.GitHubCopilotSubscription, "Copilot", null,
            [], true, TimeProvider.System);
        await vm.LoadTask;
        vm.ToolCallLimitText = input;
        vm.ActiveFile = false;
        Assert.That(vm.CanSave, Is.False);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.That(repository.Saved, Is.Null);
        vm.ToolCallLimitText = "";
        Assert.That(vm.CanSave, Is.True);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.That(repository.Saved!.DataSending.ActiveFile, Is.False);
        Assert.That(repository.Saved.MaximumToolCallsPerTurn, Is.Null);
    }

    [Test]
    [NonParallelizable]
    public async Task PermissionsWindowRendersLocalizedSectionsInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            foreach (var (providerId, providerName, prefix) in new[]
                     {
                         (AgentProviderIds.GitHubCopilotSubscription, "GitHub Copilot de teste", "agent-permissions"),
                         (AgentProviderIds.ClaudeCodeSubscription, "Claude (assinatura)", "agent-permissions-Claude"),
                     })
            {
                var repository = new PermissionsRepository
                {
                    LoadResult = AgentPersistenceResult.Success(AgentProviderPermissions.Default(providerId)),
                };
                var vm = new AgentPermissionsViewModel(repository, null, providerId, providerName, null,
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
                        frame!.Save(Path.Combine(directory, $"{prefix}-{name}-{suffix}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                }
                window.Close();
            }
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
    public async Task CopilotMongoDocumentConsentIsPersistedSeparately()
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
    public async Task ClaudeCanOptIntoMongoDocumentToolsWithItsOwnProviderPermissions()
    {
        var repository = new PermissionsRepository
        {
            LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound),
        };
        var vm = new AgentPermissionsViewModel(repository, null, AgentProviderIds.ClaudeCodeSubscription,
            "Claude (assinatura)", null, [], productToolsAvailable: true, clock: TimeProvider.System);

        await vm.LoadTask;

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowMongoDocumentConsent, Is.True);
            Assert.That(vm.ShowNativeToolOptions, Is.True, "A allowlist nativa do Claude continua editável.");
            Assert.That(vm.MongoDocuments, Is.False, "Consentimento é opt-in e começa desligado.");
            Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Contain(AgentProductToolNames.MongoFind));
            Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Contain(AgentProductToolNames.MongoExplain));
            Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Not.Contain(AgentProductToolNames.GetCollectionSchema),
                "A amostragem ao vivo permanece fechada até a UI obter consentimento local delimitado.");
        });

        vm.MongoDocuments = true;
        vm.ReadTools.Single(tool => tool.Name == AgentProductToolNames.MongoFind).IsEnabled = true;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(repository.Saved!.ProviderId, Is.EqualTo(AgentProviderIds.ClaudeCodeSubscription));
            Assert.That(repository.Saved.DataSending.MongoDocuments, Is.True);
            Assert.That(repository.Saved.EnabledReadTools, Does.Contain(AgentProductToolNames.MongoFind));
        });
    }

    [Test]
    public async Task NoProviderSeesLiveSchemaSamplingWithoutItsDedicatedConsentFlow()
    {
        foreach (var provider in new[] { AgentProviderIds.GitHubCopilotSubscription, AgentProviderIds.ClaudeCodeSubscription, "other-provider" })
        {
            var repository = new PermissionsRepository
            {
                LoadResult = AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound),
            };
            var vm = new AgentPermissionsViewModel(repository, null, provider, provider, null, [],
                productToolsAvailable: true, clock: TimeProvider.System);
            await vm.LoadTask;
            Assert.That(vm.ReadTools.Select(tool => tool.Name), Does.Not.Contain(AgentProductToolNames.GetCollectionSchema), provider);
        }
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
