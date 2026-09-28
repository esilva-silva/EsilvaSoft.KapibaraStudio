using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Shared UI diagnostic categories. Logs contain type/category only, never document text or resolved credentials.</summary>
public static partial class OperationErrorMessages
{
    public static string Describe(Exception exception, bool export = false, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var types = new HashSet<string>(StringComparer.Ordinal);
        for (var type = exception.GetType(); type is not null; type = type.BaseType) types.Add(type.Name);
        var categoryKey = exception switch {
            OperationCanceledException => "errorOperationCancelled",
            TimeoutException => "errorTimeout",
            JsonException => "errorInvalidJson",
            _ when types.Contains("MongoAuthenticationException") => "errorAuthentication",
            _ when types.Contains("MongoExecutionTimeoutException") => "errorTimeout",
            _ when types.Contains("MongoConnectionException") => "errorConnection",
            _ when types.Contains("MongoQueryException") || types.Contains("MongoCommandException") => "errorMongoQuery",
            _ when types.Contains("MongoException") => "errorMongo",
            _ when export => "errorExport",
            IOException or UnauthorizedAccessException => "errorFileStorage",
            FormatException or ArgumentException => "errorInvalidInput",
            _ => "errorOperationIncomplete"
        };
        var category = localize?.Invoke(categoryKey) ?? DefaultText(categoryKey);
        Trace.WriteLine($"{category}; exception={exception.GetType().Name}; hresult={exception.HResult}", "Operações");
        // Network/driver failures can echo connection settings and server data. Keep those details out of the UI as well.
        if (types.Any(name => name.StartsWith("Mongo", StringComparison.Ordinal)))
        {
            var suffix = localize?.Invoke("errorMongoDetailsSuffix") ?? ". Verifique o destino, as permissões e os parâmetros informados.";
            return category + suffix;
        }
        var message = exception.Message.Length > 2000 ? exception.Message[..2000] + "…" : exception.Message;
        return category + ": " + MongoUri().Replace(message, "[URI MongoDB protegida]");
    }

    private static string DefaultText(string key) => key switch
    {
        "errorOperationCancelled" => "Operação cancelada",
        "errorTimeout" => "Tempo limite excedido",
        "errorInvalidJson" => "JSON inválido",
        "errorAuthentication" => "Falha de autenticação",
        "errorConnection" => "Falha de conexão",
        "errorMongoQuery" => "Erro de consulta MongoDB",
        "errorMongo" => "Erro MongoDB",
        "errorExport" => "Erro de exportação",
        "errorFileStorage" => "Erro de arquivo ou armazenamento",
        "errorInvalidInput" => "Entrada inválida",
        _ => "Operação não concluída"
    };

    [GeneratedRegex(@"mongodb(?:\+srv)?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MongoUri();
}
