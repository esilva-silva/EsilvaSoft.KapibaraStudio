using System.Collections;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalHostEnvironmentSnapshot : IHostEnvironmentSnapshot
{
    public IReadOnlyDictionary<string, string> Capture() => Environment.GetEnvironmentVariables()
        .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
}
