using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LSMA.Pages;

public sealed partial class SavesPage : Page
{
    private Canvas? _bubbleCanvas;
    private Border? _activeBubble;
    private Storyboard? _activeAnimation;

    public SavesPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Saves;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        AttachCardBubbles();
    }

    private void AttachCardBubbles()
    {
        // Create overlay canvas BELOW all content so card covers bubble
        _bubbleCanvas = new Canvas { IsHitTestVisible = false };
        var rootGrid = (Grid)Content;
        rootGrid.Children.Insert(0, _bubbleCanvas);

        foreach (var card in FindCardBorders(DetailScrollViewer))
        {
            var tooltipText = ToolTipService.GetToolTip(card) as string;
            if (string.IsNullOrWhiteSpace(tooltipText))
            {
                continue;
            }

            ToolTipService.SetToolTip(card, null);
            var text = tooltipText;

            card.PointerEntered += (_, _) =>
            {
                ShowBubble(card, text);
            };

            card.PointerExited += (_, _) =>
            {
                HideBubble();
            };
        }
    }

    private void ShowBubble(Border card, string text)
    {
        _activeAnimation?.Stop();

        if (_activeBubble is null)
        {
            _activeBubble = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Opacity = 0,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                    TextWrapping = TextWrapping.Wrap
                }
            };

            var transform = new TranslateTransform { Y = 20 };
            _activeBubble.RenderTransform = transform;
            _bubbleCanvas!.Children.Add(_activeBubble);
        }
        else
        {
            ((TextBlock)_activeBubble.Child).Text = text;
        }

        // Position: bottom edge at card's vertical center, above card
        // Bottom padding matches card top half so bubble extends behind the card seamlessly
        _activeBubble.Padding = new Thickness(12, 8, 12, 8 + card.ActualHeight / 2);
        _activeBubble.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var bubbleHeight = _activeBubble.DesiredSize.Height;

        var cardTransform = card.TransformToVisual(_bubbleCanvas);
        var cardPos = cardTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var cardCenterY = cardPos.Y + card.ActualHeight / 2;

        Canvas.SetLeft(_activeBubble, cardPos.X);
        Canvas.SetTop(_activeBubble, cardCenterY - bubbleHeight);
        Canvas.SetZIndex(_activeBubble, 0);
        _activeBubble.Width = card.ActualWidth;

        // Reset and animate
        ((TranslateTransform)_activeBubble.RenderTransform).Y = 20;
        _activeBubble.Opacity = 0;
        _activeBubble.Visibility = Visibility.Visible;

        AnimateBubble(true, (TranslateTransform)_activeBubble.RenderTransform);
    }

    private void HideBubble()
    {
        if (_activeBubble is null)
        {
            return;
        }

        var transform = (TranslateTransform)_activeBubble.RenderTransform;
        AnimateBubble(false, transform);
    }

    private void AnimateBubble(bool show, TranslateTransform transform)
    {
        _activeAnimation?.Stop();
        var bubble = _activeBubble!;

        var duration = new Duration(TimeSpan.FromMilliseconds(190));
        var easing = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };

        var move = new DoubleAnimation
        {
            To = show ? 0 : 20,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(move, transform);
        Storyboard.SetTargetProperty(move, "Y");

        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacity, bubble);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var animation = new Storyboard();
        animation.Children.Add(move);
        animation.Children.Add(opacity);
        if (!show)
        {
            animation.Completed += (_, _) => bubble.Visibility = Visibility.Collapsed;
        }

        _activeAnimation = animation;
        animation.Begin();
    }

    private static IEnumerable<Border> FindCardBorders(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border border && border.Style == Application.Current.Resources["CardStyle"] as Style)
            {
                yield return border;
            }

            foreach (var descendant in FindCardBorders(child))
            {
                yield return descendant;
            }
        }
    }
}
