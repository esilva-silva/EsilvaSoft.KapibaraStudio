using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class CopilotErrorLocalizationTests
{
    private static readonly string[] ErrorCodes =
    [
        "CopilotAuthenticationFailed", "CopilotAccessDenied", "CopilotQuotaExceeded", "CopilotRateLimited",
        "CopilotContextLimitExceeded", "CopilotQueryFailed", "CopilotRequestTimedOut", "CopilotProviderFailure",
    ];

    [TestCase("pt-BR", "falha de autenticação", "O app não repete o pedido automaticamente.")]
    [TestCase("en", "authentication failure", "This app does not retry it automatically.")]
    [TestCase("es", "fallo de autenticación", "La aplicación no repite la solicitud automáticamente.")]
    [TestCase("zh-CN", "身份验证失败", "此应用不会自动重试请求。")]
    public void FixedCopilotErrorsHaveTranslationsAndTimeoutPreservesRetryWarning(
        string language, string authenticationFragment, string timeoutFragment)
    {
        var localization = new LocalizationViewModel { Language = language };
        foreach (var code in ErrorCodes)
        {
            var key = "agentError." + code;
            Assert.That(localization.HasTranslation(key), Is.True, key);
            Assert.That(localization.Resolve(key), Does.Not.Contain("[["), key);
        }
        Assert.That(localization.Resolve("agentError.CopilotAuthenticationFailed"), Does.Contain(authenticationFragment));
        Assert.That(localization.Resolve("agentError.CopilotRequestTimedOut"), Does.Contain(timeoutFragment));
    }
}
