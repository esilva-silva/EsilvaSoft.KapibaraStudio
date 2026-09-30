using EsilvaSoft.KapibaraStudio.Infrastructure;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Decodificação incremental (R42): o texto sai conforme os tokens chegam, sem redecodificar a sequência, e sem
/// nunca entregar metade de um caractere.
/// </summary>
/// <remarks>
/// A equivalência com a decodificação em lote é o contrato que sustenta o critério "streaming idêntico ao não
/// streaming": se a concatenação dos pedaços divergisse do lote, a prévia inline mostraria um texto e a aplicação
/// inseriria outro.
/// </remarks>
[TestFixture]
[Category("Integration")]
public sealed class TokenizerStreamingIntegrationTests
{
    private const int EotId = 256;
    private static readonly string[] Texts =
    [
        "db.getCollection(\"clientes\").find({})",
        "informação de endereço não encontrada",
        "中文注释与分词",
        "status: 😀 concluído 🎉",
        "é decomposto e a​ de largura zero"
    ];

    /// <summary>Byte a byte é o pior caso possível: todo caractere multibyte nasce partido.</summary>
    [Test]
    public void TheDeepSeekDecoderStreamsFragmentedCharacters([ValueSource(nameof(Texts))] string text)
    {
        using var workspace = new SyntheticDirectory();
        ITokenizer tokenizer = new DeepSeekModelTokenizer(FabricatedByteLevelTokenizer(workspace.Path), new LocalModelFileAccess());
        var tokens = tokenizer.Encode(text);
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var assembled = new StringBuilder();
        foreach (var token in tokens)
        {
            var piece = decoder.Append(token);
            Assert.That(piece, Does.Not.Contain("�"));
            assembled.Append(piece);
        }
        assembled.Append(decoder.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(tokenizer.Decode(tokens)));
        Assert.That(assembled.ToString(), Is.EqualTo(text));
    }

    /// <summary>Marcadores adicionados não são bytes: entram inteiros no fluxo, sem estragar o caractere seguinte.</summary>
    [Test]
    public void TheDeepSeekDecoderKeepsAddedMarkersWholeBetweenFragmentedCharacters()
    {
        using var workspace = new SyntheticDirectory();
        ITokenizer tokenizer = new DeepSeekModelTokenizer(FabricatedByteLevelTokenizer(workspace.Path), new LocalModelFileAccess());
        // "ç" partido ao meio pelo marcador não existe em geração real; o que existe é marcador entre caracteres.
        var tokens = tokenizer.Encode("ção").Concat([EotId]).Concat(tokenizer.Encode("中")).ToArray();
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var assembled = new StringBuilder();
        foreach (var token in tokens) assembled.Append(decoder.Append(token));
        assembled.Append(decoder.Flush());
        Assert.That(assembled.ToString(), Is.EqualTo(tokenizer.Decode(tokens)));
        Assert.That(assembled.ToString(), Is.EqualTo("ção<|EOT|>中"));
    }

    private static string FabricatedByteLevelTokenizer(string directory)
    {
        var characters = ByteLevelCharacters();
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var value = 0; value < 256; value++) vocabulary[characters[value].ToString()] = value;
        var document = new
        {
            normalizer = (object?)null,
            pre_tokenizer = new { pretokenizers = new[] { new { type = "ByteLevel", add_prefix_space = false, use_regex = false } } },
            added_tokens = new[] { new { content = "<|EOT|>", id = EotId } },
            model = new { type = "BPE", byte_fallback = false, unk_token = (object?)null, vocab = vocabulary, merges = Array.Empty<string>() }
        };
        var path = Path.Combine(directory, "tokenizer.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return path;
    }

    /// <summary>Mesma tabela ByteLevel do GPT-2 usada pelo tokenizer real.</summary>
    private static char[] ByteLevelCharacters()
    {
        var result = new char[256];
        var next = 256;
        for (var value = 0; value < result.Length; value++)
            result[value] = (char)(value is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 and <= 255 ? value : next++);
        return result;
    }

}
