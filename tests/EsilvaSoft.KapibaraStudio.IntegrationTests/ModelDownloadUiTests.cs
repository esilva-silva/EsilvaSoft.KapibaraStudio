using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class ModelDownloadUiTests
{
    [Test]
    public async Task DefaultOnnxCatalogSelectsNanoCpuInt4AndExplainsExperimentalGpu()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var synthetic = new SyntheticDirectory();
            using var handler = new KapiCoderOnnxCatalogHandler { UseRepresentativePackageSizes = true };
            using var remote = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
            var models = Path.Combine(synthetic.Path, "Models");
            Directory.CreateDirectory(models);
            var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), new LocalModelCatalog(models, fileAccess: new LocalModelFileAccess()), _ => Task.CompletedTask,
                remote: remote, directories: new LocalModelDirectoryService());
            preferences.Load(new AutocompleteSettings { ModelDirectory = models });
            var previousLanguage = LocalizationViewModel.Current.Language;
            var window = new AutocompleteSettingsWindow { DataContext = preferences };
            try
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                window.Show();
                await WaitUntil(() => preferences.RemoteModels.Count == 5);
                var selected = preferences.SelectedRemoteModel!;
                Assert.That(selected.Title, Is.EqualTo("KapiCoder-Mongo-Nano — CPU INT4"));
                Assert.That(selected.Variant.Variant, Is.EqualTo("cpu-int4"));
                Assert.That(selected.Variant.SizeBytes, Is.EqualTo(410_666_723));
                Assert.That(selected.Subtitle, Does.StartWith("411 MB").And.Contain("Padrão em CPU: INT4."));
                Assert.That(preferences.SelectedRemoteModelCardUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Nano")));
                Assert.That(preferences.RemoteModels[2].Subtitle, Does.Contain("experimental"));
                Assert.That(preferences.RemoteModelDetails, Does.Contain(Path.Combine(models, "KapiCoder-Mongo-Nano-ONNX-cpu-int4")));
                Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.True);
                var download = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Baixar modelo");
                RenderCatalog(window, download, "kapicoder-onnx-ready", expectedEnabled: true);
                Assert.That(handler.Requests, Has.Count.EqualTo(4), "Opening preferences does not download weights.");
            }
            finally
            {
                window.Close();
                LocalizationViewModel.Current.Language = previousLanguage;
            }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PublishedSafetensorsAreVisibleButCannotBeDownloaded()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var synthetic = new SyntheticDirectory();
            var models = Path.Combine(synthetic.Path, "Models");
            Directory.CreateDirectory(models);
            var remote = new PublishedKapiCoderModels();
            var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), new LocalModelCatalog(models, fileAccess: new LocalModelFileAccess()), _ => Task.CompletedTask,
                remote: remote, directories: new LocalModelDirectoryService());
            preferences.Load(new AutocompleteSettings { ModelDirectory = models });
            var window = new AutocompleteSettingsWindow { DataContext = preferences };
            window.Show();
            await WaitUntil(() => preferences.RemoteModels.Count == 3);
            Assert.That(preferences.SelectedRemoteModel!.Variant.IsInstallable, Is.True, "An installable variant is preferred even if listed after informational models.");
            preferences.SelectedRemoteModel = preferences.RemoteModels[0];
            Assert.That(preferences.SelectedRemoteModelCardUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Nano")));
            Assert.That(preferences.SelectedRemoteModel!.Subtitle, Does.Contain("ONNX GenAI"));
            Assert.That(preferences.RemoteModelDetails, Does.Not.Contain(models));
            Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.False);
            await preferences.DownloadModelCommand.ExecuteAsync(null);
            Assert.That(remote.DownloadCalls, Is.Zero, "Calling the handler directly must not bypass the disabled command.");
            Assert.That(preferences.RemoteModels[1].Title, Does.Contain("KapiCoder-Mongo-Small"));
            var download = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Baixar modelo");
            RenderPublished(window, download);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void RenderPublished(AutocompleteSettingsWindow window, Button download) =>
        RenderCatalog(window, download, "kapicoder-catalog", expectedEnabled: false);

    private static void RenderCatalog(AutocompleteSettingsWindow window, Button download, string prefix, bool expectedEnabled)
    {
        var directory = UiEvidenceDirectory.Current();
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(520, 420), new Size(660, 680), new Size(900, 760) })
                foreach (var scale in new[] { 1d, 1.5, 2 })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    download.BringIntoView();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    // Avalonia combina CanExecute com IsEnabledCore; o valor efetivo governa a interação.
                    Assert.That(download.IsEffectivelyEnabled, Is.EqualTo(expectedEnabled));
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"{prefix}-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"),
                        new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
    }

    [Test]
    public async Task PreferencesNameModelsClearlyDownloadWithProgressSelectAndCancelFromStatusBar()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var synthetic = new SyntheticDirectory();
            var models = Path.Combine(synthetic.Path, "Models");
            try
            {
                var remote = new ControlledRemoteModels();
                var operations = new ApplicationOperationService();
                var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), new LocalModelCatalog(models, fileAccess: new LocalModelFileAccess()), _ => Task.CompletedTask,
                    remote: remote, operations: operations, directories: new LocalModelDirectoryService());
                preferences.Load(new AutocompleteSettings { ModelDirectory = models });
                var window = new AutocompleteSettingsWindow { DataContext = preferences };
                window.Show();
                await WaitUntil(() => preferences.RemoteModels.Count == 4);
                Assert.That(preferences.RemoteModels.Select(option => option.Title), Is.EqualTo(ControlledRemoteModels.Titles));
                Assert.That(preferences.SelectedRemoteModel?.Title, Is.EqualTo("SlopCoder-Mongo-0.5B — CPU INT4"), "The first model not installed yet is preselected.");
                Assert.That(preferences.RemoteModels[3].Subtitle, Does.Contain("GB").And.Contain("recomendado com GPU"));
                Assert.That(preferences.SelectedRemoteModelCardUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/SlopCoder-Mongo-0.5B")));
                Assert.That(preferences.RemoteModelDetails, Does.Contain(Path.Combine(models, "SlopCoder-Mongo-0.5B-ONNX-int4")));
                Assert.That(preferences.RemoteSourceNotice, Does.Contain("esilva/SlopCoder-Mongo-1.5B-full-ONNX"));

                Assert.That(Directory.Exists(models), Is.False);
                Assert.That(preferences.EnsureModelsDirectory(), Is.EqualTo(models), "Abrir pasta creates the models directory before opening it.");
                var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.That(buttons.Count(button => AutomationProperties.GetName(button) == "Abrir diretório de modelos"), Is.EqualTo(1));

                preferences.SelectedRemoteModel = preferences.RemoteModels[1];
                var download = buttons.Single(button => AutomationProperties.GetName(button) == "Baixar modelo");
                Assert.That(download.Command!.CanExecute(null), Is.True);
                download.Command.Execute(null);
                await WaitUntil(() => preferences.DownloadProgress >= 50);
                Assert.That(preferences.IsDownloadingModel, Is.True);
                Assert.That(operations.ActiveOperations.Single().Description, Does.Contain("SlopCoder-Mongo-0.5B — GPU DirectML FP16"), "The status bar shows the download.");
                var cancel = buttons.Single(button => AutomationProperties.GetName(button) == "Cancelar download do modelo");
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.That(cancel.IsVisible, Is.True);
                Assert.That(download.IsVisible, Is.False);
                Render(window, cancel);

                remote.Gate.SetResult();
                await WaitUntil(() => !preferences.IsDownloadingModel && preferences.SelectedModelOption is not null);
                Assert.That(preferences.SelectedModelOption!.Reference, Is.EqualTo("SlopCoder-Mongo-0.5B-ONNX-dml-fp16"), "The installed model is selected for saving.");
                Assert.That(preferences.SelectedModelOption.Title, Is.EqualTo("SlopCoder-Mongo-0.5B — GPU DirectML FP16"), "Selection uses the same name as the download list.");
                Assert.That(preferences.SelectedModelOption.Subtitle, Does.Contain("pasta SlopCoder-Mongo-0.5B-ONNX-dml-fp16"));
                Assert.That(preferences.DownloadStatus, Does.Contain("Salvar"));
                Assert.That(preferences.RemoteModels[1].IsInstalled, Is.True);
                Assert.That(preferences.RemoteModels[1].Subtitle, Does.Contain("instalado"));
                Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.False, "An installed model is not downloaded again.");
                Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success));

                remote.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                preferences.SelectedRemoteModel = preferences.RemoteModels[2];
                Assert.That(preferences.SelectedRemoteModelCardUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/SlopCoder-Mongo-1.5B-full")));
                var second = preferences.DownloadModelCommand.ExecuteAsync(null);
                await WaitUntil(() => operations.ActiveOperations.Count == 1);
                operations.Cancel(operations.ActiveOperations[0].Id);
                await second;
                Assert.That(preferences.DownloadStatus, Does.Contain("cancelado"));
                Assert.That(preferences.RemoteModels[2].IsInstalled, Is.False);
                Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Cancelled));
                window.Close();
            }
            finally
            {
                synthetic.Dispose();
            }
            return true;
        }, CancellationToken.None);
    }

    private static void Render(AutocompleteSettingsWindow window, Button cancel)
    {
        var directory = UiEvidenceDirectory.Current();
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(520, 420), new Size(660, 680), new Size(900, 760) })
                foreach (var scale in new[] { 1d, 1.5, 2 })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    cancel.BringIntoView();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var right = cancel.TranslatePoint(new Point(cancel.Bounds.Width, 0), window)!.Value.X;
                    Assert.That(right, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), $"{size}: download actions overflow");
                    Assert.That(cancel.Bounds.Height, Is.GreaterThanOrEqualTo(28));
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"model-download-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"),
                        new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.That(condition(), Is.True, "Timed out waiting for the download state.");
    }
}

internal sealed class PublishedKapiCoderModels : IRemoteModelSource
{
    public IReadOnlyList<Uri> RepositoryUrls { get; } = [new("https://huggingface.co/esilva/KapiCoder-Mongo-Nano"), new("https://huggingface.co/esilva/KapiCoder-Mongo-Small")];
    public int DownloadCalls { get; private set; }

    public Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RemoteModelVariant>>(
    [
        Published("Nano"), Published("Small"),
        new("custom/export", new string('a', 40), "int4", "custom-int4", 100, null, new Uri("https://huggingface.co/custom/export"), [])
    ]);

    public Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken)
    {
        DownloadCalls++;
        throw new InvalidOperationException("Unexpected download");
    }

    private static RemoteModelVariant Published(string size)
    {
        var name = $"KapiCoder-Mongo-{size}";
        var url = new Uri($"https://huggingface.co/esilva/{name}");
        // Metadados públicos observados em 26/10/2026; nenhum peso é usado no teste de UI.
        var revision = size == "Nano" ? "c03bfe15ba1ff4e5b58a4b0953349eca82bb43f1" : "4ba57bc1f56a5b3cbe486dd314321ce4cdb2cae6";
        var bytes = size == "Nano" ? 988_097_824L : 3_087_467_144L;
        return new RemoteModelVariant($"esilva/{name}", revision, "safetensors", name + "-safetensors", bytes, "apache-2.0", url, [])
        { Family = name, BaseModelUrl = url, HintKey = "remoteHintRequiresOnnx", IsInstallable = false };
    }
}

internal sealed class ControlledRemoteModels : IRemoteModelSource
{
    public static readonly string[] Titles =
    [
        "SlopCoder-Mongo-0.5B — CPU INT4", "SlopCoder-Mongo-0.5B — GPU DirectML FP16",
        "SlopCoder-Mongo-1.5B-full — CPU INT8", "SlopCoder-Mongo-1.5B-full — GPU DirectML FP16"
    ];

    public IReadOnlyList<Uri> RepositoryUrls { get; } =
        [new("https://huggingface.co/esilva/SlopCoder-Mongo-0.5B-ONNX"), new("https://huggingface.co/esilva/SlopCoder-Mongo-1.5B-full-ONNX")];

    public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RemoteModelVariant>>(
        [
            Variant("0.5B", "int4", 415_000_000, "padrão em CPU: menor e mais rápido"),
            Variant("0.5B", "dml-fp16", 1_010_000_000, "padrão com GPU: melhor qualidade e menor latência"),
            Variant("1.5B-full", "int8", 2_700_000_000, "recomendado em CPU"),
            Variant("1.5B-full", "dml-fp16", 3_120_000_000, "recomendado com GPU")
        ]);

    public async Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new RemoteModelProgress(variant.SizeBytes / 2, variant.SizeBytes, "model.onnx.data"));
        await Gate.Task.WaitAsync(cancellationToken);
        // A structurally valid Qwen FIM export, so the catalog lists the installed folder.
        var path = Path.Combine(modelsDirectory, variant.FolderName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "genai_config.json"), """{"model":{"type":"qwen2","decoder":{"filename":"model.onnx"}}}""");
        File.WriteAllText(Path.Combine(path, "model.onnx"), "onnx");
        File.WriteAllText(Path.Combine(path, "tokenizer.json"), JsonSerializer.Serialize(new { added_tokens = QwenFimPromptBuilder.SpecialTokens.Select(token => new { content = token }) }));
        File.WriteAllText(Path.Combine(path, "tokenizer_config.json"), "{}");
        return path;
    }

    private static RemoteModelVariant Variant(string size, string folder, long bytes, string hint)
    {
        var repository = $"esilva/SlopCoder-Mongo-{size}-ONNX";
        return new RemoteModelVariant(repository, new string('a', 40), folder, $"SlopCoder-Mongo-{size}-ONNX-{folder}", bytes, "deepseek-license",
            new Uri($"https://huggingface.co/{repository}/tree/main/{folder}"), [])
        {
            Family = $"SlopCoder-Mongo-{size}", Hint = hint, BaseModelUrl = new Uri($"https://huggingface.co/esilva/SlopCoder-Mongo-{size}")
        };
    }
}
