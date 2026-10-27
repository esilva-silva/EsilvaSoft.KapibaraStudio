using Avalonia.Input;
using EsilvaSoft.KapibaraStudio.Desktop;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Consulta o layout real do Windows; o resultado depende do teclado do host, por isso é manual.</summary>
[TestFixture, Category("Integration")]
public sealed class KeyboardLayoutSymbolManualTests
{
    [Test, Explicit("Depende do layout de teclado do host; com ABNT2, a posição física do '/' americano produz ';'.")]
    public void RealWindowsLayoutReportsWhatEachPunctuationKeyProduces()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Só o Windows consulta o layout.");
        foreach (var key in new[] { PhysicalKey.Slash, PhysicalKey.Semicolon, PhysicalKey.Period, PhysicalKey.Comma, PhysicalKey.IntlRo })
            TestContext.Out.WriteLine($"{key}: '{KeyboardLayoutSymbol.Platform(key, shift: false)}' / shift '{KeyboardLayoutSymbol.Platform(key, shift: true)}'");
        Assert.That(KeyboardLayoutSymbol.Platform(PhysicalKey.Period, shift: false), Is.EqualTo('.'));
    }
}
