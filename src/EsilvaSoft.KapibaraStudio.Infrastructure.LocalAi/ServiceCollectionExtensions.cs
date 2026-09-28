using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKapibaraStudioLocalAiInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILocalModelCatalog>(_ => new LocalModelCatalog());
        services.AddSingleton<IRemoteModelSource>(_ => new HuggingFaceModelSource());
        services.AddSingleton<IAiHardwareProbe, OnnxHardwareProbe>();
        // One model service shared by autocomplete, chat and the preferences window.
        services.AddSingleton<ILocalAiModelService>(provider => new LocalAiModelService(
            provider.GetRequiredService<ILocalModelCatalog>(),
            () => new OnnxLocalModelRuntime(provider.GetRequiredService<IAutocompleteDiagnostics>(), provider.GetRequiredService<IAiHardwareProbe>()),
            provider.GetRequiredService<IAiHardwareProbe>(),
            provider.GetRequiredService<IAutocompleteDiagnostics>(),
            provider.GetRequiredService<IApplicationOperationService>()));
        // Tokenizer e construtor de prompt do formato saem do mesmo adaptador que o runtime usa, e de nenhum outro.
        services.AddSingleton<AdapterTokenizerFactory>();
        // A camada de geração compartilhada recebe o singleton acima; ela não constrói serviço nem runtime.
        services.AddSingleton(provider => new AiGenerationPipeline(
            provider.GetRequiredService<ILocalAiModelService>(),
            provider.GetRequiredService<AdapterTokenizerFactory>().Create,
            new AdapterPromptBuilder(provider.GetRequiredService<AdapterTokenizerFactory>()),
            provider.GetRequiredService<IAutocompleteDiagnostics>()));
        services.AddSingleton(provider => new AiCompletionProvider(provider.GetRequiredService<AiGenerationPipeline>()));
        services.AddSingleton<IAiCompletionProvider>(provider => provider.GetRequiredService<AiCompletionProvider>());
        return services;
    }
}

/// <summary>
/// Tokenizador do pacote pelo mesmo caminho do runtime: <c>ModelAdapters.For(model).CreateTokenizer(...)</c>.
/// </summary>
/// <remarks>
/// <para><strong>Por que existe.</strong> O orçamento da Fase 3 é medido com o tokenizador real do modelo, e o prompt
/// tokenizado (DEC-R42-PROMPTTOKENS) só serve ao runtime se vier do mesmo vocabulário. Deixar o
/// <see cref="AiGenerationPipeline"/> escolher um tokenizador por conta própria produziria identificadores de outro
/// vocabulário — por isso a fábrica é o adaptador, e nada mais.</para>
/// <para><strong>Custo conhecido.</strong> O tokenizador do GenAI só existe preso a um <see cref="Model"/>, então esta
/// fábrica abre uma sessão sem provider (CPU) só para obtê-lo, uma única vez por pasta de modelo e sob demanda: um
/// pedido explícito que nunca aconteça não abre nada. É memória a mais enquanto a IA explícita está em uso; a saída
/// definitiva é o runtime carregado publicar o próprio tokenizador, o que exigiria mexer no runtime fechado por R42.</para>
/// <para><strong>Sem estado de domínio.</strong> A classe só guarda sessão, tokenizador e construtor de prompt por
/// caminho de pasta; nada de MongoDB, nada de texto do editor.</para>
/// </remarks>
internal sealed class AdapterTokenizerFactory : IDisposable
{
    private const int Limit = 2;
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    /// <summary>Tokenizador do pacote, memorizado por pasta.</summary>
    /// <param name="model">Modelo já validado pelo catálogo.</param>
    public ITokenizer Create(LocalModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.Find(entry => string.Equals(entry.Path, model.Path, StringComparison.OrdinalIgnoreCase)) is { } cached)
                return cached.Tokenizer;
            var entry = Load(model);
            _entries.Add(entry);
            if (_entries.Count > Limit) { var evicted = _entries[0]; _entries.RemoveAt(0); evicted.Dispose(); }
            return entry.Tokenizer;
        }
    }

    /// <summary>
    /// Construtor de prompt do formato do tokenizador entregue por <see cref="Create"/>. Um tokenizador desconhecido
    /// é recusado com <see cref="InvalidDataException"/>, que o pipeline já trata deixando o runtime montar o prompt.
    /// </summary>
    /// <param name="tokenizer">Tokenizador devolvido por esta fábrica.</param>
    public ICompletionPromptBuilder BuilderFor(ITokenizer tokenizer)
    {
        lock (_gate)
            return _entries.Find(entry => ReferenceEquals(entry.Tokenizer, tokenizer))?.Builder
                ?? throw new InvalidDataException("Tokenizador de outra origem; o prompt será montado pelo runtime.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _entries) entry.Dispose();
            _entries.Clear();
        }
    }

    private static Entry Load(LocalModelDefinition model)
    {
        var adapter = ModelAdapters.For(model);
        Model? session = null;
        try
        {
            using (var config = new Config(model.Path))
            {
                // Sem provider acelerado: este modelo existe só para dar acesso ao tokenizador do pacote.
                config.ClearProviders();
                session = new Model(config);
            }
            return new(model.Path, session, adapter.CreateTokenizer(session, model.Path), adapter.CreatePromptBuilder());
        }
        catch (Exception ex)
        {
            session?.Dispose();
            throw new LocalModelLoadException(LocalModelLoadStage.Tokenizer, "Tokenizer incompatível com este modelo.", ex);
        }
    }

    private sealed record Entry(string Path, Model Session, ITokenizer Tokenizer, ICompletionPromptBuilder Builder)
    {
        public void Dispose()
        {
            (Tokenizer as IDisposable)?.Dispose();
            Session.Dispose();
        }
    }
}

/// <summary>
/// Construtor de prompt que despacha para o formato do modelo, deduzido do tokenizador que o pipeline recebeu.
/// </summary>
/// <remarks>
/// O <see cref="ICompletionPromptBuilder"/> do pipeline é único, mas o formato não é: DeepSeek e Qwen têm marcadores
/// FIM diferentes. Como o tokenizador entregue pela fábrica identifica o pacote, o despacho por ele mantém prompt e
/// vocabulário do mesmo adaptador, sem alterar a assinatura que A41 fechou.
/// </remarks>
internal sealed class AdapterPromptBuilder(AdapterTokenizerFactory factory) : ICompletionPromptBuilder
{
    public IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer) =>
        factory.BuilderFor(tokenizer).Build(prefix, suffix, contextTokens, tokenizer);
}
