namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Creates resources owned by one provider; creation must not start a runtime.</summary>
internal interface ICopilotRuntimeResourcesFactory
{
    ICopilotRuntimeResources Create();
}

internal sealed class LocalCopilotRuntimeResourcesFactory : ICopilotRuntimeResourcesFactory
{
    public ICopilotRuntimeResources Create() => new LocalCopilotRuntimeResources();
}
