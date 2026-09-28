using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public static class AuditJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string Serialize(IReadOnlyCollection<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(entries), "A exportação de auditoria aceita no máximo 500 entradas.");
        }

        return JsonSerializer.Serialize(entries, Options);
    }
}
