using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class EditorRequestScopeTests
{
    [Test]
    public void BeginningANewRequestCancelsOnlyThePreviousRequestInThisScope()
    {
        using var scope = new EditorRequestScope();
        var first = Request(1, new(1, 1));
        var second = Request(2, new(1, 2));
        var firstLease = scope.Begin(first);
        var secondLease = scope.Begin(second);

        Assert.Multiple(() =>
        {
            Assert.That(firstLease.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(firstLease.IsCurrent, Is.False);
            Assert.That(secondLease.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(secondLease.IsCurrent, Is.True);
        });
    }

    [Test]
    public void AProviderResponseFromAnOldVersionIsRejectedEvenIfCancellationWasIgnored()
    {
        using var scope = new EditorRequestScope();
        var first = Request(1, new(4, 1));
        var second = Request(2, new(4, 2));
        scope.Begin(first);
        scope.Begin(second);

        Assert.That(scope.IsCurrent(first), Is.False);
        Assert.That(scope.IsCurrent(second), Is.True);
    }

    private static CompletionRequest Request(long id, TextSnapshotVersion version) => new(
        new(version, EditorDialects.Mql, SymbolKinds.Keyword, "", new(0, 0)), id, 0);
}
