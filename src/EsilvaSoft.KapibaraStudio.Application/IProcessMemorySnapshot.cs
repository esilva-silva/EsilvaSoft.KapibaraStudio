namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Reads the current process working set for optional runtime diagnostics.</summary>
public interface IProcessMemorySnapshot
{
    long? GetWorkingSetBytes();
}
