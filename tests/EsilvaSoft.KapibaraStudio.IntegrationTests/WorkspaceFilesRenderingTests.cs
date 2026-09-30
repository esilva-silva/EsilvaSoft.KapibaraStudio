using EsilvaSoft.KapibaraStudio.SystemAdapters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class WorkspaceFilesRenderingTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    [Test]
    public async Task FilesPanelRendersAtAllSupportedThemesSizesAndScales()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var synthetic = new SyntheticDirectory();
            var fixture = synthetic.Path;
            Directory.CreateDirectory(Path.Combine(fixture, "consultas"));
            await File.WriteAllTextAsync(Path.Combine(fixture, "leia-me.txt"), "Workspace de arquivos\r\nTexto independente de conexão.\n");
            try
            {
                var textFiles = new MemoryTextFiles();
                await textFiles.SaveAsync(Path.Combine(fixture, "leia-me.txt"), "Workspace de arquivos\r\nTexto independente de conexão.\n");
                using var context = new WorkspaceTestContext(textFiles: textFiles);
                using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, workspaceFiles: new LocalWorkspaceFileService());
                var window = new MainWindow { DataContext = vm };
                window.Show();
                await window.InitializationTask;
                await vm.SetWorkspaceFolderAsync(fixture);
                await vm.OpenTextFileAsync(Path.Combine(fixture, "leia-me.txt"));
                var output = UiEvidenceDirectory.Current();
                Directory.CreateDirectory(output);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                        foreach (var scale in Scales)
                        {
                            Avalonia.Application.Current!.RequestedThemeVariant = theme;
                            window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                            var panel = window.FindControl<Border>("FilesPanel")!;
                            Assert.That(panel.IsVisible, Is.True);
                            Assert.That(window.FindControl<Border>("ConnectionsPanel")!.IsVisible, Is.False);
                            Assert.That(window.FindControl<TreeView>("WorkspaceTree")!.Bounds.Height, Is.GreaterThan(200));
                            using var frame = window.CaptureRenderedFrame();
                            Assert.That(frame, Is.Not.Null);
                            frame!.Save(Path.Combine(output, $"workspace-files-{theme}-{size.Width}x{size.Height}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        }
                window.SetRenderScaling(1);
                window.DataContext = null;
                window.Close();
            }
            finally { synthetic.Dispose(); }
            return true;
        }, CancellationToken.None);
    }
}
