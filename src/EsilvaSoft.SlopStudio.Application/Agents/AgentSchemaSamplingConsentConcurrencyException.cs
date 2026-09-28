namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>A competing schema sampling consent change must be reviewed before retrying.</summary>
public sealed class AgentSchemaSamplingConsentConcurrencyException : InvalidOperationException
{
    public AgentSchemaSamplingConsentConcurrencyException()
        : base("O consentimento de amostragem de schema mudou. Recarregue antes de salvar.")
    {
    }
}
