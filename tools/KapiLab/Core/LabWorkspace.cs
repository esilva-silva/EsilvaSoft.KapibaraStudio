using System.Text;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class LabWorkspace
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string? Resolve(string? commandLineValue)
    {
        var value = string.IsNullOrWhiteSpace(commandLineValue)
            ? Environment.GetEnvironmentVariable("KAPILAB_WORKSPACE")
            : commandLineValue;
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    public static string ReadUtf8FileLimited(string path, int maximumBytes)
    {
        var bytes = ReadFileLimited(path, maximumBytes);
        return StrictUtf8.GetString(bytes);
    }

    public static byte[] ReadFileLimited(string path, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("Arquivo vazio ou acima do limite de leitura.");

        var result = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < result.Length)
        {
            var read = stream.Read(result, offset, result.Length - offset);
            if (read == 0) throw new EndOfStreamException("Arquivo mudou durante a leitura.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Arquivo excedeu o limite durante a leitura.");
        return result;
    }

    public static string ResolveOutput(string workspace, string requestedPath)
    {
        var root = Path.GetFullPath(workspace);
        var fullPath = Path.GetFullPath(requestedPath, root);
        var allowed = new[] { Path.Combine(root, "data", "lab"), Path.Combine(root, "reports", "lab"), Path.Combine(root, "tmp") };
        if (!allowed.Any(directory => IsWithin(fullPath, directory)))
            throw new UnauthorizedAccessException("A saída só pode ficar em data/lab, reports/lab ou tmp do workspace.");
        RefuseLinks(root, fullPath);
        return fullPath;
    }

    public static void RefuseInputOverwrite(string workspace, string outputPath, IEnumerable<string> inputPaths)
    {
        var root = Path.GetFullPath(workspace);
        var output = Path.GetFullPath(outputPath, root);
        foreach (var inputPath in inputPaths)
        {
            var input = Path.GetFullPath(inputPath, root);
            if (PathComparer.Equals(output, input))
                throw new UnauthorizedAccessException("A saída não pode sobrescrever um artefato de entrada.");
        }
    }

    public static void RefuseBlindInput(string path, string? workspace)
    {
        var fullPath = Path.GetFullPath(path);
        RefuseLinksInPath(fullPath);
        if (workspace is null) return;
        if (IsWithin(fullPath, workspace)) RefuseLinks(workspace, fullPath);
        var blind = Path.Combine(workspace, "data", "eval", "blind");
        if (IsWithin(fullPath, blind))
            throw new UnauthorizedAccessException("A leitura do conjunto cego está bloqueada.");
    }

    private static void RefuseLinksInPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? throw new UnauthorizedAccessException("Caminho sem raiz segura.");
        RefuseLinks(root, fullPath);
    }

    private static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RefuseLinks(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        var current = root;
        RefuseReparsePoint(current);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".") continue;
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current)) RefuseReparsePoint(current);
        }
    }

    private static void RefuseReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Caminhos com link simbólico/junction não são aceitos para escrita do laboratório.");
    }
}
