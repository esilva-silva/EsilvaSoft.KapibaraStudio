using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>Renders the CommonMark constructs used by the shipped third-party notices.</summary>
internal static partial class MarkdownNoticeRenderer
{
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, monospace");

    public static void Render(string markdown, StackPanel destination)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(destination);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var codeLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    codeLines.Add(lines[i++]);
                if (i < lines.Length) i++;
                AddCodeBlock(string.Join(Environment.NewLine, codeLines), destination);
                continue;
            }

            if (i + 1 < lines.Length && IsTableHeader(line) && IsTableSeparator(lines[i + 1]))
            {
                var headers = ParseTableRow(lines[i]);
                i += 2;
                var rows = new List<IReadOnlyList<string>>();
                while (i < lines.Length && IsTableHeader(lines[i])) rows.Add(ParseTableRow(lines[i++]));
                AddTable(headers, rows, destination);
                continue;
            }

            if (TryGetHeading(line, out var level, out var heading))
            {
                var headingBlock = CreateTextBlock(heading);
                AutomationProperties.SetName(headingBlock, heading);
                headingBlock.FontSize = level switch { 1 => 22, 2 => 19, 3 => 16, _ => 14 };
                headingBlock.FontWeight = FontWeight.SemiBold;
                headingBlock.Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 2);
                destination.Children.Add(headingBlock);
                i++;
                continue;
            }

            if (IsHorizontalRule(line))
            {
                destination.Children.Add(new Border
                {
                    Classes = { "markdown-rule" }
                });
                i++;
                continue;
            }

            if (TryGetListItem(line, out var marker, out var listText))
            {
                var item = CreateTextBlock(listText);
                item.Margin = new Thickness(16, 1, 0, 1);
                item.Inlines!.Insert(0, new Run(marker + " "));
                destination.Children.Add(item);
                i++;
                continue;
            }

            var paragraphLines = new List<string> { line.Trim() };
            i++;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines, i))
                paragraphLines.Add(lines[i++].Trim());
            destination.Children.Add(CreateTextBlock(string.Join(' ', paragraphLines)));
        }
    }

    private static void AddCodeBlock(string code, Panel destination)
    {
        var text = new SelectableTextBlock
        {
            Text = code,
            FontFamily = CodeFont,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2)
        };
        AutomationProperties.SetName(text, code);
        destination.Children.Add(new Border
        {
            Classes = { "license-code" },
            Child = text
        });
    }

    private static void AddTable(string[] headers, IReadOnlyList<IReadOnlyList<string>> rows, Panel destination)
    {
        var table = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var row in rows)
        {
            var fields = new StackPanel { Spacing = 4 };
            for (var column = 0; column < Math.Min(headers.Length, row.Count); column++)
            {
                var field = new SelectableTextBlock
                {
                    Inlines = new InlineCollection(),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1)
                };
                AutomationProperties.SetName(field, headers[column] + ": " + row[column]);
                field.Inlines.Add(new Bold { Inlines = { new Run(headers[column] + ": ") } });
                AddInlineMarkdown(field.Inlines, row[column]);
                fields.Children.Add(field);
            }
            table.Children.Add(new Border
            {
                Classes = { "license-table-row" },
                Child = fields
            });
        }
        destination.Children.Add(table);
    }

    private static SelectableTextBlock CreateTextBlock(string markdown)
    {
        var text = new SelectableTextBlock
        {
            Inlines = new InlineCollection(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2)
        };
        AutomationProperties.SetName(text, InlineTokenRegex().Replace(markdown, match =>
            match.Groups["label"].Success ? $"{match.Groups["label"].Value} ({match.Groups["address"].Value})" :
            match.Groups["boldText"].Success ? match.Groups["boldText"].Value :
            match.Groups["italicText"].Success ? match.Groups["italicText"].Value :
            match.Groups["codeText"].Success ? match.Groups["codeText"].Value : match.Value));
        AddInlineMarkdown(text.Inlines!, markdown);
        return text;
    }

    private static void AddInlineMarkdown(InlineCollection inlines, string text)
    {
        var cursor = 0;
        foreach (Match match in InlineTokenRegex().Matches(text))
        {
            if (match.Index > cursor) inlines.Add(new Run(text[cursor..match.Index]));

            if (match.Groups["label"].Success)
            {
                inlines.Add(new Run($"{match.Groups["label"].Value} ({match.Groups["address"].Value})")
                {
                    Classes = { "markdown-link" }
                });
            }
            else if (match.Groups["boldText"].Success)
                inlines.Add(new Bold { Inlines = { new Run(match.Groups["boldText"].Value) } });
            else if (match.Groups["italicText"].Success)
                inlines.Add(new Italic { Inlines = { new Run(match.Groups["italicText"].Value) } });
            else if (match.Groups["codeText"].Success)
                inlines.Add(new Run(match.Groups["codeText"].Value) { FontFamily = CodeFont });

            cursor = match.Index + match.Length;
        }
        if (cursor < text.Length) inlines.Add(new Run(text[cursor..]));
    }

    private static bool StartsBlock(string[] lines, int index) =>
        TryGetHeading(lines[index], out _, out _) || IsHorizontalRule(lines[index]) ||
        TryGetListItem(lines[index], out _, out _) || lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal) ||
        index + 1 < lines.Length && IsTableHeader(lines[index]) && IsTableSeparator(lines[index + 1]);

    private static bool TryGetHeading(string line, out int level, out string text)
    {
        var match = HeadingRegex().Match(line);
        if (!match.Success) { level = 0; text = string.Empty; return false; }
        level = match.Groups["marks"].Length;
        text = match.Groups["text"].Value.Trim().TrimEnd('#').TrimEnd();
        return true;
    }

    private static bool TryGetListItem(string line, out string marker, out string text)
    {
        var match = ListItemRegex().Match(line);
        if (!match.Success) { marker = string.Empty; text = string.Empty; return false; }
        marker = match.Groups["bullet"].Success ? "•" : match.Groups["number"].Value;
        text = match.Groups["text"].Value;
        return true;
    }

    private static bool IsTableHeader(string line) => line.TrimStart().StartsWith('|') && line.TrimEnd().EndsWith('|');
    private static bool IsTableSeparator(string line) => IsTableHeader(line) && ParseTableRow(line).All(cell => SeparatorCellRegex().IsMatch(cell));
    private static bool IsHorizontalRule(string line) => HorizontalRuleRegex().IsMatch(line.Trim());

    private static string[] ParseTableRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    [GeneratedRegex("^(?<marks>#{1,6})\\s+(?<text>.*?)\\s*#*\\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("^\\s*(?:(?<bullet>[-+*])|(?<number>\\d+[.)]))\\s+(?<text>.*)$")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex("^(?:[-*_]\\s*){3,}$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex("^:?-{3,}:?$")]
    private static partial Regex SeparatorCellRegex();

    [GeneratedRegex("\\[(?<label>[^\\]]+)\\]\\((?<address>https?://[^)\\s]+)\\)|\\*\\*(?<boldText>.+?)\\*\\*|(?<!\\*)\\*(?<italicText>[^*]+)\\*(?!\\*)|`(?<codeText>[^`]+)`")]
    private static partial Regex InlineTokenRegex();
}
