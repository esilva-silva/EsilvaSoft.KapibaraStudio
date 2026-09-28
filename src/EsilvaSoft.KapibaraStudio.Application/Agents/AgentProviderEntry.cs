using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>A registered provider as the UI may show it: validated descriptor plus the trusted destination.</summary>
public sealed record AgentProviderEntry(AgentProviderDescriptor Descriptor, AgentDataDestinationKind Destination);
