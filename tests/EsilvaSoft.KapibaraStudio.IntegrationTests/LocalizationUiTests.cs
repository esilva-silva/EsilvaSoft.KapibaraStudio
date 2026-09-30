using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class LocalizationUiTests
{
    [Test]
    public async Task ConnectionsWindowReactsToGlobalLanguageChanges()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            await workspace.InitializeAsync();
            var window = new ConnectionsWindow(workspace);
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var evidenceDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence", "localization");
            Directory.CreateDirectory(evidenceDirectory);
            var previousLanguage = LocalizationViewModel.Current.Language;
            var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;

            try
            {
                Assert.That(window.Title, Is.EqualTo("Conexões"));
                Assert.That(window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content?.ToString()),
                    Does.Contain("Nova conexão"));

                LocalizationViewModel.Current.Language = "en";
                Dispatcher.UIThread.RunJobs();
                Assert.That(window.Title, Is.EqualTo("Connections"));
                Assert.That(window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content?.ToString()),
                    Does.Contain("New connection"));

                LocalizationViewModel.Current.Language = "zh-CN";
                Dispatcher.UIThread.RunJobs();
                Assert.That(window.Title, Is.EqualTo("连接"));
                Assert.That(window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content?.ToString()),
                    Does.Contain("新建连接"));

                LocalizationViewModel.Current.Language = "es";
                Dispatcher.UIThread.RunJobs();
                Assert.That(window.Title, Is.EqualTo("Conexiones"));
                Assert.That(window.GetLogicalDescendants().OfType<Button>().Select(button => button.Content?.ToString()),
                    Does.Contain("Nueva conexión"));

                foreach (var language in ApplicationLanguages.All.Select(language => language.Code))
                {
                    await context.Repository.SaveSessionAsync(new WorkspaceSession
                    {
                        Preferences = new WorkspacePreferences { Language = language }
                    });
                    LocalizationViewModel.Current.Language = language;
                    var renderWindow = new ConnectionsWindow(workspace);
                    renderWindow.Show();
                    foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    {
                        Avalonia.Application.Current.RequestedThemeVariant = theme;
                        renderWindow.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        foreach (var button in renderWindow.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                        {
                            Assert.That(button.Bounds.Width, Is.GreaterThan(0), $"{language}/{theme}: button width");
                            Assert.That(button.Bounds.Height, Is.GreaterThan(0), $"{language}/{theme}: button height");
                        }
                        using var frame = renderWindow.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null, $"{language}/{theme}: frame");
                        frame!.Save(Path.Combine(evidenceDirectory, $"connections-{language}-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                    renderWindow.Close();
                }
            }
            finally
            {
                LocalizationViewModel.Current.Language = previousLanguage;
                Avalonia.Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task AdditionalWindowsRenderInAllLanguagesAndThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var evidenceDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence", "localization");
            Directory.CreateDirectory(evidenceDirectory);
            var previousLanguage = LocalizationViewModel.Current.Language;
            var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;

            try
            {
                foreach (var language in ApplicationLanguages.All.Select(language => language.Code))
                {
                    using var languageContext = new WorkspaceTestContext();
                    await languageContext.Repository.SaveSessionAsync(new WorkspaceSession
                    {
                        Preferences = new WorkspacePreferences { Language = language }
                    });
                    LocalizationViewModel.Current.Language = language;
                    var mainWorkspaceModel = new WorkspaceViewModel(languageContext.Workspace, languageContext.Repository);
                    var mainWorkspaceWindow = new MainWindow { DataContext = mainWorkspaceModel };
                    // WorkspaceViewModel initializes the process-wide catalog from its default language.
                    // Restore the requested language before constructing the remaining view models so their
                    // initial status values are localized as well.
                    LocalizationViewModel.Current.Language = language;
                    var windows = new (string Name, Window Window)[]
                    {
                        ("environments", new EnvironmentsWindow { DataContext = new EnvironmentsViewModel(languageContext.Workspace) }),
                        ("history", new HistoryWindow { DataContext = new WorkspaceTabViewModel(languageContext.Workspace) }),
                        ("document-json", new DocumentJsonWindow { DataContext = new DocumentJsonViewModel(new ResultDocumentViewModel("{\"_id\":{\"$oid\":\"66e3bd5b3bc3f54c840d73ac\"},\"name\":\"CJK\"}", 0)) }),
                        ("mutation", new DocumentMutationWindow
                        {
                            DataContext = new DocumentMutationViewModel(languageContext.Workspace, ConnectionProfile.Create("Visual", "mongodb://localhost", "db"), "db", "collection",
                                new ResultDocumentViewModel("{\"_id\":{\"$oid\":\"66e3bd5b3bc3f54c840d73ac\"},\"name\":\"CJK\"}", 0), "Editar")
                        }),
                        ("autocomplete", new AutocompleteSettingsWindow
                        {
                            DataContext = CreateAutocompletePreferences()
                        }),
                        ("tools", new WorkspaceToolsWindow { DataContext = new MainWindowViewModel(languageContext.Workspace, autoLoadCollections: false) }),
                        ("main", mainWorkspaceWindow)
                    };
                    LocalizationViewModel.Current.Language = language;

                    foreach (var (name, window) in windows)
                    {
                        window.Show();
                        if (window is MainWindow { DataContext: WorkspaceViewModel workspaceModel } workspaceWindow)
                        {
                            await workspaceWindow.InitializationTask;
                            workspaceModel.Language = language;
                            LocalizationViewModel.Current.Language = language;
                        }
                        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                        {
                            Avalonia.Application.Current.RequestedThemeVariant = theme;
                            window.UpdateLayout();
                            Dispatcher.UIThread.RunJobs();
                            var visibleButtons = window.GetVisualDescendants().OfType<Button>()
                                .Where(button => button.IsEffectivelyVisible && button.Bounds.Width > 0 && button.Bounds.Height > 0)
                                .ToArray();
                            Assert.That(visibleButtons, Is.Not.Empty, $"{name}/{language}/{theme}: visible buttons");
                            foreach (var button in visibleButtons)
                            {
                                Assert.That(button.Bounds.Width, Is.GreaterThan(0), $"{name}/{language}/{theme}: button width");
                                Assert.That(button.Bounds.Height, Is.GreaterThan(0), $"{name}/{language}/{theme}: button height");
                            }
                            using var frame = window.CaptureRenderedFrame();
                            Assert.That(frame, Is.Not.Null, $"{name}/{language}/{theme}: frame");
                            frame!.Save(Path.Combine(evidenceDirectory, $"{name}-{language}-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        }
                        window.Close();
                        if (window.DataContext is IDisposable disposable) disposable.Dispose();
                    }
                }
            }
            finally
            {
                LocalizationViewModel.Current.Language = previousLanguage;
                Avalonia.Application.Current.RequestedThemeVariant = previousTheme;
            }

            return true;
        }, CancellationToken.None);
    }

    private static AutocompleteSettingsViewModel CreateAutocompletePreferences()
    {
        var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), catalog: null, _ => Task.CompletedTask);
        preferences.Load(new AutocompleteSettings());
        return preferences;
    }
}
