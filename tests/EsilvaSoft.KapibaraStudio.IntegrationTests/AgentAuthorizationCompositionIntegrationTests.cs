using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentAuthorizationCompositionIntegrationTests
{
    [Test]
    public void AgentAuthorizationServicesShareTheRegisteredWorkspaceOwner()
    {
        using var directory = new SyntheticDirectory();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(directory.Path, "workspace.db"));

        using var provider = services.BuildServiceProvider();
        var owner = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        var policyProvider = provider.GetRequiredService<IAgentAuthorizationPolicyProvider>();
        var policyRepository = provider.GetRequiredService<IAgentAuthorizationPolicyRepository>();
        var evaluator = provider.GetRequiredService<IAgentPermissionEvaluator>();

        Assert.Multiple(() =>
        {
            Assert.That(policyProvider, Is.InstanceOf<NativeChatTurnPolicyProvider>());
            Assert.That(policyRepository, Is.SameAs(owner));
            Assert.That(provider.GetRequiredService<IAgentAuthorizationPolicyProvider>(), Is.SameAs(policyProvider));
            Assert.That(provider.GetRequiredService<IAgentAuthorizationPolicyRepository>(), Is.SameAs(policyRepository));
            Assert.That(evaluator, Is.InstanceOf<AgentPermissionEvaluator>());
            Assert.That(provider.GetRequiredService<IAgentPermissionEvaluator>(), Is.SameAs(evaluator));
            Assert.That(provider.GetRequiredService<IAgentToolRegistry>().GetDescriptors()
                    .Select(descriptor => AgentToolExposure.StageOf(descriptor.Name)),
                Is.All.EqualTo(AgentToolExposureStage.Metadata));
        });
    }
}
