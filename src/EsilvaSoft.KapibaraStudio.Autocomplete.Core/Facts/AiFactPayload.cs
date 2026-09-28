namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Dados de um fato. Deliberadamente pobre: um nome, um tipo lógico, duas estatísticas e uma lista curta de rótulos.
/// Nenhum valor lido de documento, nenhuma amostra e nenhum texto longo entram aqui — o que não couber nesta forma
/// não é fato de contexto.
/// </summary>
public sealed record AiFactPayload
{
    /// <summary>Teto de rótulos; existe para que um campo poliformo não vire uma lista inteira dentro do prompt.</summary>
    public const int MaximumValues = 8;
    /// <summary>Teto de texto descritivo; excedentes são cortados, não rejeitados.</summary>
    public const int MaximumTextLength = 256;

    private readonly IReadOnlyList<string> _values = [];
    private readonly string? _detail;
    private readonly double? _presence;
    private readonly double? _confidence;

    public AiFactPayload(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>Nome do campo, coleção, operador ou variável. Nunca um valor.</summary>
    public string Name { get; }

    /// <summary>Tipo lógico predominante, quando conhecido.</summary>
    public string? LogicalType { get; init; }

    /// <summary>Fração de documentos amostrados em que o campo aparece (0 a 1).</summary>
    public double? Presence
    {
        get => _presence;
        init => _presence = Fraction(value, nameof(Presence));
    }

    /// <summary>Confiança da fonte no fato (0 a 1); fontes determinísticas deixam nulo.</summary>
    public double? Confidence
    {
        get => _confidence;
        init => _confidence = Fraction(value, nameof(Confidence));
    }

    /// <summary>Rótulos curtos (tipos BSON observados, por exemplo). Nunca valores de documentos.</summary>
    public IReadOnlyList<string> Values
    {
        get => _values;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count > MaximumValues)
                throw new ArgumentOutOfRangeException(nameof(value), value.Count, "Um payload carrega no máximo " + MaximumValues + " rótulos.");
            if (value.Any(string.IsNullOrEmpty)) throw new ArgumentException("Rótulos vazios não descrevem nada.", nameof(value));
            _values = value.ToArray();
        }
    }

    /// <summary>Descrição curta; cortada em <see cref="MaximumTextLength"/>.</summary>
    public string? Detail
    {
        get => _detail;
        init => _detail = value is { Length: > MaximumTextLength } ? value[..MaximumTextLength] : value;
    }

    public bool Equals(AiFactPayload? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(LogicalType, other.LogicalType, StringComparison.Ordinal)
        && Presence == other.Presence && Confidence == other.Confidence
        && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
        && Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(LogicalType, StringComparer.Ordinal);
        hash.Add(Presence);
        hash.Add(Confidence);
        hash.Add(Detail, StringComparer.Ordinal);
        foreach (var value in Values) hash.Add(value, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static double? Fraction(double? value, string name) => value switch
    {
        null => null,
        { } number when double.IsFinite(number) && number is >= 0 and <= 1 => number,
        _ => throw new ArgumentOutOfRangeException(name, value, "Uma fração fica entre 0 e 1.")
    };
}
