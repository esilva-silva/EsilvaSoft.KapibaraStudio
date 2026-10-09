namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Reports safe per-definition outcomes when a confirmed restore does not complete.</summary>
public sealed class DatabaseDefinitionRestoreException : InvalidOperationException
{
    public DatabaseDefinitionRestoreException(
        string message,
        DatabaseDefinitionRestoreReport report,
        bool outcomeMayBePartial,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Report = (report ?? throw new ArgumentNullException(nameof(report))).Validate();
        OutcomeMayBePartial = outcomeMayBePartial;
    }

    public DatabaseDefinitionRestoreReport Report { get; }

    public bool OutcomeMayBePartial { get; }
}
