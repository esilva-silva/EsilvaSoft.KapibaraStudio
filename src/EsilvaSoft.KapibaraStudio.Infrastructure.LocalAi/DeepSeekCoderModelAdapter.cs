using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public sealed class DeepSeekCoderModelAdapter : IModelAdapter
{
    public const string ManifestFileName = "slopcoder_manifest.json";
    public string Architecture => "DeepSeek-Coder";
    public string PromptFormat => LocalModelPromptFormats.DeepSeekCoderFim;
    public string Description => "DeepSeek-Coder FIM (llama com slopcoder_manifest.json)";
    public bool CanHandle(string modelType, string root) => modelType == "llama" && File.Exists(Path.Combine(root, ManifestFileName));

    public ModelAdapterFailure? Validate(ModelFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var manifest = LocalModelCatalog.ReadJson(Path.Combine(folder.Root, ManifestFileName));
        var prompt = manifest.RootElement.GetProperty("prompt");
        if (prompt.GetProperty("format").GetString() != LocalModelPromptFormats.DeepSeekCoderFim) throw new InvalidDataException();
        var added = folder.Tokenizer.GetProperty("added_tokens").EnumerateArray()
            .ToDictionary(token => token.GetProperty("content").GetString()!, token => token.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        foreach (var (key, expected) in DeepSeekFimPromptBuilder.Tokens)
            if (prompt.GetProperty("tokens").GetProperty(key).GetString() != expected.Text
                || prompt.GetProperty("token_ids").GetProperty(key).GetInt32() != expected.Id
                || !added.TryGetValue(expected.Text, out var id) || id != expected.Id) throw new InvalidDataException();
        var stops = manifest.RootElement.GetProperty("generation").GetProperty("stop_token_ids").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
        if (!stops.SetEquals(StopIds())) throw new InvalidDataException();
        var data = folder.DecoderPath + ".data";
        return File.Exists(data) && new FileInfo(data).Length > 0 ? null
            : new(LocalModelState.MissingFiles, $"Arquivos ausentes: {Path.GetFileName(data)} (pesos externos do decoder).");
    }

    public ICompletionPromptBuilder CreatePromptBuilder() => new DeepSeekFimPromptBuilder();
    public ITokenizer CreateTokenizer(Model model, string root) => new DeepSeekModelTokenizer(Path.Combine(root, "tokenizer.json"));
    public IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer) => StopIds().ToHashSet();
    private static IEnumerable<int> StopIds() => DeepSeekFimPromptBuilder.Tokens.Where(token => token.Key != "bos").Select(token => token.Value.Id);
}
