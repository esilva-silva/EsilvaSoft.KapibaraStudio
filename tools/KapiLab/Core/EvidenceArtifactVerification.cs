using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Checks local artifact bytes; it does not independently validate metric values or external ownership.</summary>
internal sealed class EvidenceArtifactVerification(string workspace, string? outputPath = null)
{
    internal const long MaximumArtifactBytes = 64L * 1024 * 1024;
    private const long MaximumTotalBytes = 256L * 1024 * 1024;
    private readonly string _root = Path.GetFullPath(workspace);
    private long _bytesRead;

    public string? Verify(string reference, string expectedSha256)
    {
        // The optional fragment identifies a metric to the producer; only artifact bytes are checked here.
        var requested = reference.Split('#', 2)[0];
        if (string.IsNullOrWhiteSpace(requested) || Path.IsPathRooted(requested) || requested.Contains(':', StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A referência de evidência precisa ser relativa ao workspace.");
        var path = Path.GetFullPath(requested, _root);
        var relative = Path.GetRelativePath(_root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A referência de evidência está fora do workspace.");
        LabWorkspace.RefuseBlindInput(path, _root);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(_root, outputPath, [path]);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumArtifactBytes || stream.Length > MaximumTotalBytes - _bytesRead)
                throw new InvalidDataException("Artefato de evidência excede o orçamento de leitura.");
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long artifactBytes = 0;
            int read;
            while ((read = stream.Read(buffer)) != 0)
            {
                artifactBytes += read;
                _bytesRead += read;
                if (artifactBytes > MaximumArtifactBytes || _bytesRead > MaximumTotalBytes)
                    throw new InvalidDataException("Artefato de evidência excedeu o orçamento durante a leitura.");
                hasher.AppendData(buffer.AsSpan(0, read));
            }
            var actual = Convert.ToHexStringLower(hasher.GetHashAndReset());
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase) ? actual : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public PointerVerification VerifyNumber(string reference, string? pointer, string expectedSha256, double expectedValue)
    {
        var requested = reference;
        if (string.IsNullOrWhiteSpace(requested) || Path.IsPathRooted(requested) || requested.Contains(':', StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A referência de evidência precisa ser relativa ao workspace.");
        var path = Path.GetFullPath(requested, _root);
        var relative = Path.GetRelativePath(_root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A referência de evidência está fora do workspace.");
        LabWorkspace.RefuseBlindInput(path, _root);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(_root, outputPath, [path]);
        if (reference.Contains('#') || pointer is null || !TryParsePointer(pointer, out var segments))
            return new(null, "evidence-value-invalid");

        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumArtifactBytes || stream.Length > MaximumTotalBytes - _bytesRead)
                throw new InvalidDataException("Artefato de evidência excede o orçamento de leitura.");
            using var memory = new MemoryStream((int)stream.Length);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer)) != 0)
            {
                _bytesRead += read;
                if (_bytesRead > MaximumTotalBytes || memory.Length + read > MaximumArtifactBytes)
                    throw new InvalidDataException("Artefato de evidência excedeu o orçamento durante a leitura.");
                memory.Write(buffer, 0, read);
            }
            bytes = memory.ToArray();
        }
        catch (FileNotFoundException) { return new(null, "evidence-integrity-failed"); }
        catch (DirectoryNotFoundException) { return new(null, "evidence-integrity-failed"); }
        catch (InvalidDataException) { return new(null, "evidence-integrity-failed"); }

        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            return new(null, "evidence-integrity-failed");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            JsonContractValidation.RequireUniqueProperties(document.RootElement);
            var value = Resolve(document.RootElement, segments);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var parsed) || !double.IsFinite(parsed))
                return new(actual, "evidence-value-invalid");
            return new(actual, parsed == expectedValue ? "verified" : "evidence-value-mismatch");
        }
        catch (JsonException) { return new(actual, "evidence-value-invalid"); }
        catch (InvalidDataException) { return new(actual, "evidence-value-invalid"); }
    }

    private static bool TryParsePointer(string pointer, out string[] segments)
    {
        if (pointer.Length == 0) { segments = []; return true; }
        if (!pointer.StartsWith('/')) { segments = []; return false; }
        var encoded = pointer[1..].Split('/');
        segments = new string[encoded.Length];
        for (var i = 0; i < encoded.Length; i++)
        {
            var builder = new System.Text.StringBuilder(encoded[i].Length);
            for (var j = 0; j < encoded[i].Length; j++)
            {
                if (encoded[i][j] != '~') { builder.Append(encoded[i][j]); continue; }
                if (++j >= encoded[i].Length || encoded[i][j] is not ('0' or '1')) return false;
                builder.Append(encoded[i][j] == '0' ? '~' : '/');
            }
            segments[i] = builder.ToString();
        }
        return true;
    }

    private static JsonElement Resolve(JsonElement root, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out var child)) throw new InvalidDataException("JSON Pointer não encontrado.");
                current = child;
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                if (segment.Length == 0 || (segment.Length > 1 && segment[0] == '0') ||
                    !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index < 0 || index >= current.GetArrayLength())
                    throw new InvalidDataException("Índice de array inválido no JSON Pointer.");
                current = current[index];
            }
            else throw new InvalidDataException("JSON Pointer atravessa um valor escalar.");
        }
        return current;
    }

    internal sealed record PointerVerification(string? ArtifactSha256, string Status);
}
