using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

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

    private async void SearchAction_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string query } && !string.IsNullOrWhiteSpace(query))
        {
            SearchBox.Text = query;
            await App.Current.Services.Guide.SearchAsync(query);
        }
    }
}
