using Avalonia.Controls;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class ThirdPartyLicensesWindow : Window
{
    public ThirdPartyLicensesWindow()
    {
        InitializeComponent();

        var localization = LocalizationViewModel.Current;
        Title = localization.Resolve("thirdPartyLicensesTitle");
        TitleText.Text = Title;
        DescriptionText.Text = localization.Resolve("thirdPartyLicensesDescription");
        CloseText.Text = localization.Resolve("close");

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        try
        {
            MarkdownNoticeRenderer.Render(File.ReadAllText(noticesPath), NoticeContent);
        }
        catch (IOException)
        {
            NoticeContent.Children.Add(new SelectableTextBlock
            {
                Text = localization.Resolve("thirdPartyLicensesMissing"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
        catch (UnauthorizedAccessException)
        {
            NoticeContent.Children.Add(new SelectableTextBlock
            {
                Text = localization.Resolve("thirdPartyLicensesMissing"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
