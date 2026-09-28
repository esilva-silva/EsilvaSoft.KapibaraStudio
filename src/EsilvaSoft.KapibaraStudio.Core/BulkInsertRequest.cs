namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a bounded batch insert represented as an Extended JSON array.</summary>
public sealed record BulkInsertRequest(string Database, string Collection, string DocumentsJson, int MaximumDocuments = 1_000, bool Ordered = true)
{
    public BulkInsertRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection))
        {
            throw new ArgumentException("A coleção é obrigatória.", nameof(Collection));
        }

        if (string.IsNullOrWhiteSpace(DocumentsJson))
        {
            throw new ArgumentException("O array de documentos é obrigatório.", nameof(DocumentsJson));
        }

        if (MaximumDocuments is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDocuments), "O lote deve aceitar entre 1 e 10000 documentos.");
        }

        return this;
    }
}
