using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using System.Globalization;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>
/// A parsed, intentionally small LSP snippet. Parsing before insertion keeps malformed
/// catalog data from reaching the editor and keeps placeholder offsets deterministic.
/// </summary>
public sealed class SnippetTemplate
{
    private readonly IReadOnlyList<Part> _parts;

    private SnippetTemplate(string source, IReadOnlyList<Part> parts)
    {
        Source = source;
        _parts = parts;
    }

    public string Source { get; }

    public static SnippetTemplate Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parser = new Parser(source);
        return new SnippetTemplate(source, parser.Parse());
    }

    public static bool TryParse(string? source, out SnippetTemplate? template, out string? error)
    {
        template = null;
        error = null;
        if (source is null)
        {
            error = "O snippet não pode ser nulo.";
            return false;
        }

        try
        {
            template = Parse(source);
            return true;
        }
        catch (FormatException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public SnippetExpansion Expand()
    {
        var text = new StringBuilder();
        var placeholders = new List<SnippetPlaceholder>();
        Expand(_parts, text, placeholders);
        return new SnippetExpansion(text.ToString(), placeholders);
    }

    private static void Expand(IReadOnlyList<Part> parts, StringBuilder text, List<SnippetPlaceholder> placeholders)
    {
        foreach (var part in parts)
        {
            switch (part)
            {
                case TextPart literal:
                    text.Append(literal.Value);
                    break;
                case TabStopPart tabStop:
                    placeholders.Add(new(tabStop.Index, new TextSpan(text.Length, 0), null, []));
                    break;
                case PlaceholderPart placeholder:
                    var start = text.Length;
                    Expand(placeholder.Default, text, placeholders);
                    placeholders.Add(new(placeholder.Index, new TextSpan(start, text.Length - start), null, []));
                    break;
                case ChoicePart choice:
                    var choiceStart = text.Length;
                    text.Append(choice.Values[0]);
                    placeholders.Add(new(choice.Index, new TextSpan(choiceStart, text.Length - choiceStart), choice.Values[0], choice.Values));
                    break;
            }
        }
    }

    private abstract record Part;
    private sealed record TextPart(string Value) : Part;
    private sealed record TabStopPart(int Index) : Part;
    private sealed record PlaceholderPart(int Index, IReadOnlyList<Part> Default) : Part;
    private sealed record ChoicePart(int Index, IReadOnlyList<string> Values) : Part;

    private sealed class Parser(string source)
    {
        private int _position;

        public List<Part> Parse()
        {
            var parts = ParseParts(false);
            if (_position != source.Length) Fail("texto após o fim do snippet");
            return parts;
        }

        private List<Part> ParseParts(bool stopAtCloseBrace)
        {
            var parts = new List<Part>();
            var literal = new StringBuilder();
            while (_position < source.Length)
            {
                var character = source[_position];
                if (stopAtCloseBrace && character == '}') break;
                if (character == '$')
                {
                    AddLiteral(parts, literal);
                    parts.Add(ParseDollar());
                    continue;
                }
                if (character == '\\')
                {
                    literal.Append(ParseEscape());
                    continue;
                }
                literal.Append(character);
                _position++;
            }
            AddLiteral(parts, literal);
            return parts;
        }

        private Part ParseDollar()
        {
            var dollar = _position++;
            if (_position == source.Length) FailAt(dollar, "cifrão sem placeholder");
            if (char.IsAsciiDigit(source[_position])) return new TabStopPart(ParseIndex());
            if (source[_position] != '{') FailAt(dollar, "placeholder deve usar um índice numérico");
            _position++;
            if (_position == source.Length || !char.IsAsciiDigit(source[_position])) Fail("placeholder sem índice numérico");
            var index = ParseIndex();
            if (_position == source.Length) Fail("placeholder sem fechamento");
            return source[_position] switch
            {
                '}' => CloseTabStop(index),
                ':' => ParsePlaceholder(index),
                '|' => ParseChoice(index),
                _ => throw Error("separador de placeholder inválido")
            };
        }

        private TabStopPart CloseTabStop(int index)
        {
            _position++;
            return new TabStopPart(index);
        }

        private PlaceholderPart ParsePlaceholder(int index)
        {
            _position++;
            var content = ParseParts(true);
            if (_position == source.Length) Fail("placeholder sem fechamento");
            _position++;
            return new PlaceholderPart(index, content);
        }

        private ChoicePart ParseChoice(int index)
        {
            _position++;
            var choices = new List<string>();
            var choice = new StringBuilder();
            while (_position < source.Length)
            {
                var character = source[_position++];
                if (character == '\\')
                {
                    if (_position == source.Length) Fail("escape incompleto na escolha");
                    var escaped = source[_position++];
                    if (escaped is not ('\\' or '$' or '}' or ',' or '|')) Fail("escape inválido na escolha");
                    choice.Append(escaped);
                    continue;
                }
                if (character == ',')
                {
                    AddChoice(choices, choice);
                    continue;
                }
                if (character == '|' && _position < source.Length && source[_position] == '}')
                {
                    AddChoice(choices, choice);
                    _position++;
                    return new ChoicePart(index, choices);
                }
                choice.Append(character);
            }
            throw Error("escolha sem fechamento '|}'");
        }

        private char ParseEscape()
        {
            var slash = _position++;
            if (_position == source.Length) FailAt(slash, "escape incompleto");
            var escaped = source[_position++];
            if (escaped is not ('\\' or '$' or '}')) FailAt(slash, "escape inválido");
            return escaped;
        }

        private int ParseIndex()
        {
            var start = _position;
            while (_position < source.Length && char.IsAsciiDigit(source[_position])) _position++;
            var value = source[start.._position];
            if (value.Length > 1 && value[0] == '0') FailAt(start, "índice não pode ter zero à esquerda");
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) FailAt(start, "índice fora do intervalo");
            return index;
        }

        private static void AddLiteral(List<Part> parts, StringBuilder literal)
        {
            if (literal.Length == 0) return;
            parts.Add(new TextPart(literal.ToString()));
            literal.Clear();
        }

        private void AddChoice(List<string> choices, StringBuilder choice)
        {
            if (choice.Length == 0) Fail("escolha vazia");
            choices.Add(choice.ToString());
            choice.Clear();
        }

        private FormatException Error(string message) => new($"Snippet inválido na posição {_position}: {message}.");
        private void Fail(string message) => throw Error(message);
        private static void FailAt(int position, string message) => throw new FormatException($"Snippet inválido na posição {position}: {message}.");
    }
}
