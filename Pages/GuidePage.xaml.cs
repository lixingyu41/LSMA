using Microsoft.UI.Xaml.Controls;

namespace LSMA.Pages;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Guide;
    }

    private void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        App.Current.Services.Guide.Search(args.QueryText);
    }
}
