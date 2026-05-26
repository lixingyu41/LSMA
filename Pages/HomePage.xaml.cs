using Microsoft.UI.Xaml.Controls;

namespace LSMA.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Home;
    }

    private async void UseSmapi_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Current.Services.Home.UseSmapiCommand.ExecuteAsync(null);
    }

    private async void UseVanilla_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Current.Services.Home.UseVanillaCommand.ExecuteAsync(null);
    }

    private async void UseQuickMode_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Current.Services.Home.UseQuickModeCommand.ExecuteAsync(null);
    }

    private async void UseSafeMode_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Current.Services.Home.UseSafeModeCommand.ExecuteAsync(null);
    }

    private async void UseDiagnosticMode_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Current.Services.Home.UseDiagnosticModeCommand.ExecuteAsync(null);
    }
}
