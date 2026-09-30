namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Captures host variables at the adapter boundary for one operation.</summary>
public interface IHostEnvironmentSnapshot
{
    IReadOnlyDictionary<string, string> Capture();
}
