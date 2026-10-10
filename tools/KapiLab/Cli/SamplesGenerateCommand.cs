using System.Text;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class SamplesGenerateCommand
{
    public static async Task<int> RunAsync(int seed, int count, int? index, string? output, string? workspace,
        Func<int, int, int?, CancellationToken, IReadOnlyList<string>>? generate = null,
        CancellationToken cancellationToken = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        cancellationToken = cancellation.Token;
        try
        {
            var root = LabWorkspace.Resolve(workspace);
            if (root is null)
            {
                Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE.");
                return (int)ExitCode.Usage;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var lines = (generate ?? SyntheticSampleGenerator.Generate)(seed, count, index, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = lines.Sum(line => (long)Encoding.UTF8.GetByteCount(line) + 1);
            if (string.IsNullOrWhiteSpace(output) || output == "-")
            {
                // Publish only after generation is complete. Do not pass the cancellation token to the
                // final write: cancellation must not interrupt the buffered output halfway through.
                await Console.Out.WriteAsync(string.Concat(lines.Select(line => line + "\n"))).ConfigureAwait(false);
            }
            else
            {
                var destination = LabWorkspace.ResolveOutput(root, output);
                await AtomicWriteAsync(destination, lines, cancellationToken).ConfigureAwait(false);
            }
            Console.Error.WriteLine($"info samples.generate count={lines.Count} bytes={bytes} execution=skipped");
            return (int)ExitCode.Success;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error acesso recusado por política de privacidade/caminho.");
            return (int)ExitCode.PrivacyViolation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("info samples.generate cancelled");
            return (int)ExitCode.Cancelled;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine("error parâmetros inválidos ou falha ao gravar amostras.");
            return (int)ExitCode.InvalidInput;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task AtomicWriteAsync(string path, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (var line in lines)
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
