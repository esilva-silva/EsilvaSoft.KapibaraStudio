using System.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Reads the immutable test corpus compiled into this assembly, without consulting host files.</summary>
internal static class EmbeddedTestData
{
    private const string Prefix = "test-data/";
    private static readonly Dictionary<string, string> Resources = typeof(EmbeddedTestData).Assembly
        .GetManifestResourceNames()
        .Select(name => (Name: name, Path: name.Replace('\\', '/')))
        .Where(resource => resource.Path.StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(resource => resource.Path[Prefix.Length..], resource => resource.Name, StringComparer.Ordinal);

    public static IEnumerable<string> Paths => Resources.Keys;

    public static bool Contains(string path) => Resources.ContainsKey(path.Replace('\\', '/'));

    public static Stream Open(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!Resources.TryGetValue(normalized, out var name))
            throw new InvalidDataException("Recurso do corpus não encontrado: " + normalized);
        return typeof(EmbeddedTestData).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Recurso do corpus sem conteúdo: " + normalized);
    }

    public static string ReadText(string path)
    {
        using var stream = Open(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    public static byte[] ReadBytes(string path)
    {
        using var stream = Open(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
