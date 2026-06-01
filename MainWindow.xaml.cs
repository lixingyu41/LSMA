using LSMA.Models;
using LSMA.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Windows.UI;

namespace LSMA;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan StartupModLoadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartupTimeoutNoticeDelay = TimeSpan.FromMilliseconds(900);
    private AppPalette _palette = AppPalette.Stardrop;
    private bool _layoutLoaded;
    private int _appearanceTransitionVersion;
    private Storyboard? _appearanceStoryboard;
    private TaskCompletionSource? _appearanceStoryboardCompletion;
    private bool _updatingNavigationSelection;
    private bool _launchOptionsVisible;
    private Storyboard? _launchOptionsStoryboard;
    private InputNonClientPointerSource? _nonClientPointerSource;

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
        App.Current.Services.PageAcceleration.AttachFrame(ContentFrame);
        ContentFrame.Navigated += ContentFrame_Navigated;
        RegisterHistoryInput();
        AppTitleBarDragRegion.SizeChanged += (_, _) => UpdateTitleBarPassthroughRegions();
        BrandTitleArea.SizeChanged += (_, _) => UpdateTitleBarPassthroughRegions();
        LaunchTitleArea.SizeChanged += (_, _) => UpdateTitleBarPassthroughRegions();
        LaunchGameButtonHost.SizeChanged += (_, _) => UpdateTitleBarPassthroughRegions();
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
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        UpdateTitleBarPassthroughRegions();
        var target = App.Current.Services.Settings.Current.DefaultLaunchTarget;
        UpdateLaunchToggle(target == LaunchTarget.Smapi);
        SteamCheckbox.IsChecked = App.Current.Services.Settings.Current.LaunchViaSteam;

        var settings = App.Current.Services.Settings.Current;
        if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
        {
            AppWindow.Resize(new SizeInt32(settings.WindowWidth, settings.WindowHeight));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(500, 380));
        }

        // Save on resize
        AppWindow.Changed += (_, _) =>
        {
            var size = AppWindow.Size;
            if (size.Width > 0 && size.Height > 0)
            {
                App.Current.Services.Settings.Current.WindowWidth = size.Width;
                App.Current.Services.Settings.Current.WindowHeight = size.Height;
            }
        };

        App.Current.Services.Dialogs.AttachRoot(RootLayout);
        App.Current.Services.RunLock.AttachDispatcher(RootLayout.DispatcherQueue);
        App.Current.Services.UiDispatcher.Attach(RootLayout.DispatcherQueue);
        var startupTask = App.Current.Services.InitializeForStartupAsync();
        var startupReady = await WaitForStartupAsync(startupTask);
        var handledActivation = await App.Current.TryHandlePendingActivationAsync();
        if (!handledActivation && ContentFrame.CurrentSourcePageType is null)
        {
            App.Current.Services.Navigation.Navigate(typeof(ModsPage));
        }

        if (startupReady)
        {
            HideStartupLoading();
        }
        else
        {
            _ = HideStartupLoadingAfterNoticeAsync();
        }

        _ = ContinueStartupInitializationAsync(startupTask);
    }

    private async Task<bool> WaitForStartupAsync(Task startupTask)
    {
        try
        {
            if (await Task.WhenAny(startupTask, Task.Delay(StartupModLoadTimeout)) == startupTask)
            {
                await startupTask;
                return true;
            }

            StartupStatusText.Text = "模组加载超过 15 秒，进入界面后继续刷新。";
            return false;
        }
        catch
        {
            StartupStatusText.Text = "模组加载失败，进入界面后可重试。";
            return true;
        }
    }

    private async Task ContinueStartupInitializationAsync(Task startupTask)
    {
        try
        {
            await startupTask;
            await App.Current.Services.InitializeDeferredAsync();
        }
        catch (Exception exception)
        {
            await App.Current.Services.Logging.ErrorAsync("启动初始化失败", exception);
            await App.Current.Services.Dialogs.ShowMessageAsync("启动初始化失败", exception.Message);
        }
    }

    private async Task HideStartupLoadingAfterNoticeAsync()
    {
        await Task.Delay(StartupTimeoutNoticeDelay);
        HideStartupLoading();
    }

    private void HideStartupLoading()
    {
        StartupLoadingOverlay.Visibility = Visibility.Collapsed;
        StartupLoadingOverlay.IsHitTestVisible = false;
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

    private void LaunchGameBtn_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _launchOptionsVisible = true;
        var target = App.Current.Services.Settings.Current.DefaultLaunchTarget;
        UpdateLaunchToggle(target == LaunchTarget.Smapi);
        SteamCheckbox.IsChecked = App.Current.Services.Settings.Current.LaunchViaSteam;
        ApplyLaunchOptionsVisual(true, true);
        UpdateTitleBarPassthroughRegions();
    }

    private void LaunchTitleArea_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _launchOptionsVisible = false;
        ApplyLaunchOptionsVisual(false, true);
        UpdateTitleBarPassthroughRegions();
    }

    private void ApplyLaunchOptionsVisual(bool visible, bool animate)
    {
        LaunchHoverSurface.Background = visible
            ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
            : null;
        LaunchTargetOptions.IsHitTestVisible = visible;
        SteamLaunchOption.IsHitTestVisible = visible;

        var scale = visible ? 2 : 1;
        var opacity = visible ? 1 : 0;
        var leftX = visible ? 0 : 150;
        var rightX = visible ? 0 : -150;
        if (!animate)
        {
            SetLaunchOptionsVisual(scale, opacity, leftX, rightX);
            return;
        }

        _launchOptionsStoryboard?.Stop();
        var duration = new Duration(TimeSpan.FromMilliseconds(visible ? 180 : 150));
        var easing = new CubicEase
        {
            EasingMode = visible ? EasingMode.EaseOut : EasingMode.EaseIn
        };
        var storyboard = new Storyboard();
        AddDoubleAnimation(storyboard, LaunchGameScale, "ScaleX", scale, duration, easing);
        AddDoubleAnimation(storyboard, LaunchGameScale, "ScaleY", scale, duration, easing);
        AddDoubleAnimation(storyboard, LaunchTargetOptions, "Opacity", opacity, duration, easing);
        AddDoubleAnimation(storyboard, SteamLaunchOption, "Opacity", opacity, duration, easing);
        AddDoubleAnimation(storyboard, LaunchTargetOptionsTranslate, "X", leftX, duration, easing);
        AddDoubleAnimation(storyboard, SteamLaunchOptionTranslate, "X", rightX, duration, easing);
        storyboard.Completed += (_, _) =>
        {
            SetLaunchOptionsVisual(scale, opacity, leftX, rightX);
            storyboard.Stop();
            if (ReferenceEquals(_launchOptionsStoryboard, storyboard))
            {
                _launchOptionsStoryboard = null;
            }
        };
        _launchOptionsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void SetLaunchOptionsVisual(double scale, double opacity, double leftX, double rightX)
    {
        LaunchGameScale.ScaleX = scale;
        LaunchGameScale.ScaleY = scale;
        LaunchTargetOptions.Opacity = opacity;
        SteamLaunchOption.Opacity = opacity;
        LaunchTargetOptionsTranslate.X = leftX;
        SteamLaunchOptionTranslate.X = rightX;
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double value,
        Duration duration,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
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
            await Launcher.LaunchUriAsync(new Uri("steam://rungameid/413150"));
        }
        else
        {
            await App.Current.Services.Home.LaunchGameCommand.ExecuteAsync(null);
        }
    }

    private void UpdateTitleBarPassthroughRegions()
    {
        if (_nonClientPointerSource is null || RootLayout.XamlRoot is null)
        {
            return;
        }

        var regions = new List<RectInt32>
        {
            GetElementRect(BrandTitleArea),
            GetElementRect(LaunchGameButtonHost)
        };
        if (_launchOptionsVisible)
        {
            regions.Add(GetElementRect(LaunchTitleArea));
        }

        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.Passthrough,
            [.. regions]);
    }

    private static RectInt32 GetElementRect(FrameworkElement element)
    {
        var scale = element.XamlRoot.RasterizationScale;
        var bounds = element.TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    private void RegisterHistoryInput()
    {
        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootLayout_KeyDown), true);
        RootLayout.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootLayout_PointerPressed), true);
    }

    private void RootLayout_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsAltPressed())
        {
            return;
        }

        var handled = e.Key switch
        {
            VirtualKey.Left => App.Current.Services.Navigation.GoBack(),
            VirtualKey.Right => App.Current.Services.Navigation.GoForward(),
            _ => false
        };
        if (handled)
        {
            e.Handled = true;
        }
    }

    private static bool IsAltPressed()
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private void RootLayout_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var updateKind = e.GetCurrentPoint(RootLayout).Properties.PointerUpdateKind;
        var handled = updateKind switch
        {
            PointerUpdateKind.XButton1Pressed => App.Current.Services.Navigation.GoBack(),
            PointerUpdateKind.XButton2Pressed => App.Current.Services.Navigation.GoForward(),
            _ => false
        };
        if (handled)
        {
            e.Handled = true;
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        SelectNavigationItem(e.SourcePageType);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_updatingNavigationSelection)
        {
            return;
        }

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
            _ => typeof(ModsPage)
        };

        var parameter = pageType == typeof(GuidePage) ? string.Empty : null;
        App.Current.Services.Navigation.Navigate(pageType, parameter);
    }

    private void SelectNavigationItem(Type pageType)
    {
        var tag = pageType == typeof(ModsPage)
            ? "mods"
            : pageType == typeof(DownloadsPage)
                ? "downloads"
                : pageType == typeof(GuidePage)
                    ? "guide"
                    : pageType == typeof(SavesPage)
                        ? "saves"
                        : pageType == typeof(SettingsPage)
                            ? "settings"
                            : "mods";

        var target = ShellNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == tag);
        if (target is null || ReferenceEquals(ShellNavigation.SelectedItem, target))
        {
            return;
        }

        try
        {
            _updatingNavigationSelection = true;
            ShellNavigation.SelectedItem = target;
        }
        finally
        {
            _updatingNavigationSelection = false;
        }
    }
}
