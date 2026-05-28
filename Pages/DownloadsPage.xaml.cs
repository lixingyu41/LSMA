using Microsoft.UI.Xaml.Controls;
using LSMA.ViewModels;

namespace LSMA.Pages;

public sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Downloads;
        Loaded += async (_, _) => await App.Current.Services.Downloads.StartAsync();
    }
}
