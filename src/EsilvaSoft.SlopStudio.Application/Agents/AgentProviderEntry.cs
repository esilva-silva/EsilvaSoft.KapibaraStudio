using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>A registered provider as the UI may show it: validated descriptor plus the trusted destination.</summary>
public sealed record AgentProviderEntry(AgentProviderDescriptor Descriptor, AgentDataDestinationKind Destination);
