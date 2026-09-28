using System.Numerics;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Escopo de um fato. Coleção nomeada ou documento/aba; nunca "o banco", porque um banco com dezenas de coleções não
/// é contexto relevante para um cursor que está dentro de uma delas.
/// </summary>
public sealed record AiFactScope
{
    private AiFactScope(AiFactScopeKind kind, string database, string collection, string documentId)
    {
        Kind = kind;
        Database = database;
        Collection = collection;
        DocumentId = documentId;
    }

    public AiFactScopeKind Kind { get; }
    /// <summary>Banco da coleção; vazio em escopo de documento.</summary>
    public string Database { get; }
    /// <summary>Coleção alvo do fato; vazio em escopo de documento.</summary>
    public string Collection { get; }
    /// <summary>Identidade da aba/documento; vazio em escopo de coleção.</summary>
    public string DocumentId { get; }

    /// <summary>Chave textual estável, usada para ordenação determinística e comparação de escopo.</summary>
    public string Key => Kind == AiFactScopeKind.Collection ? "collection:" + Database + "." + Collection : "document:" + DocumentId;

    public static AiFactScope ForCollection(string database, string collection)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        return new(AiFactScopeKind.Collection, database, collection, "");
    }

    public static AiFactScope ForDocument(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        return new(AiFactScopeKind.Document, "", "", documentId);
    }
}

/// <summary>
/// Um fato imutável sobre o contexto, pronto para virar texto de prompt. Sempre tem proveniência
/// (<see cref="Origin"/>) e escopo (<see cref="Scope"/>): um fato órfão não pode ser filtrado por privacidade e por
/// isso não existe. O <see cref="Payload"/> carrega apenas nomes e estatísticas — nunca documentos, nunca valores de
/// amostra.
/// </summary>
public sealed record AiFact
{
    public AiFact(AiFactKind kind, AiFactOrigin origin, AiFactScope scope, AiFactPayload payload)
    {
        if (kind == AiFactKind.None || BitOperations.PopCount((uint)kind) != 1)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Um fato tem exatamente uma categoria; combinações servem só para filtrar conjuntos.");
        if (origin == AiFactOrigin.Unspecified)
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Todo fato precisa declarar de onde veio.");
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(payload);
        Kind = kind;
        Origin = origin;
        Scope = scope;
        Payload = payload;
    }

    public AiFactKind Kind { get; }
    public AiFactOrigin Origin { get; }
    public AiFactScope Scope { get; }
    public AiFactPayload Payload { get; }
}
