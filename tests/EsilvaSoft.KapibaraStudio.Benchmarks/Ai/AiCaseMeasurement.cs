namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Resultado da medição de um caso. Puramente de dados, para que a agregação e a serialização do relatório possam ser
/// testadas com valores fabricados, sem rodar o dataset inteiro.
/// </summary>
public sealed record AiCaseMeasurement
{
    /// <summary>Identificador do caso medido.</summary>
    public required string CaseId { get; init; }

    /// <summary>Semente do caso, repetida aqui para que o relatório seja reexecutável sem o dataset em mãos.</summary>
    public required int Seed { get; init; }

    public required AiEvaluationShape Shape { get; init; }
    public required AiEvaluationSchemaSize SchemaSize { get; init; }
    public required bool HasLookup { get; init; }
    public required bool HasLearnedSchema { get; init; }

    /// <summary>Fatos selecionados pelo <c>RelevantContextSelector</c>.</summary>
    public required int FactCount { get; init; }

    /// <summary>Tokens do prompt final montado pelo contrato, pelo contador declarado no relatório.</summary>
    public required int PromptTokens { get; init; }

    /// <summary>Tokens da renderização canônica dos fatos; linha de base para comparar contratos em A34b.</summary>
    public required int FactTokens { get; init; }

    /// <summary>Caracteres do prompt final; permite recalcular a razão caracteres/token do contador real em A34c.</summary>
    public required int PromptCharacters { get; init; }

    /// <summary>Mediana do tempo de seleção de fatos, em microssegundos.</summary>
    public required double SelectionMicroseconds { get; init; }

    /// <summary>Mediana do tempo de montagem do prompt pelo contrato, em microssegundos.</summary>
    public required double AssemblyMicroseconds { get; init; }

    /// <summary>Tokens disponíveis pelo <c>AiBudget</c> do caso, já descontados geração e overhead.</summary>
    public required int AvailableTokens { get; init; }

    /// <summary>Verdadeiro quando duas execuções do mesmo caso produziram prompt e fatos idênticos.</summary>
    public required bool IsDeterministic { get; init; }

    /// <summary>Soma dos tempos medianos de seleção e montagem.</summary>
    public double TotalMicroseconds => SelectionMicroseconds + AssemblyMicroseconds;

    /// <summary>Quanto o prompt passou do orçamento; zero quando coube.</summary>
    public int OverflowTokens => Math.Max(0, PromptTokens - AvailableTokens);

    /// <summary>Verdadeiro quando o caso precisaria de corte para respeitar o orçamento.</summary>
    public bool ExceedsBudget => OverflowTokens > 0;
}
