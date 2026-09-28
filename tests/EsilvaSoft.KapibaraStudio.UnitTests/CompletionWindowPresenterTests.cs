using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Desktop;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CompletionWindowPresenterTests
{
    private static readonly CompletionItem[] Initial = [Item("alpha"), Item("beta"), Item("gamma")];
    private static readonly CompletionItem[] Refreshed = [Item("gamma"), Item("beta"), Item("delta")];
    private static readonly CompletionItem[] Pair = [Item("alpha"), Item("beta")];
    private static readonly string[] BetaOnly = ["beta"];
    [Test]
    public void RefreshAndFilterPreserveSelectionBySymbolId()
    {
        var presenter = new CompletionWindowPresenter();
        presenter.Show(Initial);
        presenter.Select("beta");
        presenter.Show(Refreshed);
        Assert.That(presenter.Selected!.SymbolId, Is.EqualTo("beta"));
        presenter.SetFilter("be");
        Assert.That(presenter.Items.Select(item => item.SymbolId), Is.EqualTo(BetaOnly));
        Assert.That(presenter.Selected!.SymbolId, Is.EqualTo("beta"));
    }

    [Test]
    public void MovementIsBoundedAndCloseClearsVisibleState()
    {
        var presenter = new CompletionWindowPresenter();
        presenter.Show(Pair);
        Assert.That(presenter.Move(9)!.SymbolId, Is.EqualTo("beta"));
        Assert.That(presenter.Move(-9)!.SymbolId, Is.EqualTo("alpha"));
        presenter.Close();
        Assert.That(presenter.IsOpen, Is.False);
        Assert.That(presenter.Items, Is.Empty);
        Assert.That(presenter.Selected, Is.Null);
    }

    [Test]
    public void HundredRankedItemsRemainAvailableWithoutChangingTheirOrder()
    {
        var items = Enumerable.Range(0, 100).Select(index => Item($"item-{index:D3}")).ToArray();
        var presenter = new CompletionWindowPresenter();
        presenter.Show(items);
        Assert.That(presenter.Items, Has.Count.EqualTo(100));
        Assert.That(presenter.Items.Select(item => item.SymbolId), Is.EqualTo(items.Select(item => item.SymbolId)));
        Assert.That(presenter.Move(99)!.SymbolId, Is.EqualTo("item-099"));
    }

    private static CompletionItem Item(string label) => new(label, label, null, CompletionItemKind.Text,
        new(new TextSpan(0, 0), new TextSpan(0, 0), label), label, 0, CompletionSource.Catalog);
}
