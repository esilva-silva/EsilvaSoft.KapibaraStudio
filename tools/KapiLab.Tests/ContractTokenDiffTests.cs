using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ContractTokenDiffTests
{
    private static readonly TokenizerPackageIdentity Identity = new("Qwen2.5-Coder", "qwen-fim-v1", "cfg", "tok", "tokcfg", "");

    [Test]
    public void SameContractAndIdsMatchIndependentOfRecordOrder()
    {
        var first = Record("a", "prefix", "suffix", [1, 2, 3]);
        var second = Record("b", "other", "tail", [4, 5]);

        var result = ContractTokenDiff.Compare([first, second], [second, first]);

        Assert.That(result.Matches, Is.True);
        Assert.That(result.Differences, Is.Empty);
        Assert.That(result.TokenizerIdentityMatches, Is.True);
    }

    [Test]
    public void ReportsExactTextAndTokenFieldsPerRecord()
    {
        var expected = Record("sample", "prefix", "suffix", [1, 2, 3]);
        var actual = Record("sample", "changed", "suffix", [1, 9, 3]);

        var result = ContractTokenDiff.Compare([expected], [actual]);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Differences, Has.Count.EqualTo(1));
        Assert.That(result.Differences[0].Id, Is.EqualTo("sample"));
        Assert.That(result.Differences[0].Fields, Is.EquivalentTo(["model_prefix", "token_ids"]));
    }

    [Test]
    public void MissingRecordAndTokenizerDriftDoNotMatch()
    {
        var expected = Record("sample", "prefix", "suffix", [1]);
        var otherIdentity = new TokenizerPackageIdentity("Qwen3", "qwen3-chatml-v1", "cfg3", "tok3", "tokcfg3", "");
        var actual = Record("different", "prefix", "suffix", [1]) with { Tokenizer = otherIdentity };

        var result = ContractTokenDiff.Compare([expected], [actual]);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.TokenizerIdentityMatches, Is.False);
        Assert.That(result.Differences.Select(difference => difference.Id), Is.EquivalentTo(["different", "sample"]));
        Assert.That(result.Differences.Single(difference => difference.Id == "different").Fields, Is.EquivalentTo(["record"]));
    }

    [Test]
    public void DuplicateIdsAreInvalidInsteadOfSilentlyOverwritten()
    {
        var duplicate = Record("same", "p", "s", [1]);

        Assert.Throws<InvalidDataException>(() => ContractTokenDiff.Compare([duplicate, duplicate], []));
    }

    [Test]
    public void EmptySnapshotsAreIncompleteAndNeverMatch()
    {
        var result = ContractTokenDiff.Compare([], []);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Complete, Is.False);
        Assert.That(result.TokenizerIdentityMatches, Is.False);
    }

    [Test]
    public void InconsistentTokenizerIdentityWithinOneSnapshotIsDrift()
    {
        var secondIdentity = new TokenizerPackageIdentity("Qwen3", "qwen3-chatml-v1", "cfg3", "tok3", "tokcfg3", "");
        var first = Record("a", "p", "s", [1]);
        var second = Record("b", "p", "s", [1]) with { Tokenizer = secondIdentity };

        var result = ContractTokenDiff.Compare([first, second], [first, second]);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.TokenizerIdentityMatches, Is.False);
    }

    [Test]
    public void ContextBudgetIsPartOfTheComparedContract()
    {
        var expected = Record("a", "p", "s", [1]);
        var actual = Record("a", "p", "s", [1]) with { ContextTokens = 4096 };

        var result = ContractTokenDiff.Compare([expected], [actual]);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.TokenizerIdentityMatches, Is.False);
        Assert.That(result.Differences.Single().Fields, Is.EquivalentTo(["context_tokens"]));
    }

    private static TokenizedContractRecord Record(string id, string prefix, string suffix, IReadOnlyList<int> tokens) =>
        new("kapilab-tokenized-contract-v1", id, "editor-context-v1", Hash(prefix), Hash(suffix), 2048, Identity, tokens);

    [Test]
    public void RequestedAndEffectiveBudgetsParticipateInContractIdentity()
    {
        var prefix = "prefix";
        var suffix = "suffix";
        var baseline = new TokenizedContractRecord("kapilab-tokenized-contract-v2", "sample", "editor-context-v1",
            Hash(prefix), Hash(suffix), 2013, Identity, [1, 2], 2048, 32);
        var changed = baseline with { RequestedContextTokens = 1024 };

        var result = ContractTokenDiff.Compare([baseline], [changed]);

        Assert.That(result.Matches, Is.False);
        Assert.That(result.Differences.Single().Fields, Does.Contain("requested_context_tokens"));
        Assert.That(result.TokenizerIdentityMatches, Is.False);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
