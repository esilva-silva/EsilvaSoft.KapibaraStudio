using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalizationModelCatalogIntegrationTests
{
    [TestCase("pt-BR", "Padrão em CPU: INT4.", "Alternativa em CPU: INT8.", "GPU DirectML experimental: FP16.")]
    [TestCase("en", "Default on CPU: INT4.", "CPU alternative: INT8.", "Experimental DirectML GPU: FP16.")]
    [TestCase("es", "Predeterminado en CPU: INT4.", "Alternativa en CPU: INT8.", "GPU DirectML experimental: FP16.")]
    [TestCase("zh-CN", "CPU 默认：INT4。", "CPU 备选：INT8。", "实验性 DirectML GPU：FP16。")]
    public void OnnxExportHintsUseNeutralLocalizedDescriptions(string language, string cpuDefault, string cpuAlternative, string gpuExperimental)
    {
        var localization = new LocalizationViewModel { Language = language };
        Assert.That(localization.Resolve("remoteHintCpuInt4Default"), Is.EqualTo(cpuDefault));
        Assert.That(localization.Resolve("remoteHintCpuInt8Alternative"), Is.EqualTo(cpuAlternative));
        Assert.That(localization.Resolve("remoteHintDirectMlFp16"), Is.EqualTo(gpuExperimental));
    }

    [TestCase("pt-BR", "requer uma exportação ONNX")]
    [TestCase("en", "ONNX GenAI export is required")]
    [TestCase("es", "requiere una exportación ONNX")]
    [TestCase("zh-CN", "ONNX GenAI")]
    public void PublishedSafetensorsExplainTheInstallationRequirement(string language, string expected)
    {
        var localization = new LocalizationViewModel { Language = language };
        Assert.That(localization.Resolve("remoteHintRequiresOnnx"), Does.Contain(expected));
        Assert.That(localization.Resolve("remoteModelLicense"), Does.Contain("{0}"));
        Assert.That(localization.Resolve("remoteSourceNotice"), Does.Contain("{0}").And.Contain("ONNX GenAI").And.Not.Contain("SlopCoder"));
    }

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
