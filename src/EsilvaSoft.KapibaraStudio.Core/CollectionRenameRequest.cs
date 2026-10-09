namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes a collection rename within one database.</summary>
public sealed record CollectionRenameRequest(
    string Database,
    string SourceCollection,
    string TargetCollection,
    bool DropTarget = false,
    string? DropTargetConfirmation = null)
{
    public CollectionRenameRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        ValidateCollectionName(SourceCollection, nameof(SourceCollection));
        ValidateCollectionName(TargetCollection, nameof(TargetCollection));
        if (string.Equals(SourceCollection, TargetCollection, StringComparison.Ordinal))
        {
            throw new ArgumentException("O novo nome precisa ser diferente da coleção de origem.", nameof(TargetCollection));
        }

        if (DropTarget && !string.Equals(TargetCollection, DropTargetConfirmation?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite também o nome exato da coleção de destino que será removida.", nameof(DropTargetConfirmation));
        }

        return this;
    }

    private static void ValidateCollectionName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O nome da coleção é obrigatório.", parameterName);
        }

        if (value.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Coleções do sistema não podem ser renomeadas pela interface.", parameterName);
        }
    }
}
