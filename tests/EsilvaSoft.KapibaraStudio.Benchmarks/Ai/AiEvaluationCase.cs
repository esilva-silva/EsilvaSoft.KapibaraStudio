using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>Família de statement MongoDB de um caso sintético. Define a forma do texto do editor e do pipeline.</summary>
public enum AiEvaluationShape
{
    /// <summary><c>db.col.find({...})</c> — filtro simples, sem pipeline.</summary>
    Filter,
    /// <summary><c>db.col.aggregate([...])</c> — pipeline com estágios decodificados.</summary>
    Aggregation,
    /// <summary><c>db.col.updateMany({...}, {$set: {...}})</c> — filtro mais operadores de atualização.</summary>
    Update
}

/// <summary>Faixa de tamanho do schema da coleção alvo, em número de campos publicados pelo catálogo.</summary>
public enum AiEvaluationSchemaSize
{
    /// <summary>12 campos.</summary>
    Small,
    /// <summary>80 campos.</summary>
    Medium,
    /// <summary>400 campos.</summary>
    Large
}

/// <summary>
/// Um caso do dataset sintético de avaliação de contexto. Puramente de dados: não conhece contrato, tokenizer nem
/// medição. Tudo o que o caso carrega é derivado de <see cref="Seed"/> — dois datasets gerados com a mesma semente
/// produzem casos iguais campo a campo, e é isso que <c>AiEvaluationDatasetTests</c> prova.
/// </summary>
/// <remarks>
/// Nenhum dado real, nenhuma amostra de documento e nenhuma credencial entram aqui: nomes de campo e de coleção são
/// gerados por fórmula a partir do índice do caso.
/// </remarks>
public sealed record AiEvaluationCase
{
    public AiEvaluationCase(string id, int seed, AiEvaluationShape shape, AiEvaluationSchemaSize schemaSize,
        bool hasLearnedSchema, string database, string collection, string editorText, int caret,
        IReadOnlyList<PipelineStage> pipeline, IReadOnlyList<string> foreignCollections)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(database);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        ArgumentNullException.ThrowIfNull(editorText);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(foreignCollections);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, editorText.Length);
        Id = id;
        Seed = seed;
        Shape = shape;
        SchemaSize = schemaSize;
        HasLearnedSchema = hasLearnedSchema;
        Database = database;
        Collection = collection;
        EditorText = editorText;
        Caret = caret;
        Pipeline = [.. pipeline];
        ForeignCollections = [.. foreignCollections];
    }

    /// <summary>Identificador estável e legível; único dentro de um dataset.</summary>
    public string Id { get; }

    /// <summary>Semente do caso, derivada da semente raiz do dataset e do índice. Registrada no relatório.</summary>
    public int Seed { get; }

    public AiEvaluationShape Shape { get; }
    public AiEvaluationSchemaSize SchemaSize { get; }

    /// <summary>Verdadeiro quando a fonte de schema aprendido responde fatos para este caso.</summary>
    public bool HasLearnedSchema { get; }

    public string Database { get; }

    /// <summary>Coleção alvo do cursor.</summary>
    public string Collection { get; }

    /// <summary>Texto da aba no instante da captura.</summary>
    public string EditorText { get; }

    /// <summary>Posição do cursor dentro de <see cref="EditorText"/>.</summary>
    public int Caret { get; }

    /// <summary>Estágios já decodificados antes do cursor; vazio fora de <see cref="AiEvaluationShape.Aggregation"/>.</summary>
    public IReadOnlyList<PipelineStage> Pipeline { get; }

    /// <summary>Coleções estrangeiras citadas por <c>$lookup</c>/<c>$unionWith</c>/<c>$graphLookup</c> no pipeline.</summary>
    public IReadOnlyList<string> ForeignCollections { get; }

    /// <summary>Verdadeiro quando o pipeline cita ao menos uma coleção estrangeira.</summary>
    public bool HasLookup => ForeignCollections.Count > 0;

    /// <summary>Campos que o painel de resultados exporia; nomes apenas, nunca valores.</summary>
    public IReadOnlyList<string> ResultFields { get; init; } = [];

    /// <summary>Nomes conhecidos oferecidos ao contrato v1.</summary>
    public IReadOnlyList<string> KnownNames { get; init; } = [];

    /// <summary>Comandos recentes sintéticos da aba.</summary>
    public IReadOnlyList<string> RecentCommands { get; init; } = [];

    /// <summary>Envelope de orçamento do caso; define o que conta como estouro.</summary>
    public AiBudget Budget { get; init; } = new(2048, 32, 16);

    /// <summary>Captura pronta para o contrato de contexto, montada a partir dos campos acima.</summary>
    public AutocompleteContextSnapshot ToSnapshot(string language) => new(EditorText, Caret, language,
        Input: "", ResultFields: ResultFields, KnownNames: KnownNames, RecentCommands: RecentCommands);

    public override string ToString() => Id;
}
