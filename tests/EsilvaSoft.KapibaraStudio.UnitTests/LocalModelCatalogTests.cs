using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LocalModelCatalogTests
{
    [TestCase("..")]
    [TestCase("models/other")]
    [TestCase(@"models\other")]
    [TestCase(@"C:\models\other")]
    [TestCase(" padded")]
    public void SelectedModelIsAFolderNameThatCannotEscapeTheDirectory(string name)
    {
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { SelectedModel = name }.Validate());
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { ChatModel = name }.Validate());
    }

    [Test]
    public void SelectionResolvesInsideTheDirectoryAndChatCanUseItsOwnModel()
    {
        var settings = new AutocompleteSettings { ModelDirectory = "models", SelectedModel = "Coder-0.5B", ChatModel = "Coder-1.5B", ModelPath = "external" }.Validate();
        Assert.That(settings.ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("models", "Coder-0.5B")));
        Assert.That(settings.ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo(Path.Combine("models", "Coder-1.5B")));
        Assert.That((settings with { ModelDirectory = "" }).ResolveModelPath(LocalModelRole.Autocomplete, "default"), Is.EqualTo(Path.Combine("default", "Coder-0.5B")));
        Assert.That((settings with { SelectedModel = "", ChatModel = "" }).ResolveModelPath(LocalModelRole.Chat, "default"), Is.EqualTo("external"));
        Assert.That(new AutocompleteSettings().HasModelSelection(), Is.False);
    }

    [Test]
    public void UndeclaredContractResolvesToTheFrozenV1AndUnknownIdentifiersResolveToNothing()
    {
        Assert.That(LocalModelContextContracts.IsSupported(null), Is.True);
        Assert.That(LocalModelContextContracts.Resolve(null), Is.EqualTo(LocalModelContextContracts.EditorContextV1));
        Assert.That(LocalModelContextContracts.IsSupported("Editor-Context-V1"), Is.True);
        Assert.That(LocalModelContextContracts.Resolve("Editor-Context-V1"), Is.EqualTo(LocalModelContextContracts.EditorContextV1));
        Assert.That(LocalModelContextContracts.IsSupported("compact-facts-v1"), Is.False);
        Assert.That(LocalModelContextContracts.Resolve("compact-facts-v1"), Is.Null);
    }
}
