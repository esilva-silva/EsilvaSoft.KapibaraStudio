using Avalonia;
using Avalonia.Controls;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

public sealed class SyntaxSettings : AvaloniaObject
{
    public static readonly AttachedProperty<SyntaxLanguage> LanguageProperty =
        AvaloniaProperty.RegisterAttached<SyntaxSettings, Control, SyntaxLanguage>("Language", SyntaxLanguage.MongoScript, inherits: true);
    public static SyntaxLanguage GetLanguage(Control control) => control.GetValue(LanguageProperty);
    public static void SetLanguage(Control control, SyntaxLanguage value) => control.SetValue(LanguageProperty, value);
}
