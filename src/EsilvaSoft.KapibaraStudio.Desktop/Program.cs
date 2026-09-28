using Avalonia;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.Desktop;

internal static class Program
{
    /// <summary>Set by the main window when the user chose to restart into a downloaded update.</summary>
    internal static bool RestartAfterExit { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        GitHubAppUpdateService.CleanupAfterStart();
        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        // The lifetime has ended and the service provider (LiteDB included) is disposed: files can be swapped now.
        GitHubAppUpdateService.ApplyPendingOnExit(RestartAfterExit);
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
