using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;

namespace LSMA.Pages;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Guide;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var query = e.Parameter as string ?? string.Empty;
        if (SearchBox.Text != query)
        {
            SearchBox.Text = query;
        }

        await App.Current.Services.Guide.SearchAsync(query);
    }

    private void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        NavigateSearch(args.QueryText);
    }

    private void SearchAction_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string query } && !string.IsNullOrWhiteSpace(query))
        {
            NavigateSearch(query);
        }
    }

    private void NavigateSearch(string query)
    {
        App.Current.Services.Navigation.Navigate(typeof(GuidePage), query?.Trim() ?? string.Empty);
    }
}
