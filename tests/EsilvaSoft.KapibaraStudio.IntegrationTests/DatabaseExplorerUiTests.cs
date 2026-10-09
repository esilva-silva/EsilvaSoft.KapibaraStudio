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
            var moreActions = window.FindControl<Button>("MoreActionsButton")!;
            var databaseToolsEntry = ((MenuFlyout)moreActions.Flyout!).Items.OfType<MenuItem>()
                .Single(item => item.Name == "DatabaseToolsMenuItem");
            Assert.That(databaseToolsEntry.IsEnabled, Is.True, "The main-window entry is enabled for the connected active Mongo tab.");
            var cell = window.FindControl<TreeView>("Explorer")!.GetVisualDescendants().OfType<Grid>().First(g => ReferenceEquals(g.DataContext, collection) && g.ContextMenu is not null);
            cell.ContextMenu!.Open(cell); Dispatcher.UIThread.RunJobs();
            var scripts = cell.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Gerar script CRUD");
            var find = scripts.Items.OfType<MenuItem>().Single(m => m.Tag as string == "Find");
            Assert.That(find.DataContext, Is.SameAs(collection));
            find.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(workspace.ActiveTab!.Text, Does.Contain("getCollection(\"clientes\")"));
            Assert.That(context.Scripts.Calls, Is.Empty);
            // Ferramentas está no menu global Mais ações; atalhos administrativos continuam fora do Explorer.
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
            var toolsEvidence = UiEvidenceDirectory.Current(); Directory.CreateDirectory(toolsEvidence);
            toolsModel.ImportSourceDirectory = Path.Combine(Path.GetTempPath(), "sample-mflix-validation-package");
            toolsModel.ImportUseUpsert = true;
            toolsModel.ImportRestoreDefinitions = true;
            toolsModel.IsExportInProgress = true;
            toolsModel.ExportProgress = LocalizationViewModel.Current.Format("databaseExportProgress",
                LocalizationViewModel.Current.Resolve("exportStageRunning"), 1, 3, "pedidos", 128, 428);
            toolsModel.ExportProgressValue = 33;
            toolsModel.ImportDefinitionPreview = "Ações planejadas; a importação revalida o pacote e o destino antes de escrever.\n"
                + "Opções de coleção: orders — planejado — sem colisão conhecida\n"
                + "Índice: orders / customer_1 — planejado — sem colisão conhecida — depende de orders\n"
                + "View: recent — bloqueado — conflito — depende de orders";
            toolsModel.ImportConfirmation = "loja";
            toolsModel.StandaloneSourceFile = Path.Combine(Path.GetTempPath(), "sample-items.csv");
            toolsModel.StandaloneTargetCollection = "items_preview";
            toolsModel.StandaloneUseCsv = true;
            toolsModel.StandaloneMappingJson =
                "[{\"SourceColumn\":\"id\",\"TargetField\":\"_id\",\"Type\":\"Integer64\"},"
                + "{\"SourceColumn\":\"name\",\"TargetField\":\"name\",\"Type\":\"Text\"}]";
            toolsModel.StandalonePreviewText = "id → _id: Integer64\nname → name: Text";
            toolsModel.StandaloneImportResults = "Linha 2, coluna id: valor incompatível com Integer64.";
            var recoveryReceipt = new ImportCheckpoint(ImportCheckpoint.CurrentVersion, Guid.NewGuid(),
                ImportCheckpointKind.StandaloneFile, collection.Profile.Id, collection.Profile.SourceGenerationId,
                ImportCheckpointRecovery.Sha256OfText("path"), ImportCheckpointRecovery.Sha256OfText("source"),
                ImportCheckpointRecovery.Sha256OfText("plan"), "loja", "items_preview", 2, 10, 2, 0, 0,
                ImportCheckpointState.NeedsReview, DateTimeOffset.UtcNow);
            toolsModel.PendingImportCheckpoints.Add(new MainWindowViewModel.ImportCheckpointChoice(recoveryReceipt,
                "Arquivo avulso · loja.items_preview · revisão necessária · 2/10 documentos"));
            toolsModel.SelectedImportCheckpoint = toolsModel.PendingImportCheckpoints.Single();
            toolsModel.ImportRecoveryStatus = LocalizationViewModel.Current.Resolve("importRecoveryReselect");
            foreach (var (section, artifact) in new[]
                     {
                         ("Transferir", "tools-database-transfer"),
                         ("Índices", "tools-index-diagnostics"),
                         ("Coleções", "tools-view-materialization"),
                         ("Administração", "tools-admin-runtime-parameter")
                     })
            {
                toolsWindow.SelectSection(section);
                if (section == "Transferir")
                {
                    var standalone = toolsWindow.GetLogicalDescendants().OfType<Expander>()
                        .First(expander => expander.IsVisible
                            && expander.Header as string == LocalizationViewModel.Current.Resolve("standaloneImportTitle"));
                    standalone.IsExpanded = true;
                    var recovery = toolsWindow.GetLogicalDescendants().OfType<Expander>()
                        .First(expander => expander.IsVisible
                            && expander.Header as string == LocalizationViewModel.Current.Resolve("importRecoveryTitle"));
                    recovery.IsExpanded = true;
                }
                if (section == "Coleções")
                {
                    var advanced = toolsWindow.GetLogicalDescendants().OfType<Expander>()
                        .First(expander => expander.IsVisible
                            && expander.Header as string == "Gerenciar coleções e validação (avançado)");
                    advanced.IsExpanded = true;
                }
                if (section == "Administração")
                {
                    var profiler = toolsWindow.GetLogicalDescendants().OfType<Expander>()
                        .First(expander => expander.IsVisible && expander.Header as string == "Configurar profiler");
                    profiler.IsExpanded = true;
                }
                var outerScroll = toolsWindow.GetLogicalDescendants().OfType<ScrollViewer>()
                    .Single(scroll => scroll.Content is TabControl);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    if (section == "Administração")
                    {
                        var runtimeParameterPicker = toolsWindow.GetVisualDescendants().OfType<ComboBox>()
                            .Single(combo => combo.ItemsSource is IEnumerable<string> items
                                && items.Contains(RuntimeServerParameters.MaxLogSizeKb, StringComparer.Ordinal));
                        runtimeParameterPicker.BringIntoView();
                        toolsWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(outerScroll.Extent.Height, Is.GreaterThan(outerScroll.Viewport.Height),
                            "Administração precisa oferecer rolagem até os parâmetros runtime.");
                        outerScroll.Offset = new Avalonia.Vector(0,
                            Math.Min(180, outerScroll.Extent.Height - outerScroll.Viewport.Height));
                        toolsWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        using var runtimeParameters = toolsWindow.CaptureRenderedFrame();
                        Assert.That(runtimeParameters, Is.Not.Null, "Administração/runtime parameters focused frame");
                        runtimeParameters!.Save(Path.Combine(toolsEvidence,
                            $"tools-admin-runtime-parameter-focus-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        outerScroll.Offset = new Avalonia.Vector(0, 0);
                    }
                    toolsWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    outerScroll.Offset = new Avalonia.Vector(0, 0);
                    toolsWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    using var frame = toolsWindow.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null, $"{section}/{theme}: frame");
                    frame!.Save(Path.Combine(toolsEvidence, $"{artifact}-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    if (section is "Administração" or "Transferir")
                    {
                        outerScroll.Offset = new Avalonia.Vector(0, Math.Max(0, outerScroll.Extent.Height - outerScroll.Viewport.Height));
                        toolsWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        using var scrolled = toolsWindow.CaptureRenderedFrame();
                        Assert.That(scrolled, Is.Not.Null, $"{section}/{theme}: scrolled frame");
                        scrolled!.Save(Path.Combine(toolsEvidence, $"{artifact}-{theme}-scrolled.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        outerScroll.Offset = new Avalonia.Vector(0, 0);
                    }
                }
            }
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
