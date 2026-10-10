using System.Security.Cryptography;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class ContractTokenDiff
{
    public static TokenDiffResult Compare(IReadOnlyList<TokenizedContractRecord> expected,
        IReadOnlyList<TokenizedContractRecord> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var differences = new List<TokenRecordDifference>();
        var expectedById = Unique(expected, "expected");
        var actualById = Unique(actual, "actual");
        foreach (var id in expectedById.Keys.Union(actualById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!expectedById.TryGetValue(id, out var left))
            {
                differences.Add(new(id, ["record"]));
                continue;
            }
            if (!actualById.TryGetValue(id, out var right))
            {
                differences.Add(new(id, ["record"]));
                continue;
            }

            var fields = new List<string>();
            if (!string.Equals(left.Schema, right.Schema, StringComparison.Ordinal)) fields.Add("schema");
            if (!string.Equals(left.Contract, right.Contract, StringComparison.Ordinal)) fields.Add("contract");
            if (!string.Equals(left.ModelPrefixSha256, right.ModelPrefixSha256, StringComparison.Ordinal)) fields.Add("model_prefix");
            if (!string.Equals(left.SuffixSha256, right.SuffixSha256, StringComparison.Ordinal)) fields.Add("suffix");
            if (!string.Equals(left.Tokenizer.Family, right.Tokenizer.Family, StringComparison.Ordinal)
                || !string.Equals(left.Tokenizer.PromptFormat, right.Tokenizer.PromptFormat, StringComparison.Ordinal)
                || !string.Equals(left.Tokenizer.GenAiConfigSha256, right.Tokenizer.GenAiConfigSha256, StringComparison.Ordinal)
                || !string.Equals(left.Tokenizer.TokenizerSha256, right.Tokenizer.TokenizerSha256, StringComparison.Ordinal)
                || !string.Equals(left.Tokenizer.TokenizerConfigSha256, right.Tokenizer.TokenizerConfigSha256, StringComparison.Ordinal)
                || !string.Equals(left.Tokenizer.AdapterConfigSha256, right.Tokenizer.AdapterConfigSha256, StringComparison.Ordinal))
                fields.Add("tokenizer_identity");
            if (left.ContextTokens != right.ContextTokens) fields.Add("context_tokens");
            if (!left.TokenIds.SequenceEqual(right.TokenIds)) fields.Add("token_ids");
            if (left.RequestedContextTokens != right.RequestedContextTokens) fields.Add("requested_context_tokens");
            if (left.CompletionTokens != right.CompletionTokens) fields.Add("completion_tokens");
            if (fields.Count > 0) differences.Add(new(id, fields));
        }

        var identityMatches = expected.Count > 0 && actual.Count > 0
            && IsConsistent(expected) && IsConsistent(actual)
            && expected[0].Tokenizer == actual[0].Tokenizer
            && expected[0].ContextTokens == actual[0].ContextTokens
            && expected[0].RequestedContextTokens == actual[0].RequestedContextTokens
            && expected[0].CompletionTokens == actual[0].CompletionTokens;
        var complete = expected.Count > 0 && actual.Count > 0;
        return new(complete && expected.Count == actual.Count && differences.Count == 0 && identityMatches,
            expected.Count, actual.Count, identityMatches, complete, differences);
    }

    private static bool IsConsistent(IReadOnlyList<TokenizedContractRecord> records) =>
        records.Count == 0 || records.All(record => record.Tokenizer == records[0].Tokenizer
            && record.ContextTokens == records[0].ContextTokens
            && record.RequestedContextTokens == records[0].RequestedContextTokens
            && record.CompletionTokens == records[0].CompletionTokens);

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static Dictionary<string, TokenizedContractRecord> Unique(IReadOnlyList<TokenizedContractRecord> records, string side)
    {
        var result = new Dictionary<string, TokenizedContractRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Id) || !result.TryAdd(record.Id, record))
                throw new InvalidDataException($"Registros {side} contêm id vazio ou duplicado.");
        }
        return result;
    }
}

internal sealed record TokenizerPackageIdentity(string Family, string PromptFormat, string GenAiConfigSha256,
    string TokenizerSha256, string TokenizerConfigSha256, string AdapterConfigSha256);
internal sealed record TokenizedContractRecord(string Schema, string Id, string Contract, string ModelPrefixSha256,
    string SuffixSha256, int ContextTokens, TokenizerPackageIdentity Tokenizer, IReadOnlyList<int> TokenIds,
    int? RequestedContextTokens = null, int? CompletionTokens = null);
internal sealed record TokenRecordDifference(string Id, IReadOnlyList<string> Fields);
internal sealed record TokenDiffResult(bool Matches, int ExpectedRecords, int ActualRecords,
    bool TokenizerIdentityMatches, bool Complete, IReadOnlyList<TokenRecordDifference> Differences);
