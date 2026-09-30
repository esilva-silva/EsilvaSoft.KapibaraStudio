using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class AppSystemAdapterTests
{
    [Test]
    public void DiagnosticResolverFindsTheNearestCheckoutWithoutCreatingLogs()
    {
        using var workspace = new SyntheticDirectory();
        File.WriteAllText(Path.Combine(workspace.Path, "EsilvaSoft.KapibaraStudio.slnx"), "synthetic marker");
        var nested = Path.Combine(workspace.Path, "nested-checkout");
        var application = Path.Combine(nested, "bin", "Debug");
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(nested, "EsilvaSoft.KapibaraStudio.slnx"), "synthetic marker");

        var destination = new LocalDiagnosticLogDirectoryResolver(application).Resolve();

        Assert.That(destination, Is.EqualTo(Path.Combine(nested, "logs")));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [Test]
    public void DiagnosticResolverFallsBackToTheApplicationDirectoryOutsideACheckout()
    {
        using var workspace = new SyntheticDirectory();

        var destination = new LocalDiagnosticLogDirectoryResolver(workspace.Path).Resolve();

        Assert.That(destination, Is.EqualTo(Path.Combine(workspace.Path, "logs")));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [TestCase("file:///tmp/not-a-web-destination")]
    [TestCase("javascript:alert(1)")]
    [TestCase("relative/path")]
    public void BrowserRefusesNonWebDestinationsWithoutLaunching(string address)
        => Assert.ThrowsAsync<ArgumentException>(() => new LocalExternalUriLauncher().OpenAsync(new Uri(address, UriKind.RelativeOrAbsolute)));

    [Test]
    public void CancelledBrowserSubmissionNeverStartsTheNativeProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => new LocalExternalUriLauncher().OpenAsync(new Uri("https://example.com"), cancellation.Token));
    }

    [Test]
    [Explicit("Abre uma página pública no navegador padrão; exige desktop interativo e inspeção manual.")]
    [Category("NativeDesktop")]
    public Task PublicWebPageCanBeSubmittedToTheNativeBrowser()
        => new LocalExternalUriLauncher().OpenAsync(new Uri("https://example.com"));
}
