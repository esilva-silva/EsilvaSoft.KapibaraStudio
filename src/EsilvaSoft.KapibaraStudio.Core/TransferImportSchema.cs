namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Formats accepted by the standalone document transfer parser.</summary>
public enum TransferImportFormat { Ndjson, Csv, JsonArray }

/// <summary>Explicit conversion for a CSV cell; ExtendedJson preserves BSON type information.</summary>
public enum TransferFieldType { Text, Boolean, Integer32, Integer64, FloatingPoint, Decimal128, DateTimeUtc, ObjectId, UuidStandard, ExtendedJson }

public sealed record TransferColumnMapping(
    string SourceColumn,
    string TargetField,
    TransferFieldType Type,
    bool EmptyAsNull = false);

/// <summary>A CSV import requires a complete, explicit, top-level mapping; JSON formats preserve EJSON types.</summary>
public sealed record TransferImportSchema(
    TransferImportFormat Format,
    IReadOnlyList<TransferColumnMapping>? Columns = null)
{
    public TransferImportSchema Validate()
    {
        if (!Enum.IsDefined(Format))
            throw new ArgumentOutOfRangeException(nameof(Format));

        if (Format is TransferImportFormat.Ndjson or TransferImportFormat.JsonArray)
        {
            if (Columns is { Count: > 0 })
                throw new ArgumentException("NDJSON preserva os campos EJSON; mapeamento CSV não é aplicável.", nameof(Columns));
            return this;
        }

        if (Columns is not { Count: > 0 and <= 256 })
            throw new ArgumentException("CSV exige mapeamento explícito de 1 a 256 colunas.", nameof(Columns));
        if (Columns.Any(column => string.IsNullOrWhiteSpace(column.SourceColumn)
                || string.IsNullOrWhiteSpace(column.TargetField)
                || column.TargetField.StartsWith('$')
                || column.TargetField.Contains('.')
                || column.TargetField.Contains('\0')
                || !Enum.IsDefined(column.Type))
            || Columns.Select(column => column.SourceColumn).Distinct(StringComparer.Ordinal).Count() != Columns.Count
            || Columns.Select(column => column.TargetField).Distinct(StringComparer.Ordinal).Count() != Columns.Count
            || !Columns.Any(column => string.Equals(column.TargetField, "_id", StringComparison.Ordinal)))
            throw new ArgumentException("Mapeamento CSV inválido, ambíguo ou sem _id.", nameof(Columns));

        return this;
    }
}
