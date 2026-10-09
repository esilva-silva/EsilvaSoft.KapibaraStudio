using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public static class ModelAdapters
{
    /// <summary>Order matters: a llama export is DeepSeek only when its manifest is present.</summary>
    public static IReadOnlyList<IModelAdapter> CreateDefault(ILocalModelFileAccess fileAccess) =>
        [new DeepSeekCoderModelAdapter(fileAccess), new QwenCoderModelAdapter(), new Qwen3ModelAdapter(fileAccess)];

    public static IModelAdapter For(LocalModelDefinition model, IReadOnlyList<IModelAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(adapters);
        return adapters.FirstOrDefault(adapter => adapter.Architecture == model.Architecture && adapter.PromptFormat == model.PromptFormat)
            ?? throw new NotSupportedException("Arquitetura não suportada.");
    }
}
