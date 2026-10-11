using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

// Compiled only into the lab executable. The Desktop cannot register or resolve this type.
internal sealed class AgentFixturePorts : IAgentWorkspaceContextSource, IAgentEditProposalSink, IAgentToolConfirmationPrompt
{
    internal AgentWorkspaceContext Context { get; set; } = new(DateTimeOffset.UtcNow);
    internal bool Approve { get; set; }
    internal List<AgentEditProposal> Proposals { get; } = [];
    public AgentWorkspaceContext Capture() => Context;
    public AgentEditProposalSubmission Submit(AgentEditProposal proposal)
    {
        Proposals.Add(proposal);
        return new(AgentEditProposalSubmissionStatus.Registered);
    }
    public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Approve ? AgentToolConfirmationDecision.ApprovedOnce : AgentToolConfirmationDecision.Rejected);
    }
    internal static AgentFixturePorts Register(IServiceCollection services)
    {
        var ports = new AgentFixturePorts();
        services.AddSingleton<IAgentWorkspaceContextSource>(ports);
        services.AddSingleton<IAgentEditProposalSink>(ports);
        services.AddSingleton<IAgentToolConfirmationPrompt>(ports);
        services.AddSingleton<IMetadataCache, MetadataCache>();
        return ports;
    }
}
