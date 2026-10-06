using System.Buffers.Binary;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Recognizes native PE/ELF files without starting the candidate or loading its code.</summary>
internal static class NativeCopilotExecutableProbe
{
    private const int Elf32HeaderBytes = 52;
    private const int Elf64HeaderBytes = 64;
    internal static bool IsNativeExecutable(string path, bool windows)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[64];
        var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (windows)
        {
            if (length < header.Length || header[0] != 'M' || header[1] != 'Z') return false;
            var offset = BinaryPrimitives.ReadInt32LittleEndian(header[60..]);
            if (offset < header.Length || offset > stream.Length - 24) return false;
            stream.Position = offset;
            Span<byte> pe = stackalloc byte[24];
            return stream.ReadAtLeast(pe, pe.Length, throwOnEndOfStream: false) == pe.Length &&
                pe[0] == 'P' && pe[1] == 'E' && pe[2] == 0 && pe[3] == 0 &&
                (BinaryPrimitives.ReadUInt16LittleEndian(pe[22..]) & 0x2002) == 0x0002;
        }

        if (length < 20 || header[0] != 0x7f || header[1] != 'E' || header[2] != 'L' || header[3] != 'F' ||
            header[4] is not (1 or 2) || header[5] is not (1 or 2) || header[6] != 1) return false;
        // EI_CLASS determines the complete ELF header size; a magic/type prefix alone may be a truncated file.
        if (length < (header[4] == 1 ? Elf32HeaderBytes : Elf64HeaderBytes)) return false;
        var type = header[5] == 1
            ? BinaryPrimitives.ReadUInt16LittleEndian(header[16..])
            : BinaryPrimitives.ReadUInt16BigEndian(header[16..]);
        return type is 2 or 3; // Executable or position-independent executable; scripts are never native CLI candidates.
    }
}
