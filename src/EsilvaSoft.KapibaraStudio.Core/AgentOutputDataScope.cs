namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Data classification authorized to leave the tool boundary.</summary>
public enum AgentOutputDataScope
{
    Metadata = 0,
    Schema = 1,
    Diagnostics = 2,
    DocumentValues = 3
}
