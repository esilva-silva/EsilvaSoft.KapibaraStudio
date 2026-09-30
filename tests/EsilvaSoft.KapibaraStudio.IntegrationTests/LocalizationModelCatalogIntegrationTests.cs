using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalizationModelCatalogIntegrationTests
{
    [Test]
    public async Task LocalModelCatalogValidationUsesTheSelectedLanguage()
    {
        using var workspace = new SyntheticDirectory();
        var localization = new LocalizationViewModel { Language = "zh-CN" };
        var catalog = new LocalModelCatalog(Path.Combine(workspace.Path, "models"), fileAccess: new LocalModelFileAccess());
        catalog.SetLocalization(localization.Resolve);

        var validation = await catalog.ValidateAsync(Path.Combine(workspace.Path, "model-missing"));

        Assert.That(validation.Status.Message, Does.StartWith("模型未安装。"));
    }
}
