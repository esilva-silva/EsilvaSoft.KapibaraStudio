using System.Collections.Concurrent;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Preserva PNGs de inspeção em uma pasta exclusiva por teste e execução.</summary>
internal static class UiEvidenceDirectory
{
    private static readonly ConcurrentDictionary<string, string> Directories = new(StringComparer.Ordinal);

    public static string Current()
    {
        var context = TestContext.CurrentContext;
        return Directories.GetOrAdd(context.Test.ID, _ => Path.Combine(context.WorkDirectory, "ui-evidence", $"render-{Guid.NewGuid():N}"));
    }
}
