using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ApplicationLanguageTests
{
    [Test]
    public void CatalogExposesTheFourSupportedLanguagesAndKeepsPtBrAsInitialLanguage()
    {
        Assert.That(ApplicationLanguages.All.Select(language => language.Code), Is.EqualTo(["pt-BR", "en", "es", "zh-CN"]));
        Assert.That(ApplicationLanguages.DefaultCode, Is.EqualTo("pt-BR"));
        Assert.That(new WorkspacePreferences().Language, Is.EqualTo("pt-BR"));
    }

    [TestCase("pt-br", "pt-BR")]
    [TestCase("EN", "en")]
    [TestCase(" es ", "es")]
    [TestCase("zh-cn", "zh-CN")]
    public void NormalizesSupportedLanguageCodes(string input, string expected)
    {
        Assert.That(ApplicationLanguages.Normalize(input), Is.EqualTo(expected));
        Assert.That(ApplicationLanguages.IsSupported(input), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("fr")]
    [TestCase("pt")]
    [TestCase("zh-TW")]
    public void InvalidOrMissingLanguageUsesEnglishFallback(string? input)
    {
        Assert.That(ApplicationLanguages.Normalize(input), Is.EqualTo(ApplicationLanguages.FallbackCode));
        Assert.That(ApplicationLanguages.IsSupported(input), Is.False);
    }

    [Test]
    public void LanguageIsAdditiveAndReadableFromLegacySessionJson()
    {
        var document = JsonSerializer.SerializeToNode(new WorkspaceSession())!.AsObject();
        document[nameof(WorkspaceSession.Preferences)]!.AsObject().Remove(nameof(WorkspacePreferences.Language));
        var json = document.ToJsonString();
        Assert.That(json, Does.Not.Contain("\"Language\""));

        var restored = JsonSerializer.Deserialize<WorkspaceSession>(json);
        Assert.That(restored!.Preferences.Language, Is.EqualTo(ApplicationLanguages.DefaultCode));
    }

    [Test]
    public async Task WorkspacePersistsSelectedLanguageWithoutChangingDraftPolicy()
    {
        using var context = new WorkspaceTestContext();
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        try
        {
            await workspace.InitializeAsync();
            workspace.Language = "zh-CN";
            workspace.ActiveTab!.Text = "const draft = 1;";
            await workspace.SaveSessionAsync();

            var restored = await context.Repository.LoadSessionAsync();
            Assert.Multiple(() =>
            {
                Assert.That(restored.Preferences.Language, Is.EqualTo("zh-CN"));
                Assert.That(restored.Preferences.RecoverDrafts, Is.True);
                Assert.That(restored.Tabs.Single().Text, Is.EqualTo("const draft = 1;"));
            });
        }
        finally
        {
            LocalizationViewModel.Current.Language = ApplicationLanguages.DefaultCode;
        }
    }
}
