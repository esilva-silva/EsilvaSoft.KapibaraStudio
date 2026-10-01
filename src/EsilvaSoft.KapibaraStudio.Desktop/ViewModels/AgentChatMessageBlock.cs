using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Native, inert presentation of text and fenced code. No HTML, links or executable commands.</summary>
public sealed partial class AgentChatMessageBlock(bool isCode, string text, string language) : ObservableObject
{
    public bool IsCode { get; } = isCode;
    [ObservableProperty] private string _text = text;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLanguage))]
    private string _language = language;
    public bool HasLanguage => Language.Length > 0;

    public static IReadOnlyList<AgentChatMessageBlock> Parse(string content)
    {
        var blocks = new List<AgentChatMessageBlock>();
        var buffer = new StringBuilder();
        var code = false;
        var language = "";
        var cursor = 0;
        while (cursor < content.Length)
        {
            var newline = content.IndexOf('\n', cursor);
            var end = newline < 0 ? content.Length : newline + 1;
            var line = content.AsSpan(cursor, end - cursor).Trim();
            var fence = line.StartsWith("```", StringComparison.Ordinal);
            var closes = code && fence && line[3..].IsWhiteSpace();
            if (closes || (!code && fence))
            {
                if (buffer.Length > 0) blocks.Add(new(code, buffer.ToString(), language));
                buffer.Clear();
                code = !code;
                language = code ? line[3..].ToString() : "";
            }
            else buffer.Append(content.AsSpan(cursor, end - cursor));
            cursor = end;
        }
        if (buffer.Length > 0 || code) blocks.Add(new(code, buffer.ToString(), language));
        return blocks;
    }
}
