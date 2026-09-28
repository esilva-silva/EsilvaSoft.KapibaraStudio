using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Cria uma ligação para uma chave do catálogo global, independente do DataContext da janela.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        return new Binding
        {
            Path = nameof(LocalizationViewModel.Language),
            Source = LocalizationViewModel.Current,
            Converter = new LocalizationValueConverter(Key)
        };
    }

    private sealed class LocalizationValueConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            LocalizationViewModel.Current.Resolve(key);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
