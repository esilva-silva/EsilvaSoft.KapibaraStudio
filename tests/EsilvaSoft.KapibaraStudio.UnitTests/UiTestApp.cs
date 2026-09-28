using Avalonia;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Desktop;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}
