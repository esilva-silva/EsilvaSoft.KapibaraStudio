using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class KnowledgeCatalogCommand
{
    internal static async Task<int> ExportAsync(string dialect, string output, string? workspace)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace.");
        var destination = LabWorkspace.ResolveOutput(root, output);
        var snapshot = KnowledgeCatalogSnapshot.Export([new LanguageCatalogSource()], dialect, KnowledgeCatalogSnapshot.EmbeddedSource());
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, KnowledgeCatalogSnapshot.Serialize(snapshot), new UTF8Encoding(false));
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        KapiLabCommandLine.WriteJson(new { schema = snapshot.Schema, snapshot.Counts, snapshot.SnapshotSha256 });
        return 0;
    }

    internal static Task<int> CheckAsync(string expected, string actual, string? workspace)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace.");
        KnowledgeCatalogSnapshot.Snapshot Read(string path)
        {
            var fullPath = Path.GetFullPath(path, root);
            LabWorkspace.RefuseBlindInput(fullPath, root);
            return KnowledgeCatalogSnapshot.Parse(LabWorkspace.ReadUtf8FileLimited(fullPath, 16 * 1024 * 1024));
        }
        var differences = KnowledgeCatalogSnapshot.Compare(Read(expected), Read(actual));
        KapiLabCommandLine.WriteJson(new { schema = "kapilab-knowledge-catalog-check-v1", matches = differences.Length == 0, differences });
        return Task.FromResult(differences.Length == 0 ? 0 : 7);
    }
}
