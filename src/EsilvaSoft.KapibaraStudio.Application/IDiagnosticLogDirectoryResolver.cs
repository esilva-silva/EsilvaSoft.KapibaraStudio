namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Resolves the development diagnostic log destination without creating or writing it.</summary>
public interface IDiagnosticLogDirectoryResolver
{
    /// <summary>Gets the repository log directory, or the application log directory outside a checkout.</summary>
    string Resolve();
}
