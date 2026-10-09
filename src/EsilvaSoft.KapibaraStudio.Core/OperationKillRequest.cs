using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires the exact numeric operation identifier before requesting MongoDB to stop it.</summary>
public sealed record OperationKillRequest(string OperationId, string ConfirmationOperationId, string ExpectedOperationFingerprint = "")
{
    private static readonly string[] IdentityFields = ["host", "op", "ns", "desc", "client", "connectionId", "lsid", "effectiveUsers", "command", "originatingCommand"];

    public long GetOperationId()
    {
        if (!long.TryParse(OperationId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var operationId)
            || operationId < 1)
        {
            throw new ArgumentException("O identificador da operação precisa ser um inteiro positivo.", nameof(OperationId));
        }

        if (!string.Equals(operationId.ToString(CultureInfo.InvariantCulture), ConfirmationOperationId?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o identificador exato da operação para confirmar a interrupção.", nameof(ConfirmationOperationId));
        }

        return operationId;
    }

    public long ValidateForCurrentOperation()
    {
        var operationId = GetOperationId();
        if (string.IsNullOrWhiteSpace(ExpectedOperationFingerprint)
            || ExpectedOperationFingerprint.Length != 64
            || ExpectedOperationFingerprint.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Recarregue as operações e selecione novamente o alvo antes de interrompê-lo.", nameof(ExpectedOperationFingerprint));
        }

        return operationId;
    }

    public bool MatchesCurrentOperation(string currentOperationsJson)
    {
        var operationId = ValidateForCurrentOperation();
        return string.Equals(
            FindFingerprint(currentOperationsJson, operationId),
            ExpectedOperationFingerprint,
            StringComparison.Ordinal);
    }

    public static string? FindFingerprint(string currentOperationsJson, long operationId)
    {
        try
        {
            using var document = JsonDocument.Parse(currentOperationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("truncated", out var truncated)
                && truncated.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("inprog", out var operations) || operations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var matches = operations.EnumerateArray()
                .Where(operation => TryGetOperationId(operation, out var candidate) && candidate == operationId)
                .ToArray();
            if (matches.Length != 1 || !TryCreateFingerprint(matches[0], out var fingerprint))
            {
                return null;
            }

            return fingerprint;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool TryCreateFingerprint(JsonElement operation, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (operation.ValueKind != JsonValueKind.Object
            || !operation.TryGetProperty("opid", out var operationIdValue)
            || !TryParseOperationId(operationIdValue, out _)
            || !operation.TryGetProperty("host", out var host)
            || host.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(host.GetString()))
        {
            return false;
        }

        var identity = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in IdentityFields)
        {
            if (operation.TryGetProperty(field, out var value))
            {
                identity.Add(field, value.Clone());
            }
        }

        if (!identity.ContainsKey("op") && !identity.ContainsKey("desc") && !identity.ContainsKey("command"))
        {
            return false;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("opid");
            WriteCanonical(writer, operationIdValue);
            foreach (var pair in identity)
            {
                writer.WritePropertyName(pair.Key);
                WriteCanonical(writer, pair.Value);
            }

            writer.WriteEndObject();
        }

        fingerprint = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        return true;
    }

    private static bool TryGetOperationId(JsonElement operation, out long operationId)
    {
        operationId = 0;
        return operation.ValueKind == JsonValueKind.Object
            && operation.TryGetProperty("opid", out var value)
            && TryParseOperationId(value, out operationId);
    }

    private static bool TryParseOperationId(JsonElement value, out long operationId)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt64(out operationId) && operationId > 0;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out operationId) && operationId > 0;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("$numberLong", out var longValue) && longValue.ValueKind == JsonValueKind.String)
            {
                return long.TryParse(longValue.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out operationId) && operationId > 0;
            }

            if (value.TryGetProperty("$numberInt", out var intValue) && intValue.ValueKind == JsonValueKind.String)
            {
                return long.TryParse(intValue.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out operationId) && operationId > 0;
            }
        }

        operationId = 0;
        return false;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
