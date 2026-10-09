namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>The reviewed package import plan no longer matches current profile, source, policy or destination state.</summary>
public sealed class DatabaseImportPreviewStaleException : InvalidOperationException
{
    public DatabaseImportPreviewStaleException()
        : base("A prévia de importação ficou desatualizada; revise o pacote e o destino novamente.") { }
}
