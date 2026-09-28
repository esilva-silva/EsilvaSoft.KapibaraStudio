using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public sealed class DeepSeekFimPromptBuilder : ICompletionPromptBuilder
{
    private static readonly int[] Beginning = [32013, 32016];
    public static IReadOnlyDictionary<string, (string Text, int Id)> Tokens { get; } =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["bos"] = ("<｜begin▁of▁sentence｜>", 32013), ["eos"] = ("<｜end▁of▁sentence｜>", 32014),
            ["fim_hole"] = ("<｜fim▁hole｜>", 32015), ["fim_begin"] = ("<｜fim▁begin｜>", 32016),
            ["fim_end"] = ("<｜fim▁end｜>", 32017), ["eot"] = ("<|EOT|>", 32021)
        }.AsReadOnly();

    public IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        var budget = contextTokens - 4;
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var before = tokenizer.Encode(prefix);
        var after = tokenizer.Encode(suffix);
        var suffixCount = Math.Min(after.Count, budget / 4);
        var prefixCount = Math.Min(before.Count, budget - suffixCount);
        suffixCount = Math.Min(after.Count, budget - prefixCount);
        return Beginning.Concat(before.Skip(before.Count - prefixCount))
            .Append(32015).Concat(after.Take(suffixCount)).Append(32017).ToArray();
    }
}
