using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Funcionalidades fora da fase atual continuam implementadas, mas sem entrada na interface.
/// Ver docs/backlog/bkl-01 a bkl-04 e docs/phases/phase-02-v0.6.0.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class DisabledFeatureEntryPointsTests
{
    private static readonly string[] SelectableModes = ["Console", "Texto"];
    private static readonly string[] DisabledModes = ["Script", "Agregação"];

    [Test]
    public void OnlyConsoleIsOfferedByTheInterfaceWhileEveryModeStaysUnderstood()
    {
        using var context = new WorkspaceTestContext();
        var tab = new WorkspaceTabViewModel(context.Workspace);
        Assert.That(tab.SelectableModes, Is.EqualTo(SelectableModes));
        Assert.That(tab.Mode, Is.EqualTo("Console"));
        Assert.That(tab.Modes, Does.Contain("Script").And.Contain("Agregação"));
    }

    [Test]
    public void DraftsSavedInADisabledModeReopenExactlyAsTheyWereStored()
    {
        using var context = new WorkspaceTestContext();
        foreach (var mode in DisabledModes)
        {
            var tab = new WorkspaceTabViewModel(context.Workspace);
            tab.Restore(new WorkspaceDraft { Database = "shop", Collection = "orders", Mode = mode, Text = "[]" }, null);
            Assert.That(tab.Mode, Is.EqualTo(mode), mode);
            Assert.That(tab.Text, Is.EqualTo("[]"), mode);
        }
    }

    [Test]
    public async Task TheEditorDoesNotShowAModeSelector()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            await window.InitializationTask;
            vm.NewTabCommand.Execute(null);
            window.UpdateLayout();
            Assert.That(window.GetVisualDescendants().OfType<ComboBox>()
                .Any(c => AutomationProperties.GetName(c) == "Modo do editor"), Is.False);
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TheTopBarDoesNotOfferToolsOrEnvironments()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Dev", "mongodb://localhost:27017");
            await context.Repository.SaveAsync(profile);
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            await window.InitializationTask;
            window.UpdateLayout();
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.That(buttons.Any(b => AutomationProperties.GetName(b) == "Ferramentas"), Is.False);
            Assert.That(buttons.Any(b => Equals(b.Content, "Ambientes")), Is.False);
            // Os comandos da fase atual continuam presentes.
            Assert.That(buttons.Any(b => AutomationProperties.GetName(b) == "Conexões"), Is.True);
            Assert.That(buttons.Any(b => AutomationProperties.GetName(b) == "Nova aba"), Is.True);
            Assert.That(buttons.Any(b => AutomationProperties.GetName(b) == "Mais ações"), Is.True);
            return true;
        }, CancellationToken.None);
    }
}
