using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public static class ModelAdapters
{
    /// <summary>Order matters: a llama export is DeepSeek only when its manifest is present.</summary>
    public static IReadOnlyList<IModelAdapter> Default { get; } = [new DeepSeekCoderModelAdapter(), new QwenCoderModelAdapter()];

    public static IModelAdapter For(LocalModelDefinition model, IReadOnlyList<IModelAdapter>? adapters = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return (adapters ?? Default).FirstOrDefault(adapter => adapter.Architecture == model.Architecture && adapter.PromptFormat == model.PromptFormat)
            ?? throw new NotSupportedException("Arquitetura não suportada.");
    }
}
