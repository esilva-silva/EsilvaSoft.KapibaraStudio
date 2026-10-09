using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>
/// Architecture contract of an ONNX GenAI export: structural validation, tokenizer, prompt format and stop tokens.
/// A new model family is added as an adapter instead of architecture checks spread through the application.
/// </summary>
public interface IModelAdapter
{
    string Architecture { get; }
    string PromptFormat { get; }
    /// <summary>Shown when no adapter accepts a folder.</summary>
    string Description { get; }
    LocalModelCapabilities DefaultCapabilities => LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Chat | LocalModelCapabilities.Fim;
    bool CanHandle(string modelType, string root);
    /// <summary>Returns a user-facing failure, or throws <see cref="InvalidDataException"/> for malformed files.</summary>
    ModelAdapterFailure? Validate(ModelFolder folder);
    ICompletionPromptBuilder CreatePromptBuilder();
    ITokenizer CreateTokenizer(Model model, string root);
    IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer);
    /// <summary>Null for legacy FIM adapters; called only after the tokenizer is loaded.</summary>
    IChatPromptFormatter? CreateChatPromptFormatter(ITokenizer tokenizer, string root, LocalModelMetadata? metadata) => null;
}
