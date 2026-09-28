using System.Diagnostics.CodeAnalysis;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Bounded validation of the connectable Unix subset of the D-Bus address grammar.</summary>
/// <remarks>
/// Tmds 0.94.1 exposes no public address parser. Validate without decoding/reconstructing the
/// address handed to that library. Only path/abstract and the optional transport GUID are needed;
/// noncefile belongs to nonce-tcp, while dir/tmpdir/runtime describe listening addresses.
/// </remarks>
internal static class SecretServiceSessionAddress
{
    internal const int MaximumAddressCharacters = 4096;
    internal const int MaximumAlternatives = 8;
    // sockaddr_un.sun_path is 108 bytes on Linux, including the NUL terminator or abstract prefix.
    internal const int MaximumSocketNameBytes = 107;

    public static bool IsValid([NotNullWhen(true)] string? address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (address is null || address.Length is 0 or > MaximumAddressCharacters)
        {
            return false;
        }

        var text = address.AsSpan();
        var alternatives = 0;
        foreach (var range in text.Split(';'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++alternatives > MaximumAlternatives || !IsValidAlternative(text[range]))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static bool IsValidAlternative(ReadOnlySpan<char> alternative)
    {
        if (!alternative.StartsWith("unix:", StringComparison.Ordinal))
        {
            return false;
        }

        var fields = alternative[5..];
        var hasEndpoint = false;
        var hasGuid = false;
        Span<byte> decoded = stackalloc byte[MaximumSocketNameBytes];
        foreach (var range in fields.Split(','))
        {
            var field = fields[range];
            var separator = field.IndexOf('=');
            if (separator <= 0 || !TryDecodeValue(field[(separator + 1)..], decoded, out var bytes))
            {
                return false;
            }

            var key = field[..separator];
            if (key.SequenceEqual("path") || key.SequenceEqual("abstract"))
            {
                if (hasEndpoint)
                {
                    return false;
                }

                hasEndpoint = true;
            }
            else if (key.SequenceEqual("guid"))
            {
                if (hasGuid || bytes != 32)
                {
                    return false;
                }

                for (var index = 0; index < bytes; index++)
                {
                    if (!char.IsAsciiHexDigit((char)decoded[index]))
                    {
                        return false;
                    }
                }

                hasGuid = true;
            }
            else
            {
                return false;
            }
        }

        return hasEndpoint;
    }

    private static bool TryDecodeValue(ReadOnlySpan<char> value, Span<byte> destination, out int written)
    {
        written = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (written == destination.Length)
            {
                return false;
            }

            var character = value[index];
            byte decoded;
            if (character == '%')
            {
                if (index + 2 >= value.Length || !char.IsAsciiHexDigit(value[index + 1]) || !char.IsAsciiHexDigit(value[index + 2]))
                {
                    return false;
                }

                decoded = (byte)((HexDigit(value[index + 1]) << 4) | HexDigit(value[index + 2]));
                index += 2;
            }
            else if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '/' or '.' or '*')
            {
                decoded = (byte)character;
            }
            else
            {
                return false;
            }

            if (decoded == 0)
            {
                return false;
            }

            destination[written++] = decoded;
        }

        return written > 0;
    }

    private static int HexDigit(char value) => value <= '9' ? value - '0' : (value | 0x20) - 'a' + 10;
}
