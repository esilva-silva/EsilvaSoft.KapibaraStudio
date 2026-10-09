namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>Per-turn request for local reasoning. A null budget uses the package default.</summary>
public sealed record AgentReasoningOptions(bool Enabled, int? BudgetTokens = null);
