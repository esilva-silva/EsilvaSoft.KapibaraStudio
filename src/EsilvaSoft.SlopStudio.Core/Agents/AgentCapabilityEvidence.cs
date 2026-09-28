namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>How the declared capabilities of a provider were verified.</summary>
public enum AgentCapabilityEvidence
{
    /// <summary>Nothing is verified; every capability is treated as absent.</summary>
    None,

    /// <summary>Automated contract tests (offline fixtures, validated local model); no real account or service.</summary>
    AutomatedContract,

    /// <summary>Verified with an authorized credential against the real service and recorded in the validation report.</summary>
    Homologated,
}
