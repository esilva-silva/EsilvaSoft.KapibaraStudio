using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration"), Explicit("Acessa o barramento D-Bus de sessão e o Secret Service do usuário.")]
public sealed class LinuxSecretServiceIntegrationTests
{
    [Test]
    public async Task AvailabilityReadsDefaultCollectionWithoutCreatingOrUnlockingItems()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("O Secret Service está disponível somente em Linux.");
        }

        var store = new LinuxSecretServiceSecretStore();
        var result = await store.GetAvailabilityAsync();

        Assert.That(result.IsSuccess, Is.True, result.Failure?.Code.ToString());
        Assert.That(result.Value, Is.EqualTo(SecretStoreAvailability.Available));
    }
}
