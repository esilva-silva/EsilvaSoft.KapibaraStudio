namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Categoria de um fato oferecido à IA. É <see cref="FlagsAttribute"/> porque o uso dominante é filtrar conjuntos por
/// combinação ("só schema", "schema e operadores") antes de montar o prompt; um <see cref="AiFact"/> individual
/// carrega sempre exatamente um bit (ver a validação em <see cref="AiFact"/>).
/// </summary>
[Flags]
public enum AiFactKind
{
    None = 0,
    /// <summary>Campo de schema conhecido pelo catálogo: nome, tipo lógico, presença.</summary>
    FieldSchema = 1 << 0,
    /// <summary>Nome de coleção conhecida no escopo alvo.</summary>
    Collection = 1 << 1,
    /// <summary>Operador ou estágio de agregação em uso na janela do editor.</summary>
    Operator = 1 << 2,
    /// <summary>Variável local declarada na aba e visível no ponto do cursor.</summary>
    LocalVariable = 1 << 3,
    /// <summary>
    /// Campo vindo do schema aprendido (metadado probabilístico, sem valores). Categoria distinta de
    /// <see cref="FieldSchema"/> porque a confiança é estatística e um consumidor pode excluí-la por máscara sem
    /// inspecionar a proveniência de cada fato.
    /// </summary>
    LearnedFieldSchema = 1 << 4,
    /// <summary>Qualquer forma de campo, aprendida ou catalogada.</summary>
    AnyFieldSchema = FieldSchema | LearnedFieldSchema,
    /// <summary>Tudo o que o seletor sabe produzir.</summary>
    All = FieldSchema | Collection | Operator | LocalVariable | LearnedFieldSchema
}

/// <summary>
/// Proveniência obrigatória de um fato. Registrar a origem é requisito de privacidade: nenhum fato entra no prompt sem
/// que se saiba de onde veio.
/// </summary>
public enum AiFactOrigin
{
    /// <summary>Valor inválido; existe apenas para que o <c>default</c> não passe por uma origem real.</summary>
    Unspecified = 0,
    /// <summary>Catálogo de conhecimento da Fase 1 (validator, índices, amostragem já em memória).</summary>
    CatalogSchema,
    /// <summary>Schema aprendido e persistido; participa como metadado probabilístico, sem valores.</summary>
    LearnedSchema,
    /// <summary>Sintaxe do próprio editor: pipeline, operadores e declarações da aba atual.</summary>
    EditorSyntax
}

/// <summary>A que o fato pertence: uma coleção nomeada ou apenas o documento/aba que o originou.</summary>
public enum AiFactScopeKind
{
    /// <summary>Fato de uma coleção específica; nunca do banco inteiro.</summary>
    Collection,
    /// <summary>Fato local da aba: não sobrevive à troca de documento nem vaza para outra aba.</summary>
    Document
}
