using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Release version with SemVer 2 precedence; build metadata (<c>+sha</c>) never affects ordering.</summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly string[] _prerelease;

    private AppVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major; Minor = minor; Patch = patch; _prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public bool IsPrerelease => _prerelease.Length > 0;

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versão inválida: '{text}'.");

    /// <summary>Accepts an optional leading <c>v</c>, as in release tags.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        var value = text?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (value.IndexOf('+') is var plus and >= 0) value = value[..plus];
        string[] prerelease = [];
        if (value.IndexOf('-') is var dash and >= 0)
        {
            prerelease = value[(dash + 1)..].Split('.');
            value = value[..dash];
            if (prerelease.Any(identifier => identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || IsNumeric(identifier) && !HasNoLeadingZero(identifier))) return false;
        }
        var parts = value.Split('.');
        if (parts.Length != 3 || !TryParseNumber(parts[0], out var major) || !TryParseNumber(parts[1], out var minor) || !TryParseNumber(parts[2], out var patch))
            return false;
        version = new(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        // A final release ranks above any pre-release of the same core version.
        if (_prerelease.Length == 0 || other._prerelease.Length == 0) return other._prerelease.Length.CompareTo(_prerelease.Length);
        for (var index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
        {
            result = CompareIdentifier(_prerelease[index], other._prerelease[index]);
            if (result != 0) return result;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _prerelease));
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}{(_prerelease.Length == 0 ? "" : "-" + string.Join('.', _prerelease))}");

    public static bool operator ==(AppVersion? left, AppVersion? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(AppVersion? left, AppVersion? right) => !(left == right);
    public static bool operator <(AppVersion? left, AppVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;
    public static bool operator <=(AppVersion? left, AppVersion? right) => left is null || left.CompareTo(right) <= 0;
    public static bool operator >(AppVersion? left, AppVersion? right) => left is not null && left.CompareTo(right) > 0;
    public static bool operator >=(AppVersion? left, AppVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    private static bool TryParseNumber(string part, out int value)
    {
        value = 0;
        return IsNumeric(part) && HasNoLeadingZero(part) && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsNumeric(string part) => part.Length > 0 && part.All(char.IsAsciiDigit);
    private static bool HasNoLeadingZero(string part) => part == "0" || part[0] != '0';

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
            return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
        return leftNumeric ? -1 : rightNumeric ? 1 : string.CompareOrdinal(left, right);
    }
}
