using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Conservative opt-out for recognizable secrets. No ENV expansion and no configuration access.</summary>
public static class CompletionPrivacy
{
    /// <summary>
    /// Wall-clock safety net for a scan of up to 65 536 characters. A 50 ms budget timed out on busy CI runners
    /// (GC and preemption count toward it), so the privacy pattern runs on the linear non-backtracking engine.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex Sensitive = new("mongodb(?:\\+srv)?://|(?:password|passwd|pwd|secret|access[_-]?token|api[_-]?key|authorization|connection[_-]?string)\\s*[\\\"']?\\s*[:=]|Bearer\\s+|-----BEGIN .*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, MatchTimeout);

    /// <summary>Fails closed: text that cannot be checked in time is treated as sensitive and is never sent to the model.</summary>
    public static bool ContainsSensitiveText(string text)
    {
        try { return Sensitive.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return true; }
    }
}
