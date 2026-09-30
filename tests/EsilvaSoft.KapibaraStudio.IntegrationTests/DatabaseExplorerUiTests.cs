using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class DatabaseExplorerUiTests
{
    private static readonly string[] RemovedToolEntries =
        ["Criar coleção…", "Documentos: inserir / atualizar / excluir…", "Criar / remover índices…", "Administração do banco / coleção…"];

    [Test]
    public async Task ContextMenusGenerateScriptsWithoutExecutingAndRenderDocumentTree()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            context.Mongo.Handler = (method, _) => method switch
            {
                "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["loja"]),
                "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["clientes", "pedidos"]),
                "GetTopologyAsync" => Task.FromResult("{\"setName\":\"rs\",\"me\":\"mongo-a:27017\",\"primary\":\"mongo-a:27017\",\"hosts\":[\"mongo-a:27017\",\"mongo-b:27017\"]}"),
                "GetDatabaseStatsAsync" => Task.FromResult("{\"db\":\"loja\",\"collections\":2,\"dataSize\":4096}"),
                "GetCollectionStatsAsync" => Task.FromResult("{\"ns\":\"loja.clientes\",\"count\":12,\"size\":4096}"),
                "GetCollectionDefinitionAsync" => Task.FromResult("{\"name\":\"clientes\",\"type\":\"collection\",\"options\":{}}"),
                "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>(["{\"name\":\"_id_\",\"key\":{\"_id\":1}}", "{\"name\":\"email_1\",\"key\":{\"email\":1},\"unique\":true}"]),
                "QueryAsync" => Task.FromResult(new QueryPage(["{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"nome\":\"Ana\",\"endereco\":{\"cidade\":\"São Paulo\"},\"ativo\":true}"], TimeSpan.FromMilliseconds(8), false)),
                _ => throw new InvalidOperationException(method)
            };
            var profile = ConnectionProfile.Create("Desenvolvimento", "mongodb://mongo-a:27017,mongo-b:27017/loja?replicaSet=rs");
            await context.Repository.SaveAsync(profile);
            await context.Repository.SaveAsync(ConnectionProfile.Create("Produção", "mongodb://production/loja", isReadOnly: true));
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            await workspace.OpenConnectionAsync(profile);
            var root = workspace.Roots.Single(r => r.Profile.Id == profile.Id);
            var database = root.Children.Single(); await database.LoadAsync(); database.IsExpanded = true;
            var collection = database.Children[0]; await collection.LoadAsync(); collection.IsExpanded = true;
            var indexes = collection.Children.Single(n => n.Kind == ExplorerNodeKind.Indexes); await indexes.LoadAsync(); indexes.IsExpanded = true;
            workspace.SelectedNode = collection; await workspace.Details.SelectionTask;
            workspace.OpenCollection(collection);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var cell = window.FindControl<TreeView>("Explorer")!.GetVisualDescendants().OfType<Grid>().First(g => ReferenceEquals(g.DataContext, collection) && g.ContextMenu is not null);
            cell.ContextMenu!.Open(cell); Dispatcher.UIThread.RunJobs();
            var scripts = cell.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Gerar script CRUD");
            var find = scripts.Items.OfType<MenuItem>().Single(m => m.Tag as string == "Find");
            Assert.That(find.DataContext, Is.SameAs(collection));
            find.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(workspace.ActiveTab!.Text, Does.Contain("getCollection(\"clientes\")"));
            Assert.That(context.Scripts.Calls, Is.Empty);
            // A janela de Ferramentas saiu do menu do Explorer (docs/backlog/bkl-02-ferramentas-fora-de-fase.md);
            // o código permanece íntegro e é exercitado aqui sem ponto de entrada visual.
            foreach (var removed in RemovedToolEntries)
                Assert.That(cell.ContextMenu.Items.OfType<MenuItem>().Any(m => m.Header as string == removed), Is.False, removed);
            cell.ContextMenu.Close();
            var toolsModel = new MainWindowViewModel(workspace.Workspace, autoLoadCollections: false)
            { SelectedProfile = collection.Profile, SelectedDatabase = collection.Database, SelectedCollection = collection.Collection ?? "" };
            var toolsWindow = new WorkspaceToolsWindow { DataContext = toolsModel };
            toolsWindow.Show();
            toolsWindow.SelectSection("Índices");
            if (toolsModel.LoadProfilesCommand.ExecutionTask is { } profileLoading) await profileLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.That(toolsWindow.GetLogicalDescendants().OfType<TabControl>().Any(t => (t.SelectedItem as TabItem)?.Header as string == "Índices"), Is.True);
            Assert.That(toolsModel.SelectedDatabase, Is.EqualTo("loja"));
            Assert.That(toolsModel.SelectedCollection, Is.EqualTo("clientes"));
            toolsWindow.Close();
            workspace.OpenCollection(collection);
            await workspace.ActiveTab!.ExecuteCommand.ExecuteAsync(null); workspace.ActiveTab.ResultTabIndex = 3;
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"explorer-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        Assert.That(window.FindControl<TreeView>("Explorer")!.Bounds.Height, Is.GreaterThan(100));
                    }
            window.SetRenderScaling(1);
            using var editor = await workspace.ActiveTab.CreateDocumentMutationAsync("Editar");
            var dialog = new DocumentMutationWindow { DataContext = editor };
            var modal = dialog.ShowDialog(window);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = dialog.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"document-editor-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            dialog.Close(); await modal;
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }
}
