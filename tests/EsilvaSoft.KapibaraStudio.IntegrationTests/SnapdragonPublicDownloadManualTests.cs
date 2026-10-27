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

[TestFixture, NonParallelizable, Category("Integration"), Category("LocalModelIntegration")]
public sealed class SnapdragonPublicDownloadManualTests
{
    [Test, Explicit("Baixa modelos Snapdragon reais; exige diretório dedicado vazio e rede."), CancelAfter(1_800_000)]
    public async Task PublicPackagesDownloadThroughPreferencesWithoutChangingSelectedModel()
    {
        var configured = Environment.GetEnvironmentVariable("KAPI_SNAPDRAGON_DOWNLOAD_ROOT");
        Assert.That(configured, Is.Not.Null.And.Not.Empty);
        var root = Path.GetFullPath(configured!);
        if (Directory.Exists(root)) Assert.That(Directory.EnumerateFileSystemEntries(root), Is.Empty);
        else Directory.CreateDirectory(root);
        var token = TestContext.CurrentContext.CancellationToken;
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var remote = new HuggingFaceModelSource(new RemoteModelStorage());
            var catalog = new LocalModelCatalog(root, fileAccess: new LocalModelFileAccess());
            var operations = new ApplicationOperationService();
            var preferences = new AutocompleteSettingsViewModel(new AutocompleteService(), catalog, _ => Task.CompletedTask,
                remote: remote, operations: operations, directories: new LocalModelDirectoryService());
            using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => preferences.DownloadModelCancelCommand.Execute(null)));
            preferences.Load(new AutocompleteSettings { ModelDirectory = root, SelectedModel = "KapiCoder-Mongo-Nano-ONNX-cpu-int4" });
            var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            var window = new AutocompleteSettingsWindow { DataContext = preferences, Width = 900, Height = 760 };
            window.Show();
            try
            {
            await preferences.LoadRemoteModelsCommand.ExecuteAsync(null);
            Assert.That(preferences.SelectedRemoteModel?.Variant.Variant, Is.EqualTo("cpu-int4"));
            foreach (var (family, revision) in new[]
            {
                ("Nano", "a85d11c9f8db2f4388836c991b292b1cdb160d39"),
                ("Small", "c03ba2339ba263a3881134d0acd3ee24e1a80717")
            })
            {
                var repository = $"esilva/KapiCoder-Mongo-{family}-ONNX";
                var option = preferences.RemoteModels.Single(item => item.Variant.Repository == repository && item.Variant.Variant == "htp-fim-exact-v1");
                Assert.That(option.Variant.Revision, Is.EqualTo(revision));
                preferences.SelectedRemoteModel = option;
                Assert.That(preferences.DownloadModelCommand.CanExecute(null), Is.True);
                await preferences.DownloadModelCommand.ExecuteAsync(null);
                Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success), preferences.DownloadStatus);
                Assert.That(preferences.SelectedModelOption?.Reference, Is.EqualTo("KapiCoder-Mongo-Nano-ONNX-cpu-int4"));
                var path = Path.Combine(root, option.Variant.FolderName);
                var validation = await catalog.ValidateAsync(path);
                Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Unsupported), validation.Status.Message);
                await VerifyManifestAsync(path);
                TestContext.Progress.WriteLine($"{family}: download verificado em {path}; revisão {revision}.");
            }
            var evidence = UiEvidenceDirectory.Current();
            Directory.CreateDirectory(evidence);
            var download = window.GetVisualDescendants().OfType<Button>().Single(item => AutomationProperties.GetName(item) == "Baixar modelo");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(520, 420), new Size(660, 680), new Size(900, 760) })
            foreach (var scale in new[] { 1d, 1.5, 2 })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                download.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(evidence, $"snapdragon-download-pending-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            }
            finally { window.Close(); Avalonia.Application.Current!.RequestedThemeVariant = previousTheme; }
            return true;
        }, token);
    }

    private static async Task VerifyManifestAsync(string directory)
    {
        await using var stream = File.OpenRead(Path.Combine(directory, "export_manifest.json"));
        using var manifest = await JsonDocument.ParseAsync(stream);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = item.GetProperty("path").GetString()!;
            Assert.That(relative.Split('/').All(part => part.Length > 0 && part is not "." and not ".." && !part.Contains('\\') && !part.Contains(':')), Is.True);
            Assert.That(names.Add(relative), Is.True);
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(item.GetProperty("bytes").GetInt64()), relative);
            await using var file = File.OpenRead(path);
            Assert.That(Convert.ToHexStringLower(await SHA256.HashDataAsync(file)), Is.EqualTo(item.GetProperty("sha256").GetString()).IgnoreCase, relative);
        }
        Assert.That(names.Count, Is.GreaterThan(0));
    }
}
