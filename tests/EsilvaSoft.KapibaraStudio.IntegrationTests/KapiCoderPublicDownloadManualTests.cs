using System.Security.Cryptography;
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
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Manual public Hub download through the same command and storage as Preferences.</summary>
[TestFixture, NonParallelizable, Category("Integration"), Category("LocalModelIntegration")]
public sealed class KapiCoderPublicDownloadManualTests
{
    [Test, Explicit("Baixa pesos públicos reais; requer KAPI_PUBLIC_ONNX_DOWNLOAD_ROOT vazio e espaço livre."), CancelAfter(1_800_000)]
    public async Task PublicCatalogDownloadsNanoAndSmallThroughPreferences()
    {
        var configured = Environment.GetEnvironmentVariable("KAPI_PUBLIC_ONNX_DOWNLOAD_ROOT");
        Assert.That(configured, Is.Not.Null.And.Not.Empty, "Defina KAPI_PUBLIC_ONNX_DOWNLOAD_ROOT para um diretório vazio dedicado.");
        var root = Path.GetFullPath(configured!);
        Assert.That(!File.Exists(root), Is.True, "O destino não pode ser um arquivo.");
        if (Directory.Exists(root))
            Assert.That(Directory.EnumerateFileSystemEntries(root), Is.Empty, "O destino deve estar vazio; nada será sobrescrito ou removido.");
        else Directory.CreateDirectory(root);

        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var remote = new HuggingFaceModelSource(new RemoteModelStorage());
            var catalog = new LocalModelCatalog(root, fileAccess: new LocalModelFileAccess());
            var operations = new ApplicationOperationService();
            var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), catalog, _ => Task.CompletedTask,
                remote: remote, operations: operations, directories: new LocalModelDirectoryService());
            using var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => preferences.DownloadModelCancelCommand.Execute(null)));
            preferences.Load(new AutocompleteSettings { ModelDirectory = root });
            var previousLanguage = LocalizationViewModel.Current.Language;
            var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            var window = new AutocompleteSettingsWindow { DataContext = preferences, Width = 900, Height = 760 };
            try
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                await preferences.LoadRemoteModelsCommand.ExecuteAsync(null);
                Assert.That(preferences.RemoteModels, Is.Not.Empty, preferences.RemoteModelsPlaceholder);
                Assert.That(preferences.SelectedRemoteModel?.Variant.Repository, Is.EqualTo("esilva/KapiCoder-Mongo-Nano-ONNX"));
                Assert.That(preferences.SelectedRemoteModel?.Variant.Variant, Is.EqualTo("cpu-int4"));
                window.Show();

                foreach (var (repository, variantName) in new[]
                {
                    ("esilva/KapiCoder-Mongo-Nano-ONNX", "cpu-int4"),
                    ("esilva/KapiCoder-Mongo-Small-ONNX", "cpu-int8")
                })
                {
                    var option = preferences.RemoteModels.Single(item => item.Variant.Repository == repository && item.Variant.Variant == variantName);
                    var variant = option.Variant;
                    preferences.SelectedRemoteModel = option;
                    Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.True, $"{repository}/{variantName}");
                    TestContext.Progress.WriteLine($"Baixando {repository}/{variantName}; revisão {variant.Revision}; {variant.SizeBytes} bytes.");
                    await preferences.DownloadModelCommand.ExecuteAsync(null);
                    Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success), preferences.DownloadStatus);
                    var installed = Path.Combine(root, variant.FolderName);
                    Assert.That(Directory.Exists(installed), Is.True, preferences.DownloadStatus);
                    var validation = await catalog.ValidateAsync(installed);
                    Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Valid), validation.Status.Message);
                    Assert.That(preferences.RemoteModels.Single(item => item.Variant.Repository == repository && item.Variant.Variant == variantName).IsInstalled, Is.True);
                    Assert.That(preferences.SelectedModelOption?.Reference, Is.EqualTo(variant.FolderName));
                    await VerifyManifestAsync(installed);
                    TestContext.Progress.WriteLine($"Validado {installed}; revisão {variant.Revision}; {variant.SizeBytes} bytes.");
                }

                var button = window.GetVisualDescendants().OfType<Button>().Single(item => AutomationProperties.GetName(item) == "Baixar modelo");
                var evidence = UiEvidenceDirectory.Current();
                Directory.CreateDirectory(evidence);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    button.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.That(button.IsEffectivelyEnabled, Is.False);
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(evidence, $"kapicoder-public-installed-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
            }
            finally
            {
                window.Close();
                LocalizationViewModel.Current.Language = previousLanguage;
                Avalonia.Application.Current!.RequestedThemeVariant = previousTheme;
            }
            return true;
        }, cancellationToken);
    }

    private static async Task VerifyManifestAsync(string directory)
    {
        await using var stream = File.OpenRead(Path.Combine(directory, "kapicoder_manifest.json"));
        using var manifest = await JsonDocument.ParseAsync(stream);
        Assert.That(manifest.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-kapicoder-manifest-v1"));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = item.GetProperty("path").GetString()!;
            Assert.That(relative.Split('/').All(part => part.Length > 0 && part is not "." and not ".." && !part.Contains('\\') && !part.Contains(':')), Is.True, relative);
            Assert.That(names.Add(relative), Is.True, $"Caminho duplicado: {relative}");
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, relative);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(item.GetProperty("bytes").GetInt64()), relative);
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
            Assert.That(hash, Is.EqualTo(item.GetProperty("sha256").GetString()).IgnoreCase, relative);
        }
        Assert.That(names, Is.Not.Empty);
    }
}
