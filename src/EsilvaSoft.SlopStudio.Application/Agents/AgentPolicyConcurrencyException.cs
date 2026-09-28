namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>A competing authorization change must be reviewed before retrying.</summary>
public sealed class AgentPolicyConcurrencyException : InvalidOperationException
{
    public AgentPolicyConcurrencyException()
        : base("A política de autorização mudou. Recarregue as concessões antes de salvar.")
    {
    }
}
