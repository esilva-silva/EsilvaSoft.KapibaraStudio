namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Effect category used by authorization and approval gates; it is separate from a permission grant.</summary>
public enum AgentToolRisk
{
    ReadOnly = 0,
    Write = 1,
    Destructive = 2,
    Administrative = 3
}
