using System.Collections;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Conjunto imutável e ordenado de fatos. Filtrar é operação de primeira classe: privacidade e relevância são
/// aplicadas <b>por fato</b>, antes de qualquer montagem de prompt, e cada filtro devolve outro
/// <see cref="AiFactSet"/> em vez de espalhar a regra por quem consome. A igualdade é sensível à ordem, porque
/// determinismo do prompt é determinismo da sequência, não só do conteúdo.
/// </summary>
public sealed class AiFactSet : IReadOnlyList<AiFact>, IEquatable<AiFactSet>
{
    private readonly AiFact[] _facts;

    private AiFactSet(AiFact[] facts) => _facts = facts;

    public static AiFactSet Empty { get; } = new([]);

    public static AiFactSet From(IEnumerable<AiFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var materialized = facts.ToArray();
        if (Array.IndexOf(materialized, null) >= 0) throw new ArgumentException("Um conjunto de fatos não contém nulos.", nameof(facts));
        return materialized.Length == 0 ? Empty : new(materialized);
    }

    public int Count => _facts.Length;
    public AiFact this[int index] => _facts[index];

    /// <summary>Filtro geral; a base de toda política de privacidade e relevância aplicada por fato.</summary>
    public AiFactSet Where(Func<AiFact, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var kept = Array.FindAll(_facts, fact => predicate(fact));
        return kept.Length == _facts.Length ? this : kept.Length == 0 ? Empty : new(kept);
    }

    /// <summary>Mantém os fatos cuja categoria está na máscara.</summary>
    public AiFactSet OfKind(AiFactKind kinds) => Where(fact => (fact.Kind & kinds) != AiFactKind.None);

    /// <summary>Mantém os fatos de uma proveniência.</summary>
    public AiFactSet FromOrigin(AiFactOrigin origin) => Where(fact => fact.Origin == origin);

    /// <summary>Mantém os fatos de um escopo exato.</summary>
    public AiFactSet InScope(AiFactScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Where(fact => fact.Scope == scope);
    }

    /// <summary>Concatena preservando a ordem das duas sequências; nenhum dos operandos é alterado.</summary>
    public AiFactSet Concat(AiFactSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Count == 0 ? this : Count == 0 ? other : new([.. _facts, .. other._facts]);
    }

    public IEnumerator<AiFact> GetEnumerator() => ((IEnumerable<AiFact>)_facts).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _facts.GetEnumerator();

    public bool Equals(AiFactSet? other) => other is not null && (ReferenceEquals(this, other) || _facts.SequenceEqual(other._facts));
    public override bool Equals(object? obj) => Equals(obj as AiFactSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var fact in _facts) hash.Add(fact);
        return hash.ToHashCode();
    }
}
