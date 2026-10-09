using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AutocompleteUiTests
{
    [Test]
    public async Task PendingContextChangesAreRejectedAndGhostTracksTheCaretWithoutEditingTheDocument()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new TaskCompletionSource<AutocompleteResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new CompletionServiceFake { Handler = _ => { entered.TrySetResult(); return pending.Task; } };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            // A inferência automática de IA é opt-in e vem desligada por padrão (W3). Este teste cobre justamente
            // esse caminho — resposta obsoleta descartada, ghost que não edita o documento, Tab/Esc e renderização —,
            // então habilita a opção explicitamente, como faria o usuário. A carga da sessão já ocorreu acima, e é ela
            // quem define a configuração efetiva do serviço. As demais origens ficam desligadas no próprio setup: com
            // elas ligadas, o que chega ao ghost dependeria de o catálogo determinístico se abster para este texto,
            // e o teste passaria por coincidência de dados em vez de exercitar a origem que diz cobrir.
            await service.ConfigureAsync(service.Settings with { InlineUseAi = true, InlineUseTraditional = false, UseDictionary = false });
            var tab = workspace.ActiveTab!; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var preview = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus(); editor.Text = "db."; editor.CaretIndex = 3; editor.SelectionStart = editor.SelectionEnd = 3;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            tab.InputJson = "{\"changed\":true}";
            pending.SetResult(new("obsolete", true, ""));
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.That(preview.IsVisible, Is.False, "Changing auxiliary context must discard a late inference.");
            service.Handler = _ => Task.FromResult<AutocompleteResult?>(new("Name: true,\n    Active: true", true, ""));
            editor.Text = "db.Users.find({\n    \n})"; editor.CaretIndex = 20; editor.SelectionStart = editor.SelectionEnd = 20;
            var original = editor.Text;
            await WaitForAsync(() => preview.IsVisible); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(editor.Text, Is.EqualTo(original));
            var ghost = view.FindControl<InlineCompletionTextBlock>("CompletionText")!;
            Assert.That(ghost.Suggestion, Does.Contain("Active"));
            // Syntax coloring splits the suffix into runs; the complete projected suffix must remain unchanged.
            var runs = ghost.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToArray();
            Assert.That(string.Concat(runs.SkipWhile(run => run.Text != ghost.Suggestion).Skip(1).Select(run => run.Text)), Is.EqualTo(original[20..]));
            var caret = view.FindControl<Border>("GhostCaret")!;
            var presenter = editor.TextArea.TextView;
            var expected = presenter.TranslatePoint(editor.PositionInTextView(20), view.FindControl<Canvas>("GhostLayer")!)!.Value;
            Assert.That(Canvas.GetLeft(caret), Is.EqualTo(expected.X).Within(.5));
            Assert.That(Canvas.GetTop(caret), Is.EqualTo(expected.Y).Within(.5));
            editor.Text = new string(' ', 300) + "db.Users." + new string('\n', 40);
            editor.CaretIndex = 309; editor.SelectionStart = editor.SelectionEnd = 309;
            await WaitForAsync(() => preview.IsVisible); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(100, 21); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var scrolled = presenter.TranslatePoint(editor.PositionInTextView(309), view.FindControl<Canvas>("GhostLayer")!)!.Value;
            Assert.That(Canvas.GetLeft(caret), Is.EqualTo(scrolled.X).Within(.5));
            Assert.That(Canvas.GetTop(caret), Is.EqualTo(scrolled.Y).Within(.5));
            Assert.That(view.FindControl<Canvas>("GhostLayer")!.Clip, Is.Not.Null);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.That(preview.IsVisible, Is.False);
            await service.ConfigureAsync(service.Settings with { Enabled = false });
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.That(preview.IsVisible, Is.False);
            tab.Autocomplete = new AutocompleteService();
            // Rebind the view, as production registration normally happens before attachment.
            view.DataContext = null; view.DataContext = tab;
            editor.Text = "Conn"; editor.CaretIndex = 4; editor.SelectionStart = editor.SelectionEnd = 4;
            await WaitForAsync(() => preview.IsVisible);
            Assert.That(ghost.Suggestion, Is.EqualTo("ectionPool"));
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test, Explicit("Requer SLOP_QWEN_MODEL externo."), Category("LocalModelIntegration")]
    public async Task RealQwenCompletionTravelsThroughEditorAndAcceptsTab()
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var catalog = new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver());
            await using var ai = new AiAutocompleteProvider(catalog, () => new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess()));
            var service = new AutocompleteService(ai);
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service, catalog);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            workspace.AutocompletePreferences.ModelPath = path!;
            workspace.AutocompletePreferences.HardwareIndex = (int)AiAccelerationMode.Cpu;
            await workspace.AutocompletePreferences.ApplyCommand.ExecuteAsync(null);
            var tab = workspace.ActiveTab!; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            const string prefix = "function add(a, b) {\n    return ";
            editor.Focus(); editor.Text = prefix + ";\n}"; editor.CaretIndex = prefix.Length;
            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex;
            for (var i = 0; i < 1000 && !view.FindControl<Border>("CompletionPanel")!.IsVisible; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Ready), service.Status.Message);
            Assert.That(view.FindControl<InlineCompletionTextBlock>("CompletionText")!.Suggestion, Is.EqualTo("a + b"));
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = 1366; window.Height = 768; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"autocomplete-real-qwen-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.Text, Is.EqualTo(prefix + "a;\n}"));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.Text, Is.EqualTo(prefix + "a + b;\n}"));
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }
    [Test]
    public async Task PreemptiveSuggestionAcceptsTabDismissesEscapeAndRendersBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var service = new CompletionServiceFake();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service, new CompletionCatalogFake());
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            // A inferência automática de IA é opt-in e vem desligada por padrão (W3). Este teste cobre justamente
            // esse caminho — resposta obsoleta descartada, ghost que não edita o documento, Tab/Esc e renderização —,
            // então habilita a opção explicitamente, como faria o usuário. A carga da sessão já ocorreu acima, e é ela
            // quem define a configuração efetiva do serviço. As demais origens ficam desligadas no próprio setup: com
            // elas ligadas, o que chega ao ghost dependeria de o catálogo determinístico se abster para este texto,
            // e o teste passaria por coincidência de dados em vez de exercitar a origem que diz cobrir.
            await service.ConfigureAsync(service.Settings with { InlineUseAi = true, InlineUseTraditional = false, UseDictionary = false });
            var tab = workspace.ActiveTab!;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var preview = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus(); editor.Text = "db.customers."; editor.CaretIndex = editor.Text.Length;
            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex;
            await WaitForAsync(() => preview.IsVisible);
            var original = editor.Text;
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.Text, Is.EqualTo(original + "find"));
            Assert.That(preview.IsVisible, Is.True);
            for (var i = 0; i < 10 && preview.IsVisible; i++) window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.Text, Is.EqualTo(original + "find({})\n.limit(100)"));
            Assert.That(preview.IsVisible, Is.False);
            editor.Text = "db.orders."; editor.CaretIndex = editor.Text.Length;
            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex;
            await WaitForAsync(() => preview.IsVisible);
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(editor.Bounds.Height, Is.GreaterThan(70));
                        Assert.That(preview.Bounds.Height, Is.GreaterThan(20));
                        Assert.That(preview.Background, Is.EqualTo(editor.Background));
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"autocomplete-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(preview.IsVisible, Is.False);
            Assert.That(editor.Text, Is.EqualTo("db.orders."));
            window.SetRenderScaling(1);
            var preferences = new AutocompleteSettingsWindow { DataContext = workspace.AutocompletePreferences };
            preferences.Show(window);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(520, 420), new Size(660, 680), new Size(900, 760) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        preferences.Width = size.Width; preferences.Height = size.Height; preferences.SetRenderScaling(scale);
                        preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        using var frame = preferences.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"autocomplete-settings-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            preferences.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(preferences.IsVisible, Is.False);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task InlineOverridesShowResetButtonsAndDisableDependentOptionsInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var service = new CompletionServiceFake();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service, new CompletionCatalogFake());
            var preferences = workspace.AutocompletePreferences;
            var window = new AutocompleteSettingsWindow { DataContext = preferences, Width = 660, Height = 680 };
            window.Show();
            // A user override materializes an explicit value and reveals "Usar padrão"; the reset command restores absence.
            preferences.InlineUseAiEffective = true;
            Assert.That(preferences.InlineUseAiIsOverridden, Is.True);
            var snapshotWithOverride = preferences.Snapshot();
            Assert.That(snapshotWithOverride.InlineUseAiValue, Is.True);
            // With the automatic suggestion off, the two dependent checkboxes must stay visually disabled without losing their saved value.
            preferences.InlineEnabledEffective = false;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var traditional = window.GetVisualDescendants().OfType<CheckBox>().Single(box => (string?)box.Content == "Usar sugestões determinísticas (catálogo e schema)");
            var ai = window.GetVisualDescendants().OfType<CheckBox>().Single(box => (string?)box.Content == "Usar IA local nas sugestões automáticas");
            Assert.That(traditional.GetVisualAncestors().OfType<Grid>().First().IsEffectivelyEnabled, Is.False);
            Assert.That(ai.GetVisualAncestors().OfType<Grid>().First().IsEffectivelyEnabled, Is.False);
            Assert.That(ai.IsChecked, Is.True, "Disabling the master switch must not erase the saved value of a dependent option.");
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"autocomplete-settings-inline-overrides-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            preferences.ResetInlineUseAiCommand.Execute(null);
            Assert.That(preferences.InlineUseAiIsOverridden, Is.False);
            Assert.That(preferences.Snapshot().InlineUseAiValue, Is.Null, "Usar padrão must clear the override back to absent, not just toggle it off.");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TokenBudgetComboBoxesKeepSelectionsAndFreeTypedValuesWhenSaved()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            AutocompleteSettings? saved = null;
            var service = new CompletionServiceFake();
            var preferences = new AutocompleteSettingsViewModel(service, catalog: null,
                settings => { saved = settings; return Task.CompletedTask; });
            preferences.Load(new());
            // Keep the visual contract independent of the host/container memory limit (Linux CI can expose less RAM).
            // Hardware detection is covered separately; this test exercises the token controls with a stable tier.
            preferences.SelectedHardwareProfile = preferences.HardwareProfiles.Single(profile => profile.Name.StartsWith("Alto (", StringComparison.Ordinal));
            var window = new AutocompleteSettingsWindow { DataContext = preferences, Width = 660, Height = 680 };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();

            var contextBox = window.FindControl<ComboBox>("ContextTokenBox")!;
            var maximumBox = window.FindControl<ComboBox>("MaximumTokenBox")!;
            Assert.That(contextBox.IsEditable, Is.True);
            Assert.That(maximumBox.IsEditable, Is.True);
            Assert.That(contextBox.ItemsSource, Does.Contain("2048"));
            Assert.That(maximumBox.ItemsSource, Does.Contain("32"));
            Assert.That(contextBox.ItemsSource!.Cast<string>().Any(value => value.Contains('.') || value.Contains(',')), Is.False);
            Assert.That(maximumBox.ItemsSource!.Cast<string>().Any(value => value.Contains('.') || value.Contains(',')), Is.False);

            // A selected suggestion must remain exact; in particular 32 must never become 31.
            contextBox.SelectedItem = contextBox.ItemsSource!.Cast<string>().First(value => value.Replace(".", "", StringComparison.Ordinal) == "4096");
            maximumBox.SelectedItem = "64";
            maximumBox.SelectedItem = "32";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That((preferences.ContextTokensText, preferences.MaximumTokensText), Is.EqualTo(("4096", "32")));

            // Exercise arbitrary text after a selection.
            contextBox.Text = "3072";
            maximumBox.Text = "77";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That((preferences.ContextTokensText, preferences.MaximumTokensText), Is.EqualTo(("3072", "77")));

            // Use the production Snapshot path, which is what Save/Test invoke.
            await preferences.ApplyCommand.ExecuteAsync(null);
            Assert.That((saved!.ContextTokens, saved.MaximumCompletionTokens), Is.EqualTo((3072, 77)));

            var evidenceDirectory = UiEvidenceDirectory.Current();
            Directory.CreateDirectory(evidenceDirectory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(evidenceDirectory, $"autocomplete-agent-model-{theme}.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.That(condition(), Is.True, "A sugestão deveria aparecer sem comando manual.");
    }
}
