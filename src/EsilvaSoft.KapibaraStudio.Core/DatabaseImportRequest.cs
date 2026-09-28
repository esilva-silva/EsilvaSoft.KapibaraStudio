namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines an import of a KapibaraStudio logical export into a target database.</summary>
public sealed record DatabaseImportRequest(string SourceDirectory, string TargetDatabase)
{
    public DatabaseImportRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory))
        {
            throw new ArgumentException("A pasta de origem é obrigatória.", nameof(SourceDirectory));
        }

        if (string.IsNullOrWhiteSpace(TargetDatabase))
        {
            throw new ArgumentException("O banco de destino é obrigatório.", nameof(TargetDatabase));
        }

        return this;
    }
}
