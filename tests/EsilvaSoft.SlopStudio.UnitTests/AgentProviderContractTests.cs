using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Promoted phase 7 contracts (P7-L05/06-ARCH): provider descriptor/capabilities/status, the neutral catalog and the
/// API-key write port. Everything is synthetic and offline; no real provider, vault or network is exercised.
/// </summary>
[TestFixture]
public sealed class AgentProviderContractTests
{
    private static readonly SecretReference SlotA = new(Guid.ParseExact("0f6c2a8e5b7d4c3a9e1f2b3c4d5e6f70", "N"));
    private static readonly SecretReference SlotB = new(Guid.ParseExact("1a2b3c4d5e6f47089a0b1c2d3e4f5a6b", "N"));
    private static readonly AgentAuthenticationMethod[] ApiKeyOnly = [AgentAuthenticationMethod.ApiKey];
    private static readonly string[] OnlyModelA = ["model-a"];

    [Test]
    public void DescriptorRejectsUnsafeIdentityAndDuplicatedMethods()
    {
        var none = AgentProviderCapabilities.None;
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor("", "X", [], none));
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor(new string('p', 65), "X", [], none));
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor("p\n", "X", [], none));
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor("p", "Nome\u0007", [], none));
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor("p", "X",
                [AgentAuthenticationMethod.ApiKey, AgentAuthenticationMethod.ApiKey], none));
            Assert.Throws<ArgumentException>(() => _ = new AgentProviderDescriptor("p", "X",
                [(AgentAuthenticationMethod)42], none));
        });
    }

    [Test]
    public void CapabilitiesWithoutEvidenceDeclareNothingAndDependentFlagsNeedTheirBase()
    {
        var unproven = new AgentProviderCapabilities { Chat = true, Streaming = true, ToolCalling = true, UsesNetwork = true };
        var toolsWithoutChat = new AgentProviderCapabilities
        {
            ToolCalling = true, Sessions = true, Streaming = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
        };

        var descriptor = new AgentProviderDescriptor("p", "Provider", ApiKeyOnly, unproven);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Capabilities.Chat || descriptor.Capabilities.Streaming || descriptor.Capabilities.ToolCalling,
                Is.False, "Sem evidência nada é declarado.");
            Assert.That(descriptor.Capabilities.UsesNetwork, Is.True, "Uso de rede restringe e nunca é descartado.");
            var normalized = toolsWithoutChat.Normalize();
            Assert.That((normalized.ToolCalling, normalized.Sessions, normalized.Streaming), Is.EqualTo((false, false, false)));
            Assert.That(new[] { descriptor.Capabilities.FileEditing, descriptor.Capabilities.CommandExecution,
                descriptor.Capabilities.SubAgents }, Has.All.False);
            Assert.That(descriptor.RequiresApiKey, Is.True);
        });
    }

    [Test]
    public void IntersectionNeverWidensAndKeepsTheWeakerEvidence()
    {
        var declared = new AgentProviderCapabilities
        {
            Chat = true, Streaming = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
        };
        var reported = new AgentProviderCapabilities
        {
            Chat = true, Streaming = true, ToolCalling = true, Mcp = true, UsesNetwork = true,
            Evidence = AgentCapabilityEvidence.Homologated,
        };

        var effective = reported.IntersectWith(declared);

        Assert.That(effective, Is.EqualTo(new AgentProviderCapabilities
        {
            Chat = true, Streaming = true, UsesNetwork = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
        }));
    }

    [Test]
    public async Task DefaultInterfaceMembersFailClosedForProvidersThatDoNotDescribeThemselves()
    {
        IAgentProvider provider = new BareProvider("bare");

        var descriptor = provider.Describe();
        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.IsLocal, Is.False, "Omitir IsLocal significa destino externo.");
            Assert.That((descriptor.ProviderId, descriptor.DisplayName), Is.EqualTo(("bare", "bare")));
            Assert.That(descriptor.AuthenticationMethods, Is.Empty);
            Assert.That(descriptor.Capabilities, Is.EqualTo(AgentProviderCapabilities.None));
            Assert.That((status.IsAvailable, status.AuthState, status.UnavailableCode),
                Is.EqualTo((false, AgentProviderAuthState.Unknown, (string?)"StatusNotReported")));
        });
    }

    [Test]
    public void CatalogDerivesDestinationFromIsLocalAndIgnoresForeignDescriptors()
    {
        var external = new DescribedProvider("ext", isLocal: false,
            describe: () => new AgentProviderDescriptor("ext", "Externo", ApiKeyOnly, Proven(chat: true)));
        var local = new DescribedProvider("loc", isLocal: true,
            describe: () => new AgentProviderDescriptor("loc", "Local", [AgentAuthenticationMethod.None], Proven(chat: true)));
        var impostor = new DescribedProvider("imp", isLocal: false,
            describe: () => new AgentProviderDescriptor("loc", "Local", [AgentAuthenticationMethod.None], Proven(chat: true)));
        var broken = new DescribedProvider("brk", isLocal: false, describe: () => throw new InvalidOperationException("secret-text"));

        var entries = new AgentProviderCatalog([external, local, impostor, broken]).List();

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => (entry.Descriptor.ProviderId, entry.Destination)), Is.EqualTo(new[]
            {
                ("ext", AgentDataDestinationKind.External), ("loc", AgentDataDestinationKind.Local),
                ("imp", AgentDataDestinationKind.External), ("brk", AgentDataDestinationKind.External),
            }));
            Assert.That(entries[2].Descriptor, Is.EqualTo(AgentProviderDescriptor.Minimal("imp")).Using<AgentProviderDescriptor>(SameShape),
                "Descritor de outro ID é descartado.");
            Assert.That(entries[3].Descriptor.Capabilities, Is.EqualTo(AgentProviderCapabilities.None));
            Assert.That(external.StatusCalls + local.StatusCalls, Is.Zero, "Listar não consulta estado nem cofre.");
        });
        Assert.Throws<ArgumentException>(() => _ = new AgentProviderCatalog([new BareProvider("dup"), new BareProvider("dup")]));
    }

    [Test]
    public async Task CatalogStatusIsIntersectedBoundedAndSanitized()
    {
        var widening = new DescribedProvider("wide", isLocal: false,
            describe: () => new AgentProviderDescriptor("wide", "Wide", ApiKeyOnly, Proven(chat: true)),
            status: _ => Task.FromResult(new AgentProviderStatus(true, AgentProviderAuthState.Configured,
                Proven(chat: true, tools: true), ["model-a", "bad\nmodel"], "model-a")));
        var faulty = new DescribedProvider("fault", isLocal: false,
            status: _ => Task.FromException<AgentProviderStatus>(new InvalidOperationException("sk-canary")));
        var hanging = new DescribedProvider("hang", isLocal: false,
            status: token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => AgentProviderStatus.NotReported,
                TaskScheduler.Default));
        var unsafeCode = new DescribedProvider("code", isLocal: false,
            status: _ => Task.FromResult(new AgentProviderStatus(false, AgentProviderAuthState.Invalid,
                AgentProviderCapabilities.None, unavailableCode: "401: invalid key sk-canary")));
        var catalog = new AgentProviderCatalog([widening, faulty, hanging, unsafeCode], TimeSpan.FromMilliseconds(100));

        var wide = await catalog.GetStatusAsync("wide", CancellationToken.None);
        var fault = await catalog.GetStatusAsync("fault", CancellationToken.None);
        var hang = await catalog.GetStatusAsync("hang", CancellationToken.None);
        var code = await catalog.GetStatusAsync("code", CancellationToken.None);
        var unknown = await catalog.GetStatusAsync("nope", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(wide.IsAvailable, Is.True);
            Assert.That(wide.Capabilities.ToolCalling, Is.False, "Estado dinâmico não amplia o descritor.");
            Assert.That(wide.Models, Is.EqualTo(OnlyModelA));
            Assert.That((fault.IsAvailable, fault.UnavailableCode), Is.EqualTo((false, (string?)"StatusFailed")));
            Assert.That((hang.IsAvailable, hang.UnavailableCode), Is.EqualTo((false, (string?)"StatusTimedOut")));
            Assert.That((code.AuthState, code.UnavailableCode), Is.EqualTo((AgentProviderAuthState.Invalid, (string?)"ProviderUnavailable")));
            Assert.That(unknown.UnavailableCode, Is.EqualTo("UnknownProvider"));
            Assert.That(string.Join("|", new[] { wide, fault, hang, code }.Select(item => item.UnavailableCode)),
                Does.Not.Contain("sk-canary"));
        });
    }

    [Test]
    public void CatalogStatusPropagatesOnlyTheCallersCancellation()
    {
        var hanging = new DescribedProvider("hang", isLocal: false,
            status: token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => AgentProviderStatus.NotReported,
                TaskScheduler.Default));
        var catalog = new AgentProviderCatalog([hanging]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(async () => await catalog.GetStatusAsync("hang", cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ApiKeyIsWrittenOnlyToTheProviderSlotAndNeverEchoed()
    {
        var vault = new InMemoryProfileSecretStore();
        using var store = new AgentApiKeyStore(vault, new Dictionary<string, SecretReference> { ["a"] = SlotA, ["b"] = SlotB });
        var buffer = "sk-test-canary-0001".ToCharArray();

        var saved = await store.SaveApiKeyAsync("a", buffer, CancellationToken.None);
        var state = await store.GetApiKeyStateAsync("a", CancellationToken.None);
        var otherState = await store.GetApiKeyStateAsync("b", CancellationToken.None);
        var removed = await store.RemoveApiKeyAsync("a", CancellationToken.None);
        var removedAgain = await store.RemoveApiKeyAsync("a", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.EqualTo(AgentCredentialSetupOutcome.Saved));
            Assert.That(state, Is.EqualTo(AgentProviderAuthState.Configured));
            Assert.That(otherState, Is.EqualTo(AgentProviderAuthState.NotConfigured));
            Assert.That((removed, removedAgain), Is.EqualTo((AgentCredentialSetupOutcome.Removed, AgentCredentialSetupOutcome.Removed)));
            Assert.That(vault.Count, Is.Zero);
            Assert.That(new string(buffer), Is.EqualTo("sk-test-canary-0001"), "O chamador é dono do buffer e o limpa.");
        });
    }

    [TestCase("")]
    [TestCase("sk-with space")]
    [TestCase("sk-pasted-newline\n")]
    [TestCase("\tsk-tab")]
    public async Task MalformedKeysAreRejectedBeforeTouchingTheVault(string key)
    {
        var vault = new InMemoryProfileSecretStore { DenySet = true };
        using var store = new AgentApiKeyStore(vault, new Dictionary<string, SecretReference> { ["a"] = SlotA });

        var outcome = await store.SaveApiKeyAsync("a", key.ToCharArray(), CancellationToken.None);
        var tooLong = await store.SaveApiKeyAsync("a", new string('k', AgentApiKeyStore.MaximumApiKeyLength + 1).ToCharArray(),
            CancellationToken.None);

        Assert.That((outcome, tooLong), Is.EqualTo((AgentCredentialSetupOutcome.Rejected, AgentCredentialSetupOutcome.Rejected)));
    }

    [Test]
    public async Task VaultFailuresMapToSafeOutcomesWithoutFallback()
    {
        var unknownProvider = await new AgentApiKeyStore(new InMemoryProfileSecretStore(), new Dictionary<string, SecretReference>())
            .SaveApiKeyAsync("x", "sk-1".ToCharArray(), CancellationToken.None);
        var denied = await new AgentApiKeyStore(new InMemoryProfileSecretStore { DenySet = true, DenyDelete = true },
            new Dictionary<string, SecretReference> { ["a"] = SlotA }).SaveApiKeyAsync("a", "sk-1".ToCharArray(), CancellationToken.None);
        var cancelled = await new AgentApiKeyStore(new FailingSecretStore(SecretStoreFailureCode.Cancelled),
            new Dictionary<string, SecretReference> { ["a"] = SlotA }).SaveApiKeyAsync("a", "sk-1".ToCharArray(), CancellationToken.None);
        var corruptState = await new AgentApiKeyStore(new FailingSecretStore(SecretStoreFailureCode.Corrupt),
            new Dictionary<string, SecretReference> { ["a"] = SlotA }).GetApiKeyStateAsync("a", CancellationToken.None);
        var lockedState = await new AgentApiKeyStore(new FailingSecretStore(SecretStoreFailureCode.Locked),
            new Dictionary<string, SecretReference> { ["a"] = SlotA }).GetApiKeyStateAsync("a", CancellationToken.None);
        var throwing = await new AgentApiKeyStore(new FailingSecretStore(null),
            new Dictionary<string, SecretReference> { ["a"] = SlotA }).SaveApiKeyAsync("a", "sk-1".ToCharArray(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unknownProvider, Is.EqualTo(AgentCredentialSetupOutcome.UnknownProvider));
            Assert.That(denied, Is.EqualTo(AgentCredentialSetupOutcome.VaultUnavailable));
            Assert.That(cancelled, Is.EqualTo(AgentCredentialSetupOutcome.Cancelled));
            Assert.That(corruptState, Is.EqualTo(AgentProviderAuthState.Invalid));
            Assert.That(lockedState, Is.EqualTo(AgentProviderAuthState.VaultUnavailable));
            Assert.That(throwing, Is.EqualTo(AgentCredentialSetupOutcome.Failed));
        });
        Assert.Throws<ArgumentException>(() => _ = new AgentApiKeyStore(new InMemoryProfileSecretStore(),
            new Dictionary<string, SecretReference> { ["a"] = SlotA, ["b"] = SlotA }), "Dois providers não dividem um slot.");
    }

    private static AgentProviderCapabilities Proven(bool chat = false, bool tools = false) => new()
    {
        Chat = chat, Streaming = chat, ToolCalling = tools, UsesNetwork = true, Evidence = AgentCapabilityEvidence.AutomatedContract,
    };

    private static bool SameShape(AgentProviderDescriptor left, AgentProviderDescriptor right) =>
        left.ProviderId == right.ProviderId && left.DisplayName == right.DisplayName &&
        left.AuthenticationMethods.SequenceEqual(right.AuthenticationMethods) && left.Capabilities == right.Capabilities;

    private sealed class BareProvider(string providerId) : IAgentProvider
    {
        public string ProviderId { get; } = providerId;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DescribedProvider(
        string providerId,
        bool isLocal,
        Func<AgentProviderDescriptor>? describe = null,
        Func<CancellationToken, Task<AgentProviderStatus>>? status = null) : IAgentProvider
    {
        public int StatusCalls { get; private set; }

        public string ProviderId { get; } = providerId;

        public bool IsLocal { get; } = isLocal;

        public AgentProviderDescriptor Describe() => describe?.Invoke() ?? AgentProviderDescriptor.Minimal(ProviderId);

        public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return status?.Invoke(cancellationToken) ?? Task.FromResult(AgentProviderStatus.NotReported);
        }

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Vault double that fails every call with <paramref name="code"/>, or throws with a canary when null.</summary>
    private sealed class FailingSecretStore(SecretStoreFailureCode? code) : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            code is { } failure ? Task.FromResult(SecretStoreResults.Failed<string>(failure)) : throw new IOException("sk-canary");

        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
            CancellationToken cancellationToken = default) =>
            code is { } failure ? Task.FromResult(SecretStoreOperationResult.Failed(failure)) : throw new IOException("sk-canary");

        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            code is { } failure ? Task.FromResult(SecretStoreOperationResult.Failed(failure)) : throw new IOException("sk-canary");
    }
}
