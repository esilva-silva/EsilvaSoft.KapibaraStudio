using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public sealed class QwenFimPromptBuilder : ICompletionPromptBuilder
{
    public static IReadOnlyList<string> SpecialTokens { get; } = Array.AsReadOnly(new[] { "<|fim_prefix|>", "<|fim_suffix|>", "<|fim_middle|>" });
    public IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        var markers = SpecialTokens.Select(tokenizer.Encode).ToArray();
        if (markers.Any(m => m.Count != 1)) throw new InvalidDataException("Tokenizer não oferece os tokens FIM do Qwen Coder.");
        var budget = contextTokens - 3;
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        var before = tokenizer.Encode(prefix); var after = tokenizer.Encode(suffix);
        var suffixCount = Math.Min(after.Count, budget / 4);
        var prefixCount = Math.Min(before.Count, budget - suffixCount);
        suffixCount = Math.Min(after.Count, budget - prefixCount);
        return markers[0].Concat(before.Skip(before.Count - prefixCount)).Concat(markers[1])
            .Concat(after.Take(suffixCount)).Concat(markers[2]).ToArray();
    }
}
