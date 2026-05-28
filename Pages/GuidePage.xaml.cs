using Microsoft.UI.Xaml.Controls;

namespace LSMA.Pages;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Guide;
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await App.Current.Services.Guide.SearchAsync(args.QueryText);
    }
}
