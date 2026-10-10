using System.Text;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class ProcessMatrixCommand
{
    public static async Task<int> RunAsync(string input, string? output, string? workspace)
    {
        var root = LabWorkspace.Resolve(workspace);
        if (root is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
            return (int)ExitCode.Usage;
        }
        var inputPath = KapiLabCommandLine.ReadArtifactPath(input, root);
        var info = new FileInfo(inputPath);
        if (!info.Exists || info.Length > 128 * 1024) return (int)ExitCode.InvalidInput;
        var inputBytes = await File.ReadAllBytesAsync(inputPath).ConfigureAwait(false);
        if (inputBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)) return (int)ExitCode.InvalidInput;
        var definition = LocalProcessMatrix.Parse(new UTF8Encoding(false, true).GetString(inputBytes));
        string? outputPath = null;
        if (output is not null)
        {
            outputPath = LabWorkspace.ResolveOutput(root, output);
            LabWorkspace.RefuseInputOverwrite(root, outputPath, [inputPath]);
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        (ProcessMatrixReport Report, int ExitCode) result;
        try
        {
            result = await LocalProcessMatrix.RunAsync(definition, root, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        var serialized = LocalProcessMatrix.Serialize(result.Report);
        if (outputPath is null)
        {
            Console.Out.WriteLine(serialized);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, serialized, new UTF8Encoding(false)).ConfigureAwait(false);
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                schema = result.Report.Schema,
                result.Report.Complete,
                cases = result.Report.Cases.Count,
                output = Path.GetRelativePath(root, outputPath),
            }));
        }
        return result.ExitCode;
    }
}
