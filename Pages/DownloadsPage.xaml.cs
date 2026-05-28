using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LSMA.Models;
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

    private void NexusButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is NexusModInfo mod)
        {
            ((DownloadsViewModel)DataContext).OpenNexusCommand.Execute(mod);
        }
    }
}
