using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public enum ProfilerFilterMode { Unset, Set }

public sealed record ProfilerSettings(int Level, int? SlowMs, decimal? SampleRate, string? FilterDigest)
{
    public static ProfilerSettings Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryReadInt(root, "was", out var level)
            || level is < 0 or > 2)
            throw new ArgumentException("A resposta do profiler não contém um nível válido.", nameof(json));
        int? slow = TryReadInt(root, "slowms", out var slowValue) ? slowValue : null;
        decimal? rate = TryReadDecimal(root, "sampleRate", out var rateValue) ? rateValue : null;
        var filter = root.TryGetProperty("filter", out var filterValue)
            && filterValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? Digest(filterValue.GetRawText()) : null;
        return new ProfilerSettings(level, slow, rate, filter);
    }

    public static bool IsMongos(string helloJson)
    {
        using var document = JsonDocument.Parse(helloJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A resposta de topologia não é válida.", nameof(helloJson));
        return root.TryGetProperty("msg", out var message)
            && message.ValueKind == JsonValueKind.String
            && string.Equals(message.GetString(), "isdbgrid", StringComparison.Ordinal);
    }

    public static string DigestFilter(string filterJson)
    {
        using var document = JsonDocument.Parse(filterJson);
        return Digest(document.RootElement.GetRawText());
    }

    private static string Digest(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool TryReadInt(JsonElement root, string property, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(property, out var element))
            return false;
        if (element.ValueKind == JsonValueKind.Object
            && (element.TryGetProperty("$numberInt", out var wrapped)
                || element.TryGetProperty("$numberLong", out wrapped)))
            element = wrapped;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)
            || element.ValueKind == JsonValueKind.String
                && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadDecimal(JsonElement root, string property, out decimal value)
    {
        value = 0;
        if (!root.TryGetProperty(property, out var element))
            return false;
        if (element.ValueKind == JsonValueKind.Object
            && (element.TryGetProperty("$numberDouble", out var wrapped)
                || element.TryGetProperty("$numberDecimal", out wrapped)
                || element.TryGetProperty("$numberInt", out wrapped)))
            element = wrapped;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value)
            || element.ValueKind == JsonValueKind.String
                && decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record ProfilerConfigurationRequest(
    string Database,
    int Level,
    int? SlowMs,
    decimal? SampleRate,
    ProfilerFilterMode FilterMode,
    string? FilterJson,
    string ConfirmationDatabase,
    string ExpectedStatusJson,
    string ExpectedTopologyJson)
{
    public ProfilerConfigurationRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database)
            || Database is "admin" or "config" or "local")
            throw new ArgumentException("Selecione um banco de usuário para configurar o profiler.", nameof(Database));
        if (!string.Equals(Database, ConfirmationDatabase?.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Digite o nome exato do banco para confirmar.", nameof(ConfirmationDatabase));
        if (Level is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(Level), "O nível deve ser 0, 1 ou 2.");
        if (SlowMs is < 1 or > 600_000)
            throw new ArgumentOutOfRangeException(nameof(SlowMs), "slowms deve ficar entre 1 e 600000.");
        if (SampleRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "sampleRate deve ficar entre 0 e 1.");
        if (!Enum.IsDefined(FilterMode))
            throw new ArgumentOutOfRangeException(nameof(FilterMode));
        if (Level == 2 && (SlowMs is not null || SampleRate is not null || FilterMode == ProfilerFilterMode.Set))
            throw new ArgumentException("O nível 2 captura todas as operações e não aceita limite, amostragem ou filtro neste fluxo.");
        if (FilterMode == ProfilerFilterMode.Set)
        {
            if (Level != 1 || SlowMs is not null || SampleRate is not null)
                throw new ArgumentException("O filtro requer nível 1 e não pode ser combinado com slowms/sampleRate.");
            if (string.IsNullOrWhiteSpace(FilterJson) || FilterJson.Length > 8192)
                throw new ArgumentException("Informe um filtro JSON limitado a 8192 caracteres.", nameof(FilterJson));
            try
            {
                using var filter = JsonDocument.Parse(FilterJson);
                if (filter.RootElement.ValueKind != JsonValueKind.Object
                    || !filter.RootElement.EnumerateObject().Any()
                    || ContainsUnsafeOperator(filter.RootElement))
                    throw new ArgumentException("O filtro deve ser um objeto não vazio, sem código executável.", nameof(FilterJson));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("O filtro não contém JSON válido.", nameof(FilterJson), exception);
            }
        }
        else if (!string.IsNullOrWhiteSpace(FilterJson))
            throw new ArgumentException("Limpe o campo de filtro ao escolher remover filtro.", nameof(FilterJson));

        if (string.IsNullOrWhiteSpace(ExpectedStatusJson)
            || string.IsNullOrWhiteSpace(ExpectedTopologyJson))
            throw new ArgumentException("Leia o estado atual e a topologia antes de configurar.");
        ProfilerSettings.Parse(ExpectedStatusJson);
        if (ProfilerSettings.IsMongos(ExpectedTopologyJson))
            throw new NotSupportedException("A configuração de log no mongos exige controle do nó e não está liberada neste fluxo; conecte-se a um mongod.");
        return this;
    }

    private static bool ContainsUnsafeOperator(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(ContainsUnsafeOperator);
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        return value.EnumerateObject().Any(property =>
            property.Name is "$where" or "$function" or "$accumulator"
            || ContainsUnsafeOperator(property.Value));
    }
}

public sealed record ProfilerConfigurationResult(
    int Level,
    int? SlowMs,
    decimal? SampleRate,
    bool FilterConfigured);
