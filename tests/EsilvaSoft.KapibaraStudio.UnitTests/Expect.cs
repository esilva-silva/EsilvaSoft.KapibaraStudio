namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal static class Expect
{
    /// <summary>Expected names as a space-separated list, avoiding constant array arguments.</summary>
    public static string[] Words(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
