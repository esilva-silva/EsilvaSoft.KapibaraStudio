using System.Text;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public static class MongoshOutputParser
{
    public static ParsedMongoshOutput Parse(string standardOutput, string resultPrefix)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPrefix);
        var results = new List<string>();
        var console = new StringBuilder();
        using var reader = new StringReader(standardOutput);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(resultPrefix, StringComparison.Ordinal))
            {
                results.Add(line[resultPrefix.Length..]);
            }
            else
            {
                console.Append(line).Append('\n');
            }
        }

        return new ParsedMongoshOutput(results, console.ToString().TrimEnd());
    }
}
