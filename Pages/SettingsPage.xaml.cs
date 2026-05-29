using System.ComponentModel;
using LSMA.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

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

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updateNoticeTimer.Stop();
        AnimateUpdateNotice(true);
        _updateNoticeTimer.Start();
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
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                CreateQrPlaceholder(),
                new TextBlock
                {
                    Text = "请给我钱",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
    }

    private static Border CreateQrPlaceholder()
    {
        var canvas = new Canvas
        {
            Width = 156,
            Height = 156
        };
        var black = new SolidColorBrush(Colors.Black);
        var white = new SolidColorBrush(Colors.White);

        AddFinderPattern(canvas, 0, 0, black, white);
        AddFinderPattern(canvas, 108, 0, black, white);
        AddFinderPattern(canvas, 0, 108, black, white);

        var modules = new (int X, int Y)[]
        {
            (5, 0), (6, 0), (7, 0), (5, 1), (7, 1), (4, 2), (6, 2), (8, 2),
            (5, 3), (7, 3), (4, 5), (5, 5), (7, 5), (8, 5), (10, 5), (12, 5),
            (4, 6), (6, 6), (9, 6), (11, 6), (5, 7), (7, 7), (8, 7), (10, 7),
            (12, 7), (4, 8), (6, 8), (9, 8), (11, 8), (5, 9), (7, 9), (8, 9),
            (10, 9), (12, 9), (5, 10), (6, 10), (11, 10), (4, 11), (7, 11),
            (9, 11), (12, 11), (5, 12), (8, 12), (10, 12), (11, 12)
        };

        foreach (var module in modules)
        {
            AddQrRectangle(canvas, module.X * 12, module.Y * 12, 12, 12, black);
        }

        return new Border
        {
            Width = 180,
            Height = 180,
            Padding = new Thickness(12),
            Background = white,
            BorderBrush = new SolidColorBrush(Colors.LightGray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas
        };
    }

    private static void AddFinderPattern(Canvas canvas, double left, double top, Brush black, Brush white)
    {
        AddQrRectangle(canvas, left, top, 48, 48, black);
        AddQrRectangle(canvas, left + 6, top + 6, 36, 36, white);
        AddQrRectangle(canvas, left + 14, top + 14, 20, 20, black);
    }

    private static void AddQrRectangle(Canvas canvas, double left, double top, double width, double height, Brush fill)
    {
        var rectangle = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = fill
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        canvas.Children.Add(rectangle);
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
