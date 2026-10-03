using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Creates resources owned by one provider; creation must not start a runtime.</summary>
internal interface ICopilotRuntimeResourcesFactory
{
    ICopilotRuntimeResources Create();
}

internal sealed class LocalCopilotRuntimeResourcesFactory : ICopilotRuntimeResourcesFactory
{
    private readonly ICopilotCliConfiguration _configuration;
    public LocalCopilotRuntimeResourcesFactory(ICopilotCliConfiguration configuration) => _configuration = configuration;
    public ICopilotRuntimeResources Create() => new LocalCopilotRuntimeResources(configuration: _configuration);
}
