using LSMA.Models;
using LSMA.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI;

namespace LSMA;

public sealed partial class MainWindow : Window
{
    private AppPalette _palette = AppPalette.Stardrop;
    private bool _layoutLoaded;
    private int _appearanceTransitionVersion;
    private Storyboard? _appearanceStoryboard;
    private TaskCompletionSource? _appearanceStoryboardCompletion;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }
        SetTitleBar(AppTitleBarDragRegion);
        RootLayout.ActualThemeChanged += (_, _) =>
        {
            UpdateTitleBarButtons();
            App.Current.Services.SettingsPage.Refresh();
        };
        App.Current.Services.Navigation.AttachFrame(ContentFrame);
        RootLayout.Loaded += RootLayout_Loaded;
    }

    public void ApplyAppearance(AppTheme theme, AppPalette palette)
    {
        _palette = palette;
        var requestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.System => ElementTheme.Default,
            _ => ElementTheme.Dark
        };

        if (_layoutLoaded && RootLayout.RequestedTheme != requestedTheme)
        {
            _ = TransitionThemeAsync(requestedTheme);
            return;
        }

        RootLayout.RequestedTheme = requestedTheme;
        UpdateTitleBarButtons();
    }

    public AppTheme GetDisplayedTheme()
    {
        return RootLayout.ActualTheme == ElementTheme.Light ? AppTheme.Light : AppTheme.Dark;
    }

    private async Task TransitionThemeAsync(ElementTheme requestedTheme)
    {
        var version = ++_appearanceTransitionVersion;
        if (RootLayout.Background is SolidColorBrush background)
        {
            ThemeTransitionOverlay.Background = new SolidColorBrush(background.Color);
        }

        ThemeTransitionOverlay.Opacity = 0;
        ThemeTransitionOverlay.Visibility = Visibility.Visible;
        await AnimateThemeOverlayAsync(1, 95);
        if (version != _appearanceTransitionVersion)
        {
            return;
        }

        RootLayout.RequestedTheme = requestedTheme;
        UpdateTitleBarButtons();
        await AnimateThemeOverlayAsync(0, 180);
        if (version == _appearanceTransitionVersion)
        {
            ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private Task AnimateThemeOverlayAsync(double opacity, int milliseconds)
    {
        _appearanceStoryboard?.Stop();
        _appearanceStoryboardCompletion?.TrySetResult();

        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase
            {
                EasingMode = opacity > ThemeTransitionOverlay.Opacity ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        Storyboard.SetTarget(animation, ThemeTransitionOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult();
        _appearanceStoryboard = storyboard;
        _appearanceStoryboardCompletion = completion;
        storyboard.Begin();
        return completion.Task;
    }

    private void UpdateTitleBarButtons()
    {
        var light = RootLayout.ActualTheme == ElementTheme.Light;
        AppWindow.TitleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonForegroundColor = light ? Color.FromArgb(255, 37, 37, 37) : Color.FromArgb(255, 244, 244, 245);
        AppWindow.TitleBar.ButtonInactiveForegroundColor = light ? Color.FromArgb(255, 108, 104, 96) : Color.FromArgb(255, 169, 171, 179);
        var hover = (_palette, light) switch
        {
            (AppPalette.Junimo, true) => Color.FromArgb(255, 225, 240, 217),
            (AppPalette.Junimo, false) => Color.FromArgb(255, 36, 54, 34),
            (AppPalette.Moonlight, true) => Color.FromArgb(255, 221, 236, 245),
            (AppPalette.Moonlight, false) => Color.FromArgb(255, 25, 47, 66),
            (AppPalette.Cranberry, true) => Color.FromArgb(255, 245, 220, 226),
            (AppPalette.Cranberry, false) => Color.FromArgb(255, 65, 32, 42),
            (_, true) => Color.FromArgb(255, 235, 230, 216),
            _ => Color.FromArgb(255, 58, 50, 26)
        };
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hover;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = hover;
    }

    private async void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        RootLayout.Loaded -= RootLayout_Loaded;
        _layoutLoaded = true;
        App.Current.Services.Dialogs.AttachRoot(RootLayout);
        App.Current.Services.RunLock.AttachDispatcher(RootLayout.DispatcherQueue);
        App.Current.Services.UiDispatcher.Attach(RootLayout.DispatcherQueue);
        await App.Current.Services.InitializeAsync();
        App.Current.Services.Navigation.Navigate(typeof(HomePage));
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "mods" => typeof(ModsPage),
            "downloads" => typeof(DownloadsPage),
            "guide" => typeof(GuidePage),
            "saves" => typeof(SavesPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        App.Current.Services.Navigation.Navigate(pageType);
    }
}
