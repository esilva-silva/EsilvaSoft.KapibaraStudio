using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration"), Explicit("Acessa o Windows Credential Manager da sessão atual.")]
public sealed class WindowsCredentialSecretStoreIntegrationTests
{
    [Test]
    public async Task WindowsCredentialManagerRoundTripUsesUniqueTargetAndCleansItInFinally()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("A prova integrada usa o Windows Credential Manager do usuário atual.");
        }

        var store = new WindowsCredentialSecretStore();
        var availability = await store.GetAvailabilityAsync();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
        {
            Assert.Ignore("O Credential Manager não está disponível para esta sessão de logon; nenhum target foi criado.");
        }

        var reference = new SecretReference(Guid.NewGuid());
        const string syntheticSecret = "kapibarastudio-contract-test-only";
        var written = await store.SetAsync(reference, syntheticSecret);
        if (!written.IsSuccess && written.Failure?.Code is SecretStoreFailureCode.Unavailable or SecretStoreFailureCode.Denied)
        {
            Assert.Ignore("A sessão respondeu ao probe de leitura, mas não permite gravar no Credential Manager.");
        }

        Assert.That(written.IsSuccess, Is.True, "Gravação sintética no target exclusivo falhou.");
        try
        {
            var read = await store.GetAsync(reference);
            Assert.That(read.IsSuccess, Is.True);
            Assert.That(read.Value, Is.EqualTo(syntheticSecret));

            var deleted = await store.DeleteAsync(reference);
            Assert.That(deleted.IsSuccess, Is.True);
        }
        finally
        {
            var cleanup = await store.DeleteAsync(reference);
            Assert.That(cleanup.IsSuccess || cleanup.Failure?.Code == SecretStoreFailureCode.NotFound, Is.True,
                "Não foi possível confirmar a remoção do target exclusivo.");
        }
    }
}
