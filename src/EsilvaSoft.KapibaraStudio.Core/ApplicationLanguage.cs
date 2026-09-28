namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Idioma suportado pela interface do Kapibara Studio.
/// </summary>
public sealed record ApplicationLanguage(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// Catálogo do contrato de idiomas da aplicação.
/// </summary>
public static class ApplicationLanguages
{
    public const string DefaultCode = "pt-BR";
    public const string FallbackCode = "en";

    public static IReadOnlyList<ApplicationLanguage> All { get; } =
    [
        new(DefaultCode, "Português (Brasil)"),
        new("en", "English"),
        new("es", "Español"),
        new("zh-CN", "简体中文")
    ];

    /// <summary>
    /// Retorna o código canônico. Valores ausentes ou não suportados caem em inglês.
    /// O fallback é deliberadamente diferente do idioma inicial pt-BR.
    /// </summary>
    public static string Normalize(string? code)
    {
        var match = All.FirstOrDefault(language =>
            string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? FallbackCode;
    }

    public static bool IsSupported(string? code) =>
        All.Any(language => string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}
