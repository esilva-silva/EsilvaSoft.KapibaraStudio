using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public static class QueryServerDiagnostics
{
    public static QueryDiagnosticException Describe(MongoCommandException exception)
    {
        var message = exception.Message;
        var guidance = message.Contains("Unrecognized pipeline stage name", StringComparison.OrdinalIgnoreCase)
            ? "Stage desconhecido. Confira o nome após $ e a compatibilidade com a versão do servidor."
            : message.Contains("Unrecognized expression", StringComparison.OrdinalIgnoreCase)
            ? "Expressão desconhecida. Confira o operador e a compatibilidade com a versão do servidor."
            : message.Contains("must be an accumulator", StringComparison.OrdinalIgnoreCase)
            ? "$group exige um acumulador em cada campo de saída, além de _id; por exemplo, $sum ou $first."
            : message.Contains("must contain exactly one field", StringComparison.OrdinalIgnoreCase)
            ? "Cada documento do pipeline deve conter exatamente um operador de stage."
            : message.Contains("Use of undefined variable", StringComparison.OrdinalIgnoreCase)
            ? "Variável não definida. Revise as referências $$ e o escopo de let no pipeline."
            : "Revise os tipos dos argumentos, os operadores e as permissões no destino desta aba.";
        // Only a numeric code and fixed application text cross the boundary. Server messages can contain data or credentials.
        return new QueryDiagnosticException($"O MongoDB recusou a operação (código {exception.Code}). {guidance}", exception.Code);
    }
}
