namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Stores local metadata for an operation without retaining connection secrets or document payloads.</summary>
public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? ProfileId,
    string? Database,
    string? Collection,
    string Summary)
{
    public string DisplayText => $"{OccurredAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Action} · {Summary}";

    public AuditEntry Validate()
    {
        if (Id == Guid.Empty || OccurredAt == default)
        {
            throw new ArgumentException("O identificador e o instante da auditoria são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(Action) || Action.Length > 80)
        {
            throw new ArgumentException("A ação de auditoria é obrigatória e limitada a 80 caracteres.", nameof(Action));
        }

        if (string.IsNullOrWhiteSpace(Summary) || Summary.Length > 500)
        {
            throw new ArgumentException("O resumo da auditoria é obrigatório e limitado a 500 caracteres.", nameof(Summary));
        }

        if (Summary.Contains("://", StringComparison.Ordinal))
        {
            throw new ArgumentException("O resumo da auditoria não pode conter uma URI de conexão.", nameof(Summary));
        }

        return this;
    }

    public static AuditEntry Create(string action, Guid? profileId, string? database, string? collection, string summary, DateTimeOffset? occurredAt = null) =>
        new AuditEntry(Guid.NewGuid(), occurredAt ?? DateTimeOffset.UtcNow, action.Trim(), profileId, database?.Trim(), collection?.Trim(), summary.Trim()).Validate();
}
