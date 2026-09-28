using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-L06-HOST: production <see cref="IAgentApiKeyStore"/>. The vault is a recording double (no OS store is touched);
/// what is proved is the contract around it — right slot, validation before any write, safe outcomes, no echo.
/// Native Credential Manager/Secret Service behavior stays under the lote 1 homologation.
/// </summary>
[TestFixture]
public sealed class DesktopAgentApiKeyStoreTests
{
    private const string Canary = "sk-CANARY-7f3e9d";
    private static readonly SecretReference SlotA = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    private static readonly SecretReference SlotB = new(Guid.Parse("66666666-7777-8888-9999-000000000000"));

    private static (DesktopAgentApiKeyStore Store, RecordingSecretStore Vault) Create()
    {
        var vault = new RecordingSecretStore();
        var store = new DesktopAgentApiKeyStore(vault, new Dictionary<string, SecretReference>
        {
            ["provider-a"] = SlotA,
            ["provider-b"] = SlotB,
        });
        return (store, vault);
    }

    [Test]
    public async Task SaveWritesOnlyTheProvidersSlotAndStateReportsPresenceWithoutTheValue()
    {
        var (store, vault) = Create();
        var buffer = Canary.ToCharArray();

        var outcome = await store.SaveApiKeyAsync("provider-a", buffer, CancellationToken.None);
        var stateA = await store.GetApiKeyStateAsync("provider-a", CancellationToken.None);
        var stateB = await store.GetApiKeyStateAsync("provider-b", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AgentCredentialSetupOutcome.Saved));
            Assert.That(vault.Values.Keys, Is.EquivalentTo(new[] { SlotA }), "Only the provider's own slot is written.");
            Assert.That(vault.Values[SlotA], Is.EqualTo(Canary));
            Assert.That(stateA, Is.EqualTo(AgentProviderAuthState.Configured));
            Assert.That(stateB, Is.EqualTo(AgentProviderAuthState.NotConfigured));
            Assert.That(new string(buffer), Is.EqualTo(Canary), "The caller owns and clears its buffer.");
        });
    }

    [TestCase("")]
    [TestCase("sk abc")]
    [TestCase("sk-abc\n")]
    [TestCase("\tsk-abc")]
    public async Task InvalidFormatsAreRejectedBeforeAnyVaultCall(string key)
    {
        var (store, vault) = Create();

        var outcome = await store.SaveApiKeyAsync("provider-a", key.ToCharArray(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AgentCredentialSetupOutcome.Rejected));
            Assert.That(vault.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task OversizedKeyAndUnknownProviderWriteNothing()
    {
        var (store, vault) = Create();

        var oversized = await store.SaveApiKeyAsync("provider-a",
            new string('k', DesktopAgentApiKeyStore.MaximumKeyLength + 1).ToCharArray(), CancellationToken.None);
        var unknown = await store.SaveApiKeyAsync("provider-z", Canary.ToCharArray(), CancellationToken.None);
        var unknownRemove = await store.RemoveApiKeyAsync("provider-z", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(oversized, Is.EqualTo(AgentCredentialSetupOutcome.Rejected));
            Assert.That(unknown, Is.EqualTo(AgentCredentialSetupOutcome.UnknownProvider));
            Assert.That(unknownRemove, Is.EqualTo(AgentCredentialSetupOutcome.UnknownProvider));
            Assert.That(vault.Calls, Is.Zero);
        });
    }

    [TestCase(SecretStoreFailureCode.Unavailable, AgentCredentialSetupOutcome.VaultUnavailable)]
    [TestCase(SecretStoreFailureCode.Locked, AgentCredentialSetupOutcome.VaultUnavailable)]
    [TestCase(SecretStoreFailureCode.Denied, AgentCredentialSetupOutcome.VaultUnavailable)]
    [TestCase(SecretStoreFailureCode.Cancelled, AgentCredentialSetupOutcome.Cancelled)]
    [TestCase(SecretStoreFailureCode.Corrupt, AgentCredentialSetupOutcome.Failed)]
    public async Task VaultFailuresMapToSafeOutcomesWithoutFallback(SecretStoreFailureCode failure, AgentCredentialSetupOutcome expected)
    {
        var (store, vault) = Create();
        vault.Failure = failure;

        var outcome = await store.SaveApiKeyAsync("provider-a", Canary.ToCharArray(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(expected));
            Assert.That(vault.Values, Is.Empty, "No plaintext or alternative fallback exists.");
        });
    }

    [Test]
    public async Task AThrowingVaultNeverLeaksItsMessageOrTheKey()
    {
        var (store, vault) = Create();
        vault.Throw = new InvalidOperationException("vault internals " + Canary);

        AgentCredentialSetupOutcome outcome = default;
        Assert.DoesNotThrowAsync(async () => outcome = await store.SaveApiKeyAsync("provider-a", Canary.ToCharArray(), CancellationToken.None));
        var state = await store.GetApiKeyStateAsync("provider-a", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AgentCredentialSetupOutcome.Failed));
            Assert.That(state, Is.EqualTo(AgentProviderAuthState.VaultUnavailable));
        });
    }

    [Test]
    public async Task RemoveIsIdempotentAndCallerCancellationPropagates()
    {
        var (store, vault) = Create();
        await store.SaveApiKeyAsync("provider-a", Canary.ToCharArray(), CancellationToken.None);

        var first = await store.RemoveApiKeyAsync("provider-a", CancellationToken.None);
        var second = await store.RemoveApiKeyAsync("provider-a", CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(AgentCredentialSetupOutcome.Removed));
            Assert.That(second, Is.EqualTo(AgentCredentialSetupOutcome.Removed), "Already absent is the requested state.");
            Assert.That(vault.Values, Is.Empty);
            Assert.ThrowsAsync<OperationCanceledException>(() =>
                store.SaveApiKeyAsync("provider-a", Canary.ToCharArray(), cancelled.Token));
        });
    }

    /// <summary>Vault double that records calls and can fail with a typed code or an exception.</summary>
    internal sealed class RecordingSecretStore : ISecretStore
    {
        public Dictionary<SecretReference, string> Values { get; } = [];

        public int Calls { get; private set; }

        public List<SecretReference> Reads { get; } = [];

        public SecretStoreFailureCode? Failure { get; set; }

        public Exception? Throw { get; set; }

        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            Reads.Add(reference);
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreResult<string>>(exception);
            return Task.FromResult(Values.TryGetValue(reference, out var value)
                ? SecretStoreResults.Success(value)
                : SecretStoreResults.Failed<string>(SecretStoreFailureCode.NotFound));
        }

        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreOperationResult>(exception);
            if (Failure is { } failure) return Task.FromResult(SecretStoreOperationResult.Failed(failure));
            Values[reference] = secret;
            return Task.FromResult(SecretStoreOperationResult.Success());
        }

        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreOperationResult>(exception);
            return Task.FromResult(Values.Remove(reference)
                ? SecretStoreOperationResult.Success()
                : SecretStoreOperationResult.Failed(SecretStoreFailureCode.NotFound));
        }
    }
}
