namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record AggregationQuery(
    string Database,
    string Collection,
    string PipelineJson,
    int Limit = 1_000)
{
    public AggregationQuery Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(PipelineJson))
        {
            throw new ArgumentException("O pipeline é obrigatório.", nameof(PipelineJson));
        }

        if (Limit is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), "O limite deve estar entre 1 e 10000.");
        }

        return this;
    }
}
