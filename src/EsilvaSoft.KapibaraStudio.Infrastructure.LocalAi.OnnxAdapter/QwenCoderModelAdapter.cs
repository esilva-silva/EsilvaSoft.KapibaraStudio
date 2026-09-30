using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public sealed class QwenCoderModelAdapter : IModelAdapter
{
    public string Architecture => "Qwen2.5-Coder";
    public string PromptFormat => LocalModelPromptFormats.QwenFim;
    public string Description => "Qwen2.5-Coder FIM (qwen2)";
    public bool CanHandle(string modelType, string root) => modelType == "qwen2";

    public ModelAdapterFailure? Validate(ModelFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var tokens = folder.Tokenizer.GetProperty("added_tokens").EnumerateArray()
            .Select(token => token.GetProperty("content").GetString()).ToHashSet(StringComparer.Ordinal);
        return QwenFimPromptBuilder.SpecialTokens.All(tokens.Contains) ? null
            : new(LocalModelState.Unsupported, "Tokenizer sem FIM do Qwen Coder. Autocomplete básico ativo.");
    }

    public ICompletionPromptBuilder CreatePromptBuilder() => new QwenFimPromptBuilder();
    public ITokenizer CreateTokenizer(Model model, string root) => new OnnxModelTokenizer(model);

    public IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        return QwenFimPromptBuilder.SpecialTokens.Concat(["<|endoftext|>", "<|im_end|>", "<|fim_pad|>"])
            .Select(tokenizer.Encode).Where(ids => ids.Count == 1).Select(ids => ids[0]).ToHashSet();
    }
}
