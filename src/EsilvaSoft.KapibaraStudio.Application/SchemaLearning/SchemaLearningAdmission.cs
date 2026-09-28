namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Outcome of the admission rule: either an envelope or a typed reason, never both.</summary>
public sealed class SchemaLearningAdmission
{
    private SchemaLearningAdmission(SchemaLearningEnvelope? envelope, SchemaLearningRejectionReason reason)
    {
        Envelope = envelope;
        Reason = reason;
    }

    /// <summary>The envelope to enqueue, or null when rejected.</summary>
    public SchemaLearningEnvelope? Envelope { get; }

    /// <summary>Why the result set was rejected; <see cref="SchemaLearningRejectionReason.None"/> when admitted.</summary>
    public SchemaLearningRejectionReason Reason { get; }

    /// <summary>True when <see cref="Envelope"/> is present.</summary>
    public bool IsAdmitted => Envelope is not null;

    internal static SchemaLearningAdmission Admitted(SchemaLearningEnvelope envelope) => new(envelope, SchemaLearningRejectionReason.None);

    internal static SchemaLearningAdmission Rejected(SchemaLearningRejectionReason reason) => new(null, reason);
}
