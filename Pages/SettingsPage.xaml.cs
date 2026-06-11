using System.ComponentModel;
using LSMA.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LSMA.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly DispatcherTimer _updateNoticeTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private Storyboard? _updateNoticeAnimation;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.SettingsPage;
        _updateNoticeTimer.Tick += (_, _) =>
        {
            _updateNoticeTimer.Stop();
            AnimateUpdateNotice(false);
        };
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        SyncAppearanceSelection();
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _updateNoticeTimer.Stop();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.IsDarkThemeSelected)
            or nameof(SettingsViewModel.IsLightThemeSelected)
            or nameof(SettingsViewModel.IsSystemThemeSelected))
        {
            SyncAppearanceSelection();
        }
        else if (e.PropertyName is nameof(SettingsViewModel.UpdateNoticeText))
        {
            _updateNoticeTimer.Stop();
            AnimateUpdateNotice(true);
            _updateNoticeTimer.Start();
        }
    }

    private void SyncAppearanceSelection()
    {
        ApplyAppearanceOptionStyle(DarkThemeOption, ViewModel.IsDarkThemeSelected);
        ApplyAppearanceOptionStyle(LightThemeOption, ViewModel.IsLightThemeSelected);
        ApplyAppearanceOptionStyle(SystemThemeOption, ViewModel.IsSystemThemeSelected);
    }

    private static void ApplyAppearanceOptionStyle(Button button, bool selected)
    {
        var state = selected ? "selected" : "normal";
        if (Equals(button.Tag, state))
        {
            return;
        }

        button.Tag = state;
        var styleKey = selected ? "SelectedChoiceButtonStyle" : "ChoiceButtonStyle";
        button.Style = (Style)Application.Current.Resources[styleKey];
    }

    private async void DarkThemeOption_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DarkThemeCommand.ExecuteAsync(null);
        SyncAppearanceSelection();
    }

    private async void LightThemeOption_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LightThemeCommand.ExecuteAsync(null);
        SyncAppearanceSelection();
    }

    private async void SystemThemeOption_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SystemThemeCommand.ExecuteAsync(null);
        SyncAppearanceSelection();
    }

    private async void Donate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = CreateDonationDialogContent(),
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private static StackPanel CreateDonationDialogContent()
    {
        return new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Image
                {
                    Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "DonationRewardCode.jpg"))),
                    Width = 420,
                    Height = 420,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
    }

    private void AnimateUpdateNotice(bool show)
    {
        _updateNoticeAnimation?.Stop();
        if (show)
        {
            UpdateNoticeBubble.Visibility = Visibility.Visible;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(190));
        var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var move = new DoubleAnimation
        {
            To = show ? 0 : 40,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(move, UpdateNoticeTransform);
        Storyboard.SetTargetProperty(move, "Y");

        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacity, UpdateNoticeBubble);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var animation = new Storyboard();
        animation.Children.Add(move);
        animation.Children.Add(opacity);
        if (!show)
        {
            animation.Completed += (_, _) => UpdateNoticeBubble.Visibility = Visibility.Collapsed;
        }

        _updateNoticeAnimation = animation;
        animation.Begin();
    }
}
