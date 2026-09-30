using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>System process access for optional local model runtime diagnostics.</summary>
public sealed class LocalProcessMemorySnapshot : IProcessMemorySnapshot
{
    public long? GetWorkingSetBytes()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
