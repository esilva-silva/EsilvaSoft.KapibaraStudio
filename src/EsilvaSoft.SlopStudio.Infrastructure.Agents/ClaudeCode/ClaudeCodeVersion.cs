using System.Globalization;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Versão numérica <c>major.minor.patch</c> do Claude Code, lida de <c>claude --version</c> (formato observado no
/// spike P7-CL0-01: <c>2.1.268 (Claude Code)</c>). Somente os três números são interpretados; o restante da saída é
/// descartado e nunca exibido.
/// </summary>
public readonly record struct ClaudeCodeVersion(int Major, int Minor, int Patch) : IComparable<ClaudeCodeVersion>
{
    public int CompareTo(ClaudeCodeVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ClaudeCodeVersion left, ClaudeCodeVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ClaudeCodeVersion left, ClaudeCodeVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ClaudeCodeVersion left, ClaudeCodeVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ClaudeCodeVersion left, ClaudeCodeVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    /// <summary>
    /// Lê a primeira palavra de uma saída curta. Aceita somente <c>N.N.N</c> (cada parte com até 6 dígitos ASCII),
    /// opcionalmente seguido de espaço e texto livre; qualquer outra forma é recusada.
    /// </summary>
    public static bool TryParse(string? text, out ClaudeCodeVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
        {
            return false;
        }

        var trimmed = text.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var token = end < 0 ? trimmed : trimmed[..end];
        var parts = token.Split('.');
        if (parts.Length != 3 || !TryParsePart(parts[0], out var major) || !TryParsePart(parts[1], out var minor) ||
            !TryParsePart(parts[2], out var patch))
        {
            return false;
        }

        version = new ClaudeCodeVersion(major, minor, patch);
        return true;
    }

    private static bool TryParsePart(string part, out int value)
    {
        value = 0;
        return part.Length is > 0 and <= 6 && part.All(char.IsAsciiDigit) &&
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
