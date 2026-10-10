namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// Local comparison primitives for parity evidence already produced by a real runtime.
/// This class does not run a tokenizer/model or manufacture token/logit observations.
/// </summary>
internal static class ParityComparison
{
    public const string ToleranceSchema = "kapilab-parity-tolerances-v1";

    public static ParityResult CompareTokenizer(IReadOnlyList<int> expected, IReadOnlyList<int> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var complete = expected.Count > 0 && actual.Count > 0;
        var equal = complete && expected.SequenceEqual(actual);
        return new("tokenizer", complete ? equal ? "matched" : "mismatched" : "inconclusive",
            expected.Count, actual.Count, equal ? 0 : CountDifferences(expected, actual), null, null, null);
    }

    public static ParityResult ComparePrompt(ReadOnlySpan<byte> expectedUtf8, ReadOnlySpan<byte> actualUtf8)
    {
        var complete = !expectedUtf8.IsEmpty && !actualUtf8.IsEmpty;
        var equal = complete && expectedUtf8.SequenceEqual(actualUtf8);
        return new("prompt", complete ? equal ? "matched" : "mismatched" : "inconclusive",
            expectedUtf8.Length, actualUtf8.Length, equal ? 0 : null, null, null, null);
    }

    public static ParityResult CompareGreedy(IReadOnlyList<int> expected, IReadOnlyList<int> actual,
        ParityTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        Validate(tolerances);
        var complete = expected.Count > 0 && actual.Count > 0;
        if (!complete)
            return new("greedy", "inconclusive", expected.Count, actual.Count, null, null, null, "genai-direct");

        var differences = CountDifferences(expected, actual);
        var mismatchRate = (double)differences / Math.Max(expected.Count, actual.Count);
        var matched = mismatchRate <= tolerances.GreedyMaximumTokenMismatchRate;
        return new("greedy", matched ? "matched" : "mismatched", expected.Count, actual.Count,
            differences, mismatchRate, tolerances.GreedyMaximumTokenMismatchRate, "genai-direct");
    }

    public static ParityResult CompareTeacherForcing(IReadOnlyList<double> expectedLogits,
        IReadOnlyList<double> actualLogits, ParityTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(expectedLogits);
        ArgumentNullException.ThrowIfNull(actualLogits);
        Validate(tolerances);
        if (expectedLogits.Any(value => !double.IsFinite(value)) || actualLogits.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Logits devem ser valores finitos.");

        var complete = expectedLogits.Count > 0 && actualLogits.Count > 0;
        if (!complete)
            return new("teacher", "inconclusive", expectedLogits.Count, actualLogits.Count, null, null, null, "genai-direct");

        var sameShape = expectedLogits.Count == actualLogits.Count;
        var maximumDifference = sameShape
            ? expectedLogits.Zip(actualLogits, static (left, right) => Math.Abs(left - right)).Max()
            : (double?)null;
        var matched = sameShape && maximumDifference is double observed
            && observed <= tolerances.TeacherMaximumAbsoluteLogitDifference;
        return new("teacher", matched ? "matched" : "mismatched", expectedLogits.Count, actualLogits.Count,
            null, maximumDifference, tolerances.TeacherMaximumAbsoluteLogitDifference, "genai-direct");
    }

    private static int CountDifferences(IReadOnlyList<int> expected, IReadOnlyList<int> actual)
    {
        var differences = Math.Abs(expected.Count - actual.Count);
        for (var index = 0; index < Math.Min(expected.Count, actual.Count); index++)
            if (expected[index] != actual[index]) differences++;
        return differences;
    }

    private static void Validate(ParityTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(tolerances);
        if (tolerances.Schema != ToleranceSchema || string.IsNullOrWhiteSpace(tolerances.Version)
            || !double.IsFinite(tolerances.GreedyMaximumTokenMismatchRate)
            || tolerances.GreedyMaximumTokenMismatchRate is < 0 or > 1
            || !double.IsFinite(tolerances.TeacherMaximumAbsoluteLogitDifference)
            || tolerances.TeacherMaximumAbsoluteLogitDifference < 0)
            throw new InvalidDataException("Arquivo de tolerâncias de paridade inválido ou não versionado.");
    }
}

internal sealed record ParityTolerances(string Schema, string Version, double GreedyMaximumTokenMismatchRate,
    double TeacherMaximumAbsoluteLogitDifference);

internal sealed record ParityResult(string Level, string Status, int ExpectedCount, int ActualCount,
    int? DifferenceCount, double? Observed, double? Tolerance, string? Path);
