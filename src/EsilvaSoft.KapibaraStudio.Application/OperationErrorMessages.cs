using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Shared UI diagnostic categories. Logs contain type/category only, never document text or resolved credentials.</summary>
public static partial class OperationErrorMessages
{
    public static string Describe(Exception exception, bool export = false, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is MongoRoleAdministrationException roleFailure)
        {
            var roleKey = roleFailure.Kind switch
            {
                MongoRoleAdministrationFailureKind.PermissionDenied => "errorRolePermission",
                MongoRoleAdministrationFailureKind.Unsupported => "errorRoleUnsupported",
                MongoRoleAdministrationFailureKind.ReadbackFailed => "errorRoleReadback",
                MongoRoleAdministrationFailureKind.InvalidInput => "errorRoleInput",
                _ => "errorRoleCommand"
            };
            var roleMessage = ResolveRoleOrUserText(roleKey, localize);
            Trace.WriteLine($"{roleKey}; exception={exception.GetType().Name}", "Operações");
            return roleMessage;
        }
        if (exception is MongoUserAdministrationException userFailure)
        {
            var key = userFailure.Kind switch
            {
                MongoUserAdministrationFailureKind.ReadbackFailed => "errorUserReadback",
                MongoUserAdministrationFailureKind.PermissionDenied => "errorUserPermission",
                MongoUserAdministrationFailureKind.Unsupported => "errorUserUnsupported",
                MongoUserAdministrationFailureKind.InvalidInput => "errorUserInput",
                MongoUserAdministrationFailureKind.Conflict => "errorUserConflict",
                _ => "errorUserCommand"
            };
            var userMessage = ResolveRoleOrUserText(key, localize);
            Trace.WriteLine($"{key}; kind={userFailure.Kind}; exception={exception.GetType().Name}", "Operações");
            return userMessage;
        }
        if (exception is RuntimeServerParameterException parameterFailure)
        {
            var parameterKey = parameterFailure.Kind switch
            {
                RuntimeServerParameterFailureKind.PermissionDenied => "errorRuntimeParameterPermission",
                RuntimeServerParameterFailureKind.Unsupported => "errorRuntimeParameterUnsupported",
                _ => "errorRuntimeParameterReadback"
            };
            var parameterMessage = localize?.Invoke(parameterKey) ?? DefaultText(parameterKey);
            Trace.WriteLine($"{parameterKey}; exception={exception.GetType().Name}", "Operações");
            return parameterMessage;
        }

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
        "errorRolePermission" => "O servidor recusou a operação de papéis por falta de permissão.",
        "errorRoleUnsupported" => "Este deployment do MongoDB não oferece suporte ao comando de papéis personalizados.",
        "errorRoleReadback" => "O comando foi enviado, mas não foi possível confirmar o estado final do papel. Atualize a leitura antes de tentar novamente.",
        "errorRoleInput" => "Os dados do papel não são válidos. Revise a entrada antes de tentar novamente.",
        "errorRoleCommand" => "A operação de papéis não foi concluída. Releia o estado antes de tentar novamente.",
        "errorUserReadback" => "O comando de usuário foi enviado, mas não foi possível confirmar o estado final. Atualize a leitura antes de tentar novamente.",
        "errorUserPermission" => "O servidor recusou a operação de usuário por falta de permissão.",
        "errorUserUnsupported" => "Este deployment não oferece suporte à operação de usuário solicitada.",
        "errorUserInput" => "Os dados de usuário ou papéis não são válidos. Revise a entrada antes de tentar novamente.",
        "errorUserConflict" => "O usuário ou seus papéis mudaram. Atualize a leitura antes de tentar novamente.",
        "errorUserCommand" => "A operação de usuário não foi concluída. Releia o estado antes de tentar novamente.",
        "errorRuntimeParameterPermission" => "O MongoDB recusou a alteração runtime por falta de permissão.",
        "errorRuntimeParameterUnsupported" => "Este deployment ou versão não permite consultar/alterar o parâmetro runtime selecionado.",
        "errorRuntimeParameterReadback" => "A alteração runtime foi enviada, mas não foi possível confirmar o valor final. Atualize a leitura antes de tentar novamente.",
        _ => "Operação não concluída"
    };

    private static string ResolveRoleOrUserText(string key, Func<string, string>? localize)
    {
        var translated = localize?.Invoke(key);
        return string.IsNullOrWhiteSpace(translated)
            || string.Equals(translated, key, StringComparison.Ordinal)
            || string.Equals(translated, "[[" + key + "]]", StringComparison.Ordinal)
            ? DefaultText(key) : translated;
    }

    [GeneratedRegex(@"mongodb(?:\+srv)?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MongoUri();
}
