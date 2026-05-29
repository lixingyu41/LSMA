using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LSMA.Pages;

public sealed partial class SavesPage : Page
{
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
        foreach (var card in FindCardBorders(DetailScrollViewer))
        {
            var tooltipText = ToolTipService.GetToolTip(card) as string;
            if (string.IsNullOrWhiteSpace(tooltipText))
            {
                continue;
            }

            ToolTipService.SetToolTip(card, null);
            WrapCardWithBubble(card, tooltipText);
        }
    }

    private void WrapCardWithBubble(Border card, string text)
    {
        var parent = (Panel)card.Parent;
        var index = parent.Children.IndexOf(card);

        var column = Grid.GetColumn(card);
        var columnSpan = Grid.GetColumnSpan(card);
        var row = Grid.GetRow(card);
        var rowSpan = Grid.GetRowSpan(card);
        var margin = card.Margin;

        parent.Children.RemoveAt(index);
        card.Margin = new Thickness(0);

        var transform = new TranslateTransform { Y = 20 };
        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = transform,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                TextWrapping = TextWrapping.Wrap
            }
        };

        var wrapper = new Grid { Margin = margin };
        // Canvas keeps bubble out of layout flow
        var bubbleCanvas = new Canvas { IsHitTestVisible = false };
        bubbleCanvas.Children.Add(bubble);
        Canvas.SetLeft(bubble, 0);
        wrapper.Children.Add(bubbleCanvas);
        // Card on top of canvas
        wrapper.Children.Add(card);

        Grid.SetColumn(wrapper, column);
        Grid.SetColumnSpan(wrapper, columnSpan);
        Grid.SetRow(wrapper, row);
        Grid.SetRowSpan(wrapper, rowSpan);
        parent.Children.Insert(index, wrapper);

        Storyboard? activeAnimation = null;
        bool pinned = false;
        bool isOver = false;

        wrapper.PointerEntered += (_, _) =>
        {
            isOver = true;
            activeAnimation?.Stop();

            // Force layout to ensure dimensions are current
            wrapper.UpdateLayout();

            var cardWidth = card.ActualWidth;
            var cardHeight = card.ActualHeight;

            // Adjust bottom padding to cover card's top half
            bubble.Width = cardWidth;
            bubble.Padding = new Thickness(12, 8, 12, 8 + cardHeight / 2);
            bubble.Measure(new Windows.Foundation.Size(cardWidth, double.PositiveInfinity));
            var bubbleHeight = bubble.DesiredSize.Height;

            // Bottom edge of bubble at card vertical center
            Canvas.SetTop(bubble, cardHeight / 2 - bubbleHeight);

            // Animate
            transform.Y = 20;
            bubble.Opacity = 0;
            bubble.Visibility = Visibility.Visible;
            AnimateBubble(bubble, transform, true, ref activeAnimation);
        };

        wrapper.PointerExited += (_, _) =>
        {
            isOver = false;
            if (pinned) return;

            // Delay hide slightly — if PointerEntered fires again (child→child move),
            // isOver will be set back to true before this runs.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!isOver && !pinned)
                {
                    AnimateBubble(bubble, transform, false, ref activeAnimation);
                }
            });
        };

        wrapper.Tapped += (_, _) =>
        {
            pinned = !pinned;
        };
    }

    private static void AnimateBubble(Border bubble, TranslateTransform transform, bool show, ref Storyboard? activeAnimation)
    {
        activeAnimation?.Stop();

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

        activeAnimation = animation;
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
