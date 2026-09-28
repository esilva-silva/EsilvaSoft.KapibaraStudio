using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

/// <summary>Half-open range [Start, End) of UTF-16 code units; offsets are identical to AvaloniaEdit document offsets.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int Start { get; } = Start >= 0 ? Start : throw new ArgumentOutOfRangeException(nameof(Start), Start, "O início não pode ser negativo.");
    public int Length { get; } = Length >= 0 && (long)Start + Length <= int.MaxValue
        ? Length : throw new ArgumentOutOfRangeException(nameof(Length), Length, "O tamanho deve ser não negativo e caber no documento.");

    public int End => Start + Length;
    public bool IsEmpty => Length == 0;

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new(start, end - start);
    }

    /// <summary>Start ≤ position &lt; End; an empty span contains no position.</summary>
    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    /// <summary>Caret semantics: Start ≤ position ≤ End, so a caret touching either edge (or an empty span) intersects.</summary>
    public bool IntersectsWith(int position) => position >= Start && position <= End;

    /// <summary>True when both spans share at least one code unit.</summary>
    public bool OverlapsWith(TextSpan span) => Math.Max(Start, span.Start) < Math.Min(End, span.End);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Start}..{End})");
}
