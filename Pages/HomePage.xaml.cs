using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LSMA.Pages;

public sealed partial class HomePage : Page
{
    private CancellationTokenSource? _hideOptionsCts;

    public HomePage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Home;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var target = App.Current.Services.Settings.Current.DefaultLaunchTarget;
        UpdateLaunchToggle(target == Models.LaunchTarget.Smapi);
        SteamCheckbox.IsChecked = App.Current.Services.Settings.Current.LaunchViaSteam;
    }

    private void UpdateLaunchToggle(bool isSmapi)
    {
        SmapiBtn.Style = isSmapi
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["DangerButtonStyle"];
        VanillaBtn.Style = !isSmapi
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["DangerButtonStyle"];
    }

    private void LaunchFloating_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hideOptionsCts?.Cancel();
        LaunchOptionsPopup.Visibility = Visibility.Visible;
        var target = App.Current.Services.Settings.Current.DefaultLaunchTarget;
        UpdateLaunchToggle(target == Models.LaunchTarget.Smapi);
        SteamCheckbox.IsChecked = App.Current.Services.Settings.Current.LaunchViaSteam;
    }

    private async void LaunchFloating_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hideOptionsCts?.Cancel();
        _hideOptionsCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _hideOptionsCts.Token);
            LaunchOptionsPopup.Visibility = Visibility.Collapsed;
        }
        catch (TaskCanceledException) { }
    }

    private async void LaunchSmapi_Click(object sender, RoutedEventArgs e)
    {
        await App.Current.Services.SettingsPage.UseSmapiCommand.ExecuteAsync(null);
        UpdateLaunchToggle(true);
    }

    private async void LaunchVanilla_Click(object sender, RoutedEventArgs e)
    {
        await App.Current.Services.SettingsPage.UseVanillaCommand.ExecuteAsync(null);
        UpdateLaunchToggle(false);
    }

    private async void SteamCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        await App.Current.Services.Settings.UpdateAsync(s => s.LaunchViaSteam = SteamCheckbox.IsChecked == true);
    }

    private async void LaunchGameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SteamCheckbox.IsChecked == true)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("steam://rungameid/413150"));
        }
        else
        {
            await App.Current.Services.Home.LaunchGameCommand.ExecuteAsync(null);
        }
    }
}
