using System.Text;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Conservative formatter: only indentation at syntax-proven container boundaries; never rewrites literals.</summary>
public sealed class MongoCodeFormatter : ICodeFormatter
{
    public Task<string> FormatAsync(string text, CancellationToken cancellationToken = default) =>
        Task.Run(() => Format(text, cancellationToken), cancellationToken);

    private static string Format(string text, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 1_000_000) throw new ArgumentException("Formatação limitada a 1 milhão de caracteres; selecione um trecho menor.");
        token.ThrowIfCancellationRequested();
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = ExtendedJsonFormatter.MaxDepth });
            var formatted = ExtendedJsonFormatter.Format(text);
            token.ThrowIfCancellationRequested();
            return formatted;
        }
        catch (JsonException) { }

        var parser = new Parser(new ParserOptions { OnToken = (in Token _) => token.ThrowIfCancellationRequested() });
        Node program;
        try { program = parser.ParseScript(text); }
        catch (SyntaxErrorException) when (text.AsSpan().TrimStart().StartsWith("{"))
        { program = parser.ParseExpression(text); }
        var breaks = new SortedDictionary<int, int>();
        var pending = new Stack<(Node Node, int Indent, int Level)>();
        pending.Push((program, 0, 0));
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            var (node, depth, level) = item;
            if (depth > 128 || level > 512) throw new ArgumentException("Estrutura profunda demais para formatação.");
            if (node is TemplateLiteral) continue;
            var container = node is ObjectExpression or ArrayExpression or BlockStatement;
            var children = node.ChildNodes.ToArray();
            if (node is Acornima.Ast.Program)
                foreach (var child in children.Skip(1)) breaks[child.Start] = depth;
            if (container && children.Length > 0)
            {
                foreach (var child in children) breaks[child.Start] = depth + 1;
                breaks[node.End - 1] = depth;
            }
            foreach (var child in children) pending.Push((child, depth + (container ? 1 : 0), level + 1));
        }
        var output = new StringBuilder(text.Length + Math.Min(text.Length, 65536));
        var offset = 0;
        foreach (var (position, depth) in breaks)
        {
            token.ThrowIfCancellationRequested();
            var start = position;
            while (start > offset && char.IsWhiteSpace(text[start - 1])) start--;
            output.Append(text.AsSpan(offset, start - offset));
            if (output.Length > 0) output.Append('\n');
            output.Append(' ', depth * 2);
            offset = position;
        }
        output.Append(text.AsSpan(offset));
        return output.ToString();

    }
}
