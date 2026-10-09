using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Incremental parser for the Qwen3 Hermes JSON tool-call envelope.</summary>
internal sealed class LocalAgentToolCallParser(int maximumCallCharacters = 16_384)
{
    private const string Open = "<tool_call>";
    private const string Close = "</tool_call>";
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _call = new();
    private bool _insideCall;
    private bool _callCompleted;

    public string? ToolName { get; private set; }
    public string? ArgumentsJson { get; private set; }
    public string? ErrorCode { get; private set; }

    public string Append(string fragment, bool final = false)
    {
        if (ErrorCode is not null) return "";
        _pending.Append(fragment);
        var emitted = new StringBuilder();
        while (true)
        {
            if (_insideCall)
            {
                var closeAt = _pending.ToString().IndexOf(Close, StringComparison.Ordinal);
                if (closeAt < 0)
                {
                    _call.Append(_pending);
                    _pending.Clear();
                    if (_call.Length > maximumCallCharacters) ErrorCode = "InvalidToolCallFormat";
                    if (final && ErrorCode is null) ErrorCode = "InvalidToolCallFormat";
                    break;
                }

                _call.Append(_pending.ToString(0, closeAt));
                _pending.Remove(0, closeAt + Close.Length);
                if (_call.Length > maximumCallCharacters || !ParseCall(_call.ToString()))
                {
                    ErrorCode = "InvalidToolCallFormat";
                    break;
                }

                _insideCall = false;
                _callCompleted = true;
                if (!string.IsNullOrWhiteSpace(_pending.ToString())) ErrorCode = "InvalidToolCallFormat";
                _pending.Clear();
                break;
            }

            if (_callCompleted)
            {
                if (!string.IsNullOrWhiteSpace(_pending.ToString())) ErrorCode = "InvalidToolCallFormat";
                _pending.Clear();
                break;
            }

            var openAt = _pending.ToString().IndexOf(Open, StringComparison.Ordinal);
            if (openAt >= 0)
            {
                emitted.Append(_pending.ToString(0, openAt));
                _pending.Remove(0, openAt + Open.Length);
                _insideCall = true;
                continue;
            }

            if (final)
            {
                emitted.Append(_pending);
                _pending.Clear();
            }
            else
            {
                var pendingText = _pending.ToString();
                var retainedMarkerPrefix = LongestMarkerPrefixSuffix(pendingText);
                var safeLength = _pending.Length - retainedMarkerPrefix;
                if (safeLength > 0)
                {
                    emitted.Append(_pending.ToString(0, safeLength));
                    _pending.Remove(0, safeLength);
                }
            }
            break;
        }

        return emitted.ToString();
    }

    private static int LongestMarkerPrefixSuffix(string text)
    {
        var maximum = Math.Min(text.Length, Open.Length - 1);
        for (var length = maximum; length > 0; length--)
            if (text.AsSpan(text.Length - length).SequenceEqual(Open.AsSpan(0, length))) return length;
        return 0;
    }

    private bool ParseCall(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            string? name = null;
            JsonElement? arguments = null;
            var seenName = false;
            var seenArguments = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("name") && !seenName && property.Value.ValueKind == JsonValueKind.String)
                {
                    seenName = true;
                    name = property.Value.GetString();
                }
                else if (property.NameEquals("arguments") && !seenArguments && property.Value.ValueKind == JsonValueKind.Object)
                {
                    seenArguments = true;
                    arguments = property.Value;
                }
                else return false;
            }

            if (!seenName || !seenArguments || string.IsNullOrWhiteSpace(name) || name.Length > 96 ||
                name.Any(static character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')))
                return false;
            ToolName = name;
            ArgumentsJson = arguments!.Value.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
