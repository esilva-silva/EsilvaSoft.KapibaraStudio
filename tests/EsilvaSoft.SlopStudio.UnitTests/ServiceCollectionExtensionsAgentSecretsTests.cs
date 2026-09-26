using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class ServiceCollectionExtensionsAgentSecretsTests
{
    [Test]
    public void AgentAuthorizationServicesShareTheWorkspaceOwnerAndRemainSingletons()
    {
        var directory = Directory.CreateTempSubdirectory("slopstudio-agent-policy-di-");
        try
        {
            var services = new ServiceCollection();
            services.AddSlopStudioInfrastructure(Path.Combine(directory.FullName, "workspace.db"));

            using var provider = services.BuildServiceProvider();
            var owner = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
            var policyProvider = provider.GetRequiredService<IAgentAuthorizationPolicyProvider>();
            var policyRepository = provider.GetRequiredService<IAgentAuthorizationPolicyRepository>();
            var evaluator = provider.GetRequiredService<IAgentPermissionEvaluator>();

            Assert.Multiple(() =>
            {
                Assert.That(policyProvider, Is.SameAs(owner));
                Assert.That(policyRepository, Is.SameAs(owner));
                Assert.That(policyProvider, Is.SameAs(policyRepository));
                Assert.That(provider.GetRequiredService<IAgentAuthorizationPolicyProvider>(), Is.SameAs(policyProvider));
                Assert.That(provider.GetRequiredService<IAgentAuthorizationPolicyRepository>(), Is.SameAs(policyRepository));
                Assert.That(evaluator, Is.InstanceOf<AgentPermissionEvaluator>());
                Assert.That(provider.GetRequiredService<IAgentPermissionEvaluator>(), Is.SameAs(evaluator));
                // Always composed; the default stage (ADR-056) releases metadata tools only, never documents or writes.
                Assert.That(provider.GetRequiredService<IAgentToolRegistry>().GetDescriptors()
                    .Select(descriptor => AgentToolExposure.StageOf(descriptor.Name)),
                    Is.All.EqualTo(AgentToolExposureStage.Metadata));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void RealCompositionResolvesOneNativeStoreWithoutProbingItAtStartup()
    {
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(Path.Combine(Path.GetTempPath(), $"slopstudio-secrets-{Guid.NewGuid():N}.db"));

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ISecretStore>();
        var credentialProvider = provider.GetRequiredService<IAgentCredentialProvider>();

        Assert.Multiple(() =>
        {
            Assert.That(store, Is.SameAs(provider.GetRequiredService<ISecretStore>()));
            Assert.That(credentialProvider, Is.SameAs(provider.GetRequiredService<IAgentCredentialProvider>()));
            Assert.That(store, OperatingSystem.IsWindows() ? Is.InstanceOf<WindowsCredentialSecretStore>() :
                OperatingSystem.IsLinux() ? Is.InstanceOf<LinuxSecretServiceSecretStore>() :
                Is.InstanceOf<UnavailableSecretStore>());
        });
    }

    [Test]
    public async Task UnsupportedPlatformRemainsAvailableToTheContainerWithTypedFailures()
    {
        var store = ServiceCollectionExtensions.CreateAgentSecretStore(isWindows: false, isLinux: false);
        var reference = new SecretReference(Guid.NewGuid());

        var availability = await store.GetAvailabilityAsync();
        var read = await store.GetAsync(reference);
        var write = await store.SetAsync(reference, "fixture-only");
        var delete = await store.DeleteAsync(reference);

        Assert.Multiple(() =>
        {
            Assert.That(availability.Value, Is.EqualTo(SecretStoreAvailability.UnsupportedPlatform));
            Assert.That(read.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
            Assert.That(write.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
            Assert.That(delete.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
        });
    }

    [Test]
    public async Task CredentialProviderForwardsReferenceAndCancellationWithoutCachingFailure()
    {
        var store = new RecordingUnavailableStore();
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(Path.Combine(Path.GetTempPath(), $"slopstudio-secrets-{Guid.NewGuid():N}.db"));
        services.Replace(ServiceDescriptor.Singleton<ISecretStore>(store));
        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IAgentCredentialProvider>();
        var reference = new SecretReference(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();

        var first = await resolver.ResolveAsync(reference, cancellation.Token);
        var second = await resolver.ResolveAsync(reference, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<ISecretStore>(), Is.SameAs(store));
            Assert.That(first.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
            Assert.That(second.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.Unavailable));
            Assert.That(store.ReadCount, Is.EqualTo(2));
            Assert.That(store.LastReference, Is.SameAs(reference));
            Assert.That(store.LastCancellationToken, Is.EqualTo(cancellation.Token));
        });
    }

    private sealed class RecordingUnavailableStore : ISecretStore
    {
        public int ReadCount { get; private set; }
        public SecretReference? LastReference { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            LastReference = reference;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));
        }

        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            throw new AssertionException("A composição não deve consultar o backend.");

        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default) =>
            throw new AssertionException("A resolução não deve escrever segredos.");

        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            throw new AssertionException("A resolução não deve excluir segredos.");
    }
}
