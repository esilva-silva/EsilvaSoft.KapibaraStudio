namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a bounded logical export of all collections in a database.</summary>
public sealed record DatabaseExportRequest(string Database, int DocumentsPerCollectionLimit = 100_000)
{
    public DatabaseExportRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (DocumentsPerCollectionLimit is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(DocumentsPerCollectionLimit), "O limite por coleção deve estar entre 1 e 1000000.");
        }

        return this;
    }
}
