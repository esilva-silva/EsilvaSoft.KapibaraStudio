using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ScriptHistoryEntry(Guid Id, string Path, DateTimeOffset LastAccessedAt, string? InputJson = null)
{
    private const int MaximumInputLength = 65_536;

    public string DisplayText => $"{LastAccessedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Path}";

    public ScriptHistoryEntry Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("O identificador do histórico é obrigatório.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Path) || !System.IO.Path.IsPathFullyQualified(Path) || !Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O histórico precisa conter um caminho absoluto para arquivo .js.", nameof(Path));
        }

        if (LastAccessedAt == default)
        {
            throw new ArgumentException("A data de acesso ao script é obrigatória.", nameof(LastAccessedAt));
        }

        if (InputJson is { Length: > MaximumInputLength })
        {
            throw new ArgumentException("A entrada JSON associada ao script excede o limite de 64 KiB.", nameof(InputJson));
        }

        if (!string.IsNullOrWhiteSpace(InputJson))
        {
            try
            {
                using var document = JsonDocument.Parse(InputJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("A entrada associada ao script precisa ser um objeto JSON.", nameof(InputJson));
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("A entrada associada ao script contém JSON inválido.", nameof(InputJson), exception);
            }
        }

        return this;
    }

    public static ScriptHistoryEntry Create(string path, DateTimeOffset? lastAccessedAt = null, string? inputJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ScriptHistoryEntry(Guid.NewGuid(), System.IO.Path.GetFullPath(path), lastAccessedAt ?? DateTimeOffset.UtcNow, inputJson).Validate();
    }
}
