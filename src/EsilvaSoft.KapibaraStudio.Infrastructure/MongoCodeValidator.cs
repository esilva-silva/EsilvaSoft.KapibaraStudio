using Acornima;
using Acornima.Ast;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Offline parsing only: no evaluation, metadata, connection, or environment resolution.</summary>
public sealed class MongoCodeValidator : ICodeValidator
{
    public Task<CodeValidationResult> ValidateAsync(string text, bool aggregation, CancellationToken cancellationToken = default) =>
        Task.Run(() => Validate(text, aggregation, cancellationToken), cancellationToken);

    private static CodeValidationResult Validate(string text, bool aggregation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (text.Length > 1_000_000) return new(false, "Validação limitada a 1 milhão de caracteres; selecione um trecho menor.");
        if (string.IsNullOrWhiteSpace(text)) return new(false, "Digite uma consulta ou selecione um trecho para validar.");
        try
        {
            var parser = new Parser(new ParserOptions { OnToken = (in Token _) => token.ThrowIfCancellationRequested() });
            if (aggregation)
            {
                var expression = parser.ParseExpression(text);
                if (expression is not ArrayExpression array) return Error(expression, "O pipeline deve ser um array de estágios.");
                var pending = new Stack<(ArrayExpression Array, string Path, int Depth)>();
                pending.Push((array, "pipeline", 0));
                while (pending.TryPop(out var item))
                {
                    token.ThrowIfCancellationRequested();
                    if (item.Depth > 64) return Error(item.Array, "Pipelines aninhados excedem a profundidade de 64.");
                    for (var index = 0; index < item.Array.Elements.Count; index++)
                    {
                        var node = item.Array.Elements[index];
                        var path = $"{item.Path}[{index}] (estágio {index + 1})";
                        if (node is not ObjectExpression obj || obj.Properties.Count != 1 || obj.Properties[0] is not Property property)
                            return Error(node ?? item.Array, $"{path}: informe um documento com exatamente um operador de estágio.");
                        var name = Name(property);
                        if (!name.StartsWith('$')) return Error(property.Key, $"{path}: o operador do estágio deve começar com $.");
                        if (name is "$out" or "$merge") return Error(property.Key, $"{path}: {name} escreve no servidor e não é permitido no cursor de leitura.");
                        if (property.Value is not ObjectExpression body) continue;
                        foreach (var child in body.Properties.OfType<Property>())
                        {
                            if (name != "$facet" && !(name is "$lookup" or "$unionWith" && Name(child) == "pipeline")) continue;
                            if (child.Value is not ArrayExpression nested) return Error(child.Value, $"{path}.{name}.{Name(child)}: informe um array de estágios.");
                            pending.Push((nested, $"{path}.{name}.{Name(child)}", item.Depth + 1));
                        }
                    }
                }
            }
            else parser.ParseScript(text);
            return new(true, "Sintaxe válida localmente. Tipos BSON, operadores, permissões e compatibilidade serão validados na execução. Nenhuma consulta foi enviada.");
        }
        catch (SyntaxErrorException ex)
        {
            var line = Math.Max(1, ex.LineNumber);
            var offset = 0;
            for (var current = 1; current < line && offset < text.Length; offset++) if (text[offset] == '\n') current++;
            offset = Math.Min(text.Length, offset + Math.Max(0, ex.Column));
            return new(false, $"Erro de sintaxe na linha {line}, coluna {ex.Column + 1}: {ex.Message}", offset, offset < text.Length ? 1 : 0);
        }

        string Name(Property property) => property.Key is Identifier identifier ? identifier.Name : text[property.Key.Start..property.Key.End].Trim('\'', '"');
        CodeValidationResult Error(Node node, string message) => new(false,
            $"Linha {node.Location.Start.Line}, coluna {node.Location.Start.Column + 1}: {message}", node.Start, Math.Max(1, node.End - node.Start));
    }
}
