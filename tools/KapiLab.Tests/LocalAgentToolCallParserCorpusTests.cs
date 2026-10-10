using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LocalAgentToolCallParserCorpusTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record Corpus(string Schema, string Contract, string ContractSource, Vector[] Vectors);
    private sealed record Vector(string Id, string[] Fragments, Expected Expected);
    private sealed record Expected(bool Valid, string? ToolName, string? ArgumentsJson, string? ErrorCode);

    [Test]
    public void VersionedIndependentCorpusProcessesAllUniqueParserVectors()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "local-agent-tool-call-parser-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-local-agent-tool-call-parser-corpus-v1"));
        Assert.That(root.GetProperty("contract").GetString(), Is.EqualTo("qwen3-hermes-tool-call-envelope-v1"));
        Assert.That(root.GetProperty("contractSource").GetString(), Does.Contain("arguments object depth maximum 8"));

        var corpus = JsonSerializer.Deserialize<Corpus>(document.RootElement.GetRawText(), SerializerOptions)!;
        Assert.That(corpus.Vectors, Has.Length.EqualTo(200));
        Assert.That(LocalAgentToolCallParser.MaximumArgumentsJsonDepth, Is.EqualTo(8));
        Assert.That(corpus.Vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(200));
        Assert.That(corpus.Vectors.Select(vector => string.Concat(vector.Fragments)).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(200),
            "each vector must provide a distinct input, not merely a distinct label");

        foreach (var vector in corpus.Vectors)
        {
            Assert.That(vector.Fragments, Is.Not.Empty, vector.Id);
            var parser = new LocalAgentToolCallParser();
            var visible = string.Empty;
            for (var index = 0; index < vector.Fragments.Length; index++)
                visible += parser.Append(vector.Fragments[index], final: index == vector.Fragments.Length - 1);

            Assert.Multiple(() =>
            {
                Assert.That(visible, Is.Empty, vector.Id);
                Assert.That(parser.ErrorCode, Is.EqualTo(vector.Expected.ErrorCode), vector.Id);
                Assert.That(parser.ToolName, Is.EqualTo(vector.Expected.ToolName), vector.Id);
                Assert.That(parser.ArgumentsJson, Is.EqualTo(vector.Expected.ArgumentsJson), vector.Id);
                Assert.That((parser.ErrorCode is null), Is.EqualTo(vector.Expected.Valid), vector.Id);
            });
        }
    }
}
