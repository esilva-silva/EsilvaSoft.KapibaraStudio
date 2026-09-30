using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration"), Explicit("Acessa o Windows Credential Manager da sessão atual.")]
public sealed class WindowsClientTransportCredentialStoreIntegrationTests
{
    [Test]
    public async Task ReadsOnlyTheVersionedTargetWrittenForItsReference()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("O teste requer Windows Credential Manager.");

        var reference = new SecretReference(Guid.NewGuid());
        var writer = new WindowsCredentialSecretStore();
        var reader = new WindowsClientTransportCredentialStore();
        var availability = await writer.GetAvailabilityAsync();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
            Assert.Ignore("Credential Manager indisponível para esta sessão.");

        const string syntheticProof = "integration-only-mcp-proof";
        var write = await writer.SetAsync(reference, syntheticProof);
        if (!write.IsSuccess && write.Failure?.Code is SecretStoreFailureCode.Unavailable or SecretStoreFailureCode.Denied)
            Assert.Ignore("A sessão não permite gravar credenciais sintéticas.");
        Assert.That(write.IsSuccess, Is.True);

        try
        {
            Assert.That(await reader.ReadAsync(reference, CancellationToken.None), Is.EqualTo(syntheticProof));
        }
        finally
        {
            var cleanup = await writer.DeleteAsync(reference);
            Assert.That(cleanup.IsSuccess || cleanup.Failure?.Code == SecretStoreFailureCode.NotFound, Is.True,
                "Não foi possível remover a credencial sintética.");
        }
    }
}
