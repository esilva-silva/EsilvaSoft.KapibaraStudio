namespace EsilvaSoft.KapibaraStudio.Application;

public interface IAutocompleteDiagnostics
{
    void Record(string eventName, string? detail = null, TimeSpan? duration = null);
}
