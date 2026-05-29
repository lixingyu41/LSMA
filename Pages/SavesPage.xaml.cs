using System.Collections.Generic;
using System.Linq;
using LSMA.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace LSMA.Pages;

public sealed partial class SavesPage : Page
{
    private const double HiddenOffset = 20;
    private const double MinDetailTopMargin = 30;
    private const double BubbleTopClearance = 8;
    private const double MaxDetailTopMargin = 160;
    private const double MaxDetailContentWidth = 1180;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(190));

    private sealed class BubbleHost
    {
        public required Grid Wrapper { get; init; }
        public required Border Card { get; init; }
        public required Border Bubble { get; init; }
        public required TextBlock Text { get; init; }
        public required TranslateTransform Offset { get; init; }
        public required string FallbackText { get; init; }
        public Storyboard? Animation { get; set; }
    }

    private readonly List<BubbleHost> _bubbleHosts = [];
    private BubbleHost? _activeBubble;

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
        DetailContent.SizeChanged += OnDetailContentSizeChanged;
        DetailScrollViewer.SizeChanged += OnDetailScrollViewerSizeChanged;
        UpdateResponsiveLayout();
        QueueDetailTopMarginUpdate();
    }

    private void OnDetailContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AttachCardBubbles();
        QueueDetailTopMarginUpdate();
    }

    private void OnDetailScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        QueueDetailTopMarginUpdate();
    }

    private void QueueDetailTopMarginUpdate()
    {
        DispatcherQueue.TryEnqueue(UpdateDetailTopMargin);
    }

    private void AttachCardBubbles()
    {
        foreach (var card in FindTooltipCards(DetailScrollViewer).ToArray())
        {
            if (_bubbleHosts.Any(host => host.Card == card))
            {
                continue;
            }

            var text = ToolTipService.GetToolTip(card) as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var host = TryCreateBubbleHost(card, text);
            if (host is null)
            {
                continue;
            }

            ToolTipService.SetToolTip(card, null);
            host.Wrapper.PointerEntered += (_, _) => ShowBubble(host);
            host.Wrapper.PointerExited += (_, _) => HideBubble(host);
            _bubbleHosts.Add(host);
        }
    }

    private void ShowBubble(BubbleHost host)
    {
        if (_activeBubble == host)
        {
            return;
        }

        if (_activeBubble is not null)
        {
            HideBubble(_activeBubble);
        }

        _activeBubble = host;
        host.Animation?.Stop();
        RefreshBubbleText(host);

        if (host.Bubble.Visibility != Visibility.Visible)
        {
            host.Offset.Y = HiddenOffset;
            host.Bubble.Opacity = 0;
        }

        if (!ArrangeBubble(host))
        {
            _activeBubble = null;
            return;
        }

        host.Animation = CreateAnimation(host, 0, 1, EasingMode.EaseOut);
        host.Animation.Completed += (_, _) =>
        {
            if (_activeBubble == host)
            {
                host.Animation = null;
            }
        };
        host.Animation.Begin();
    }

    private void HideBubble(BubbleHost host)
    {
        if (_activeBubble == host)
        {
            _activeBubble = null;
        }

        host.Animation?.Stop();
        host.Animation = CreateAnimation(host, HiddenOffset, 0, EasingMode.EaseIn);
        host.Animation.Completed += (_, _) =>
        {
            if (_activeBubble != host)
            {
                host.Bubble.Visibility = Visibility.Collapsed;
                host.Bubble.Opacity = 0;
                host.Offset.Y = HiddenOffset;
                host.Animation = null;
            }
        };
        host.Animation.Begin();
    }

    private static bool ArrangeBubble(BubbleHost host)
    {
        host.Wrapper.UpdateLayout();

        var cardWidth = host.Card.ActualWidth;
        var cardHeight = host.Card.ActualHeight;
        if (cardWidth <= 0 || cardHeight <= 0)
        {
            return false;
        }

        host.Bubble.Visibility = Visibility.Visible;
        host.Bubble.Width = cardWidth;
        host.Bubble.CornerRadius = host.Card.CornerRadius;
        host.Bubble.Padding = new Thickness(12, 8, 12, cardHeight / 2);
        host.Bubble.Measure(new Size(cardWidth, double.PositiveInfinity));

        Canvas.SetLeft(host.Bubble, 0);
        Canvas.SetTop(host.Bubble, cardHeight / 2 - host.Bubble.DesiredSize.Height);
        return true;
    }

    private void UpdateDetailTopMargin()
    {
        DetailContent.UpdateLayout();

        var topMargin = MinDetailTopMargin;
        foreach (var host in FindFirstRowHosts())
        {
            host.Wrapper.UpdateLayout();

            var cardWidth = host.Card.ActualWidth;
            var cardHeight = host.Card.ActualHeight;
            if (cardWidth <= 0 || cardHeight <= 0)
            {
                continue;
            }

            host.Bubble.Width = cardWidth;
            host.Bubble.CornerRadius = host.Card.CornerRadius;
            host.Bubble.Padding = new Thickness(12, 8, 12, cardHeight / 2);
            RefreshBubbleText(host);
            host.Bubble.Measure(new Size(cardWidth, double.PositiveInfinity));

            if (!TryGetTop(host, out var cardTop))
            {
                continue;
            }

            var bubbleAboveCard = host.Bubble.DesiredSize.Height - cardHeight / 2;
            var requiredExtra = Math.Max(0, bubbleAboveCard - cardTop + BubbleTopClearance);
            topMargin = Math.Max(topMargin, MinDetailTopMargin + requiredExtra);
        }

        topMargin = Math.Min(Math.Ceiling(topMargin), MaxDetailTopMargin);
        if (Math.Abs(DetailContent.Margin.Top - topMargin) < 1)
        {
            return;
        }

        DetailContent.Margin = new Thickness(
            DetailContent.Margin.Left,
            topMargin,
            DetailContent.Margin.Right,
            DetailContent.Margin.Bottom);
    }

    private IEnumerable<BubbleHost> FindFirstRowHosts()
    {
        var firstTop = double.PositiveInfinity;
        var positions = new List<(BubbleHost Host, double Top)>();

        foreach (var host in _bubbleHosts.Where(IsUsableBubbleHost))
        {
            if (!TryGetTop(host, out var top))
            {
                continue;
            }

            firstTop = Math.Min(firstTop, top);
            positions.Add((host, top));
        }

        foreach (var item in positions)
        {
            if (Math.Abs(item.Top - firstTop) < 1)
            {
                yield return item.Host;
            }
        }
    }

    private void UpdateResponsiveLayout()
    {
        var width = DetailScrollViewer.ActualWidth;
        if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width))
        {
            return;
        }

        var contentWidth = Math.Floor(Math.Min(width, MaxDetailContentWidth));
        if (contentWidth < 1)
        {
            return;
        }

        DetailContent.Width = contentWidth;
        DetailGrid.Width = contentWidth;

        var pageTwoColumns = contentWidth >= 940;
        SetAdaptiveGrid(PrimaryGrid, PrimaryInfoColumn, ProgressColumn, pageTwoColumns, 20);
        SetAdaptiveGrid(DetailGrid, DetailStatsColumn, FriendshipPanel, pageTwoColumns, 20);

        var detailWidth = Math.Min(contentWidth, DetailGrid.MaxWidth);
        var catchWidth = pageTwoColumns ? Math.Max(0, (detailWidth - DetailGrid.ColumnSpacing) / 2) : detailWidth;
        SetAdaptiveGrid(CatchGrid, FishPanel, MonsterPanel, pageTwoColumns && catchWidth >= 560, 16);
    }

    private static void SetAdaptiveGrid(
        Grid grid,
        FrameworkElement firstColumn,
        FrameworkElement secondColumn,
        bool twoColumns,
        double twoColumnSpacing)
    {
        if (grid.ColumnDefinitions.Count < 2 || grid.RowDefinitions.Count < 2)
        {
            return;
        }

        grid.ColumnSpacing = twoColumns ? twoColumnSpacing : 0;
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(firstColumn, 0);
        Grid.SetRow(firstColumn, 0);
        Grid.SetColumnSpan(firstColumn, twoColumns ? 1 : 2);
        Grid.SetColumn(secondColumn, twoColumns ? 1 : 0);
        Grid.SetRow(secondColumn, twoColumns ? 0 : 1);
        Grid.SetColumnSpan(secondColumn, twoColumns ? 1 : 2);
    }

    private static bool IsUsableBubbleHost(BubbleHost host)
        => host.Wrapper.IsLoaded
            && host.Wrapper.ActualWidth > 0
            && host.Wrapper.ActualHeight > 0
            && host.Card.IsLoaded;

    private bool TryGetTop(BubbleHost host, out double top)
    {
        try
        {
            top = host.Wrapper.TransformToVisual(DetailContent).TransformPoint(new Point(0, 0)).Y;
            return !double.IsNaN(top) && !double.IsInfinity(top);
        }
        catch
        {
            top = 0;
            return false;
        }
    }

    private static void RefreshBubbleText(BubbleHost host)
    {
        var text = host.Card.DataContext switch
        {
            SaveMetricInfo { Detail: { Length: > 0 } detail } => detail,
            SaveProgressInfo { Detail: { Length: > 0 } detail } => detail,
            _ => host.FallbackText
        };
        host.Text.Text = text;
    }

    private static BubbleHost? TryCreateBubbleHost(Border card, string text)
    {
        if (card.Parent is not Panel row)
        {
            return null;
        }

        var index = row.Children.IndexOf(card);
        var margin = card.Margin;
        var column = Grid.GetColumn(card);
        var columnSpan = Grid.GetColumnSpan(card);
        var gridRow = Grid.GetRow(card);
        var rowSpan = Grid.GetRowSpan(card);

        row.Children.RemoveAt(index);
        card.Margin = new Thickness(0);

        var offset = new TranslateTransform { Y = HiddenOffset };
        var bubbleText = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap
        };
        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = card.CornerRadius,
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransform = offset,
            Visibility = Visibility.Collapsed,
            Child = bubbleText
        };

        var bubbleLayer = new Canvas { IsHitTestVisible = false };
        bubbleLayer.Children.Add(bubble);
        Canvas.SetZIndex(bubbleLayer, 0);
        Canvas.SetZIndex(card, 1);

        var wrapper = new Grid { Margin = margin };
        wrapper.Children.Add(bubbleLayer);
        wrapper.Children.Add(card);

        Grid.SetColumn(wrapper, column);
        Grid.SetColumnSpan(wrapper, columnSpan);
        Grid.SetRow(wrapper, gridRow);
        Grid.SetRowSpan(wrapper, rowSpan);
        row.Children.Insert(index, wrapper);

        return new BubbleHost
        {
            Wrapper = wrapper,
            Card = card,
            Bubble = bubble,
            Text = bubbleText,
            Offset = offset,
            FallbackText = text
        };
    }

    private static Storyboard CreateAnimation(BubbleHost host, double targetOffset, double targetOpacity, EasingMode easingMode)
    {
        var easing = new CubicEase { EasingMode = easingMode };
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateDoubleAnimation(host.Offset, nameof(TranslateTransform.Y), targetOffset, easing));
        storyboard.Children.Add(CreateDoubleAnimation(host.Bubble, nameof(UIElement.Opacity), targetOpacity, easing));
        return storyboard;
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string property,
        double value,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = AnimationDuration,
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private static IEnumerable<Border> FindTooltipCards(DependencyObject root)
    {
        var cardStyle = Application.Current.Resources["CardStyle"] as Style;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var childCount = VisualTreeHelper.GetChildrenCount(current);

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                queue.Enqueue(child);

                if (child is Border border
                    && (border.Style == cardStyle || ToolTipService.GetToolTip(border) is string))
                {
                    yield return border;
                }
            }
        }
    }
}
