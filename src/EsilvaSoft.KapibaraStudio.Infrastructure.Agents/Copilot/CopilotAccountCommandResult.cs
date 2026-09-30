using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

/// <summary>Fixed classification only; no native stdout, stderr, paths or account identity.</summary>
public sealed record CopilotAccountCommandResult(CopilotAccountCommandState State, CopilotAccountStatus? Account);
