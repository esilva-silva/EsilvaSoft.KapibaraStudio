using Avalonia;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Desktop;

[assembly: AvaloniaTestApplication(typeof(EsilvaSoft.KapibaraStudio.IntegrationTests.IntegrationUiTestApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

public static class IntegrationUiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}
