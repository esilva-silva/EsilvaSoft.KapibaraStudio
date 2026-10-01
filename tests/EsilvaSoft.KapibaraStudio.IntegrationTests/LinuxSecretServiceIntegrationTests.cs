using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration"), Explicit("Acessa o barramento D-Bus de sessão e o Secret Service do usuário.")]
public sealed class LinuxSecretServiceIntegrationTests
{
    [Test]
    public async Task TransportReaderReadsAndRevokesOnlyItsDisposableVersionedProof()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Requer Secret Service Linux real.");
        var reference = new SecretReference(Guid.NewGuid(), 2);
        var wrongVersion = new SecretReference(reference.Id, 1);
        var store = new LinuxSecretServiceSecretStore();
        var reader = new LinuxClientTransportCredentialStore();
        const string proof = "synthetic-kapibara-broker-proof";
        try
        {
            // Escreve apenas a referência descartável; não consulta credenciais de Claude/Copilot.
            var saved = await store.SetAsync(reference, proof);
            Assert.That(saved.IsSuccess, Is.True, saved.Failure?.Code.ToString());
            Assert.That(await reader.ReadAsync(wrongVersion, CancellationToken.None), Is.Null);
            Assert.That(await reader.ReadAsync(reference, CancellationToken.None), Is.EqualTo(proof));
            var deleted = await store.DeleteAsync(reference);
            Assert.That(deleted.IsSuccess, Is.True, deleted.Failure?.Code.ToString());
            Assert.That(await reader.ReadAsync(reference, CancellationToken.None), Is.Null);
        }
        finally
        {
            var cleanup = await store.DeleteAsync(reference);
            Assert.That(cleanup.IsSuccess || cleanup.Failure?.Code == SecretStoreFailureCode.NotFound, Is.True,
                "Falha ao remover a prova descartável do cofre.");
        }
    }

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
