using System.Collections.Generic;
using System.Linq;
using LSMA.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace LSMA.Pages;

public sealed partial class SavesPage : Page
{
    private const double HiddenOffset = 20;
    private const double MinDetailTopMargin = 2;
    private const int MaxSectionColumns = 4;
    private const double MinSectionColumnWidth = 320;
    private const double MaxSectionColumnWidth = 380;
    private const double SectionColumnSpacing = 20;
    private const int InactiveBubbleZIndex = 1;
    private const int InactiveSnapshotZIndex = 2;
    private const int ActiveBubbleZIndex = 3;
    private const int ActiveSnapshotZIndex = 4;
    private const double CollapsedCatchPanelHeight = 360;
    private const double CollapsedFriendshipPanelHeight = 430;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(190));

    private sealed class BubbleHost
    {
        public required Grid Wrapper { get; init; }
        public required Border Card { get; init; }
        public required Border Bubble { get; init; }
        public required Image CardSnapshot { get; init; }
        public required TextBlock Text { get; init; }
        public required TranslateTransform Offset { get; init; }
        public required string FallbackText { get; init; }
        public Storyboard? Animation { get; set; }
    }

    private readonly List<BubbleHost> _bubbleHosts = [];
    private BubbleHost? _activeBubble;
    private int _sectionColumnCount;
    private string _sectionLayoutSignature = string.Empty;
    private bool _responsiveLayoutQueued;
    private bool _isFishPanelExpanded;
    private bool _isMonsterPanelExpanded;
    private bool _isFriendshipPanelExpanded;

    public SavesPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Saves;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        QueueBubbleRefresh();
        DetailContent.SizeChanged += OnDetailContentSizeChanged;
        DetailScrollViewer.SizeChanged += OnDetailScrollViewerSizeChanged;
        DetailScrollViewer.ViewChanged += OnDetailScrollViewerViewChanged;
        DetailScrollViewer.PointerEntered += (_, _) => QueueBubbleRefresh();
        SizeChanged += OnPageSizeChanged;
        FishPanel.SizeChanged += OnExpandablePanelSizeChanged;
        MonsterPanel.SizeChanged += OnExpandablePanelSizeChanged;
        FriendshipPanel.SizeChanged += OnExpandablePanelSizeChanged;
        ApplyExpandablePanelStates();
        UpdateResponsiveLayout();
        QueueResponsiveLayoutUpdate();
        QueueDetailTopMarginUpdate();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressDetailCardSize();
    }

    private void OnDetailContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueResponsiveLayoutUpdate();
        QueueTopColumnStretch(_sectionColumnCount);
        QueueBubbleRefresh();
        QueueDetailTopMarginUpdate();
    }

    private void OnDetailScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        if (_activeBubble is { } active)
        {
            ArrangeBubble(active);
        }

        QueueDetailTopMarginUpdate();
    }

    private void OnDetailScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_activeBubble is { } active)
        {
            ArrangeBubble(active);
        }
    }

    private void QueueDetailTopMarginUpdate()
    {
        DispatcherQueue.TryEnqueue(UpdateDetailTopMargin);
    }

    private void QueueResponsiveLayoutUpdate()
    {
        if (_responsiveLayoutQueued)
        {
            return;
        }

        _responsiveLayoutQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _responsiveLayoutQueued = false;
            UpdateResponsiveLayout();
        });
    }

    private void QueueBubbleRefresh()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AttachCardBubbles();
            DispatcherQueue.TryEnqueue(AttachCardBubbles);
        });
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

    private async void ShowBubble(BubbleHost host)
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
        Canvas.SetZIndex(host.Bubble, ActiveBubbleZIndex);
        Canvas.SetZIndex(host.CardSnapshot, ActiveSnapshotZIndex);
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

        await UpdateCardSnapshotAsync(host);
        if (_activeBubble != host)
        {
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
        Canvas.SetZIndex(host.Bubble, InactiveBubbleZIndex);
        Canvas.SetZIndex(host.CardSnapshot, InactiveSnapshotZIndex);
        host.Animation = CreateAnimation(host, HiddenOffset, 0, EasingMode.EaseIn);
        host.Animation.Completed += (_, _) =>
        {
            if (_activeBubble != host)
            {
                host.Bubble.Visibility = Visibility.Collapsed;
                host.CardSnapshot.Visibility = Visibility.Collapsed;
                host.CardSnapshot.Opacity = 0;
                host.Bubble.Opacity = 0;
                host.Offset.Y = HiddenOffset;
                Canvas.SetZIndex(host.Bubble, InactiveBubbleZIndex);
                Canvas.SetZIndex(host.CardSnapshot, InactiveSnapshotZIndex);
                host.Animation = null;
            }
        };
        host.Animation.Begin();
    }

    private bool ArrangeBubble(BubbleHost host)
    {
        host.Wrapper.UpdateLayout();
        BubbleOverlay.UpdateLayout();

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

        var origin = host.Card.TransformToVisual(BubbleOverlay).TransformPoint(new Point(0, 0));
        Canvas.SetLeft(host.Bubble, origin.X);
        Canvas.SetTop(host.Bubble, origin.Y + cardHeight / 2 - host.Bubble.DesiredSize.Height);
        host.CardSnapshot.Width = cardWidth;
        host.CardSnapshot.Height = cardHeight;
        host.CardSnapshot.Visibility = Visibility.Visible;
        host.CardSnapshot.Opacity = 1;
        Canvas.SetLeft(host.CardSnapshot, origin.X);
        Canvas.SetTop(host.CardSnapshot, origin.Y);
        return true;
    }

    private static async Task UpdateCardSnapshotAsync(BubbleHost host)
    {
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(host.Card);
            host.CardSnapshot.Source = bitmap;
        }
        catch
        {
            host.CardSnapshot.Source = null;
        }
    }

    private void UpdateDetailTopMargin()
    {
        if (Math.Abs(DetailContent.Margin.Top - MinDetailTopMargin) < 1)
        {
            return;
        }

        DetailContent.Margin = new Thickness(
            DetailContent.Margin.Left,
            MinDetailTopMargin,
            DetailContent.Margin.Right,
            DetailContent.Margin.Bottom);
    }

    private void UpdateResponsiveLayout()
    {
        var width = DetailScrollViewer.ActualWidth;
        if (width <= 0 || double.IsNaN(width) || double.IsInfinity(width))
        {
            return;
        }

        var sectionColumns = CalculateSectionColumnCount(width);
        var contentWidth = Math.Floor(CalculateContentWidth(width, sectionColumns));
        if (contentWidth < 1)
        {
            return;
        }

        HeaderBar.Width = contentWidth;
        DetailContent.Width = contentWidth;
        SectionGrid.Width = contentWidth;
        SetSectionGrid(sectionColumns);
    }

    private static int CalculateSectionColumnCount(double availableWidth)
    {
        for (var columns = MaxSectionColumns; columns > 1; columns--)
        {
            var requiredWidth = columns * MinSectionColumnWidth + (columns - 1) * SectionColumnSpacing;
            if (availableWidth >= requiredWidth)
            {
                return columns;
            }
        }

        return 1;
    }

    private static double CalculateContentWidth(double availableWidth, int columns)
    {
        if (columns < MaxSectionColumns)
        {
            return availableWidth;
        }

        var maxWidth = MaxSectionColumns * MaxSectionColumnWidth + (MaxSectionColumns - 1) * SectionColumnSpacing;
        return Math.Min(availableWidth, maxWidth);
    }

    private void SetSectionGrid(int columns)
    {
        SectionGrid.ColumnSpacing = columns == MaxSectionColumns ? SectionColumnSpacing : 0;
        var columnPanels = new[] { SectionColumn0, SectionColumn1, SectionColumn2, SectionColumn3 };
        for (var index = 0; index < SectionGrid.ColumnDefinitions.Count; index++)
        {
            SectionGrid.ColumnDefinitions[index].Width = index < columns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            columnPanels[index].Visibility = index < columns ? Visibility.Visible : Visibility.Collapsed;
        }
        ApplySectionColumnMargins(columnPanels, columns);

        var sections = SectionPanels();
        var signature = columns.ToString();
        if (_sectionColumnCount == columns && _sectionLayoutSignature == signature)
        {
            return;
        }

        var columnCountChanged = _sectionColumnCount != columns;
        _sectionColumnCount = columns;
        _sectionLayoutSignature = signature;
        if (columnCountChanged)
        {
            _isFishPanelExpanded = columns > 1;
            _isMonsterPanelExpanded = columns > 1;
            ApplyExpandablePanelStates();
        }

        RemoveSectionPanels(sections);
        ResetTopColumnStretch();

        if (columns == 1)
        {
            AddSections(SectionColumn0, FarmSummaryPanel, SkillsPanel, PerfectionPanel, CollectionPanel, ActivityPanel, FriendshipPanel, FishPanel, MonsterPanel);
            return;
        }

        if (columns == 2)
        {
            AddSections(SectionColumn0, FarmSummaryPanel, SkillsPanel, ActivityPanel, FishPanel);
            AddSections(SectionColumn1, PerfectionPanel, CollectionPanel, FriendshipPanel, MonsterPanel);
            return;
        }

        AddSections(SectionColumn0, FarmSummaryPanel, SkillsPanel);
        if (columns == 3)
        {
            AddSections(SectionColumn1, ActivityPanel, FriendshipPanel);
            AddSections(SectionColumn2, PerfectionPanel, CollectionPanel);
            AddGridSection(FishPanel, 0, 1, 3);
            AddGridSection(MonsterPanel, 0, 2, 3);
            QueueTopColumnStretch(columns);
            return;
        }

        AddSections(SectionColumn1, ActivityPanel, CollectionPanel);
        AddSections(SectionColumn2, PerfectionPanel);
        AddSections(SectionColumn3, FriendshipPanel);
        AddGridSection(FishPanel, 0, 1, 2);
        AddGridSection(MonsterPanel, 2, 1, 2);
        QueueTopColumnStretch(columns);
    }

    private FrameworkElement[] SectionPanels()
    {
        return
        [
            FarmSummaryPanel,
            SkillsPanel,
            PerfectionPanel,
            CollectionPanel,
            ActivityPanel,
            FishPanel,
            MonsterPanel,
            FriendshipPanel
        ];
    }

    private static void RemoveSectionPanels(IEnumerable<FrameworkElement> sections)
    {
        foreach (var section in sections)
        {
            DetachFromParent(section);
        }
    }

    private static void AddSections(StackPanel column, params FrameworkElement[] sections)
    {
        foreach (var section in sections)
        {
            DetachFromParent(section);
            column.Children.Add(section);
        }
    }

    private static void ApplySectionColumnMargins(IReadOnlyList<StackPanel> columnPanels, int columns)
    {
        for (var index = 0; index < columnPanels.Count; index++)
        {
            columnPanels[index].Margin = columns is > 1 and < MaxSectionColumns && index < columns - 1
                ? new Thickness(0, 0, SectionColumnSpacing, 0)
                : new Thickness(0);
        }
    }

    private void AddGridSection(FrameworkElement section, int column, int row, int columnSpan)
    {
        DetachFromParent(section);
        SectionGrid.Children.Add(section);
        Grid.SetColumn(section, column);
        Grid.SetRow(section, row);
        Grid.SetColumnSpan(section, columnSpan);
    }

    private static void DetachFromParent(FrameworkElement section)
    {
        if (section.Parent is Panel parent)
        {
            parent.Children.Remove(section);
        }
    }

    private void QueueTopColumnStretch(int columns)
    {
        DispatcherQueue.TryEnqueue(() => StretchTopColumns(columns));
    }

    private void StretchTopColumns(int columns)
    {
        if (columns < 3)
        {
            return;
        }

        var columnPanels = new[] { SectionColumn0, SectionColumn1, SectionColumn2, SectionColumn3 }
            .Take(columns)
            .ToArray();
        ResetTopColumnStretch();

        var targetHeight = columnPanels.Max(column => column.ActualHeight);
        foreach (var column in columnPanels)
        {
            if (column.Children.LastOrDefault() is not FrameworkElement last)
            {
                continue;
            }

            var extra = targetHeight - column.ActualHeight;
            if (extra > 1 && last.ActualHeight > 0)
            {
                last.MinHeight = last.ActualHeight + extra;
            }
        }
    }

    private void ResetTopColumnStretch()
    {
        foreach (var panel in new FrameworkElement[] { SkillsPanel, CollectionPanel, PerfectionPanel, FriendshipPanel })
        {
            panel.MinHeight = 0;
        }
    }

    private void FishToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isFishPanelExpanded = !_isFishPanelExpanded;
        ApplyExpandablePanelStates();
    }

    private void MonsterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isMonsterPanelExpanded = !_isMonsterPanelExpanded;
        ApplyExpandablePanelStates();
    }

    private void FriendshipToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isFriendshipPanelExpanded = !_isFriendshipPanelExpanded;
        ApplyExpandablePanelStates();
    }

    private void ApplyExpandablePanelStates()
    {
        SetExpandablePanel(FishPanel, FishExpandIcon, _isFishPanelExpanded, CollapsedCatchPanelHeight);
        SetExpandablePanel(MonsterPanel, MonsterExpandIcon, _isMonsterPanelExpanded, CollapsedCatchPanelHeight);
        SetExpandablePanel(FriendshipPanel, FriendshipExpandIcon, _isFriendshipPanelExpanded, CollapsedFriendshipPanelHeight);
        QueueTopColumnStretch(_sectionColumnCount);

        if (_activeBubble is { } active)
        {
            HideBubble(active);
        }
    }

    private static void SetExpandablePanel(Border panel, FontIcon icon, bool isExpanded, double collapsedHeight)
    {
        panel.Height = isExpanded ? double.NaN : collapsedHeight;
        icon.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        UpdatePanelClip(panel, isExpanded);
    }

    private void OnExpandablePanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(sender, FishPanel))
        {
            UpdatePanelClip(FishPanel, _isFishPanelExpanded);
            return;
        }

        if (ReferenceEquals(sender, MonsterPanel))
        {
            UpdatePanelClip(MonsterPanel, _isMonsterPanelExpanded);
            return;
        }

        UpdatePanelClip(FriendshipPanel, _isFriendshipPanelExpanded);
    }

    private static void UpdatePanelClip(FrameworkElement panel, bool isExpanded)
    {
        panel.Clip = isExpanded
            ? null
            : new RectangleGeometry
            {
                Rect = new Rect(0, 0, Math.Max(0, panel.ActualWidth), Math.Max(0, panel.ActualHeight))
            };
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

    private void CollectionTile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SaveProgressInfo progress }
            || App.Current.Services.Saves.SelectedSave is not { } save)
        {
            return;
        }

        var key = progress.DetailKey ?? progress.CollectionKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var items = ResolveProgressDetailItems(save, key);
        if (items.Count == 0)
        {
            return;
        }

        e.Handled = true;
        if (_activeBubble is { } active)
        {
            HideBubble(active);
        }

        ShowProgressDetailCard(progress.Name, items);
    }

    private static IReadOnlyList<SaveCollectionItemInfo> ResolveProgressDetailItems(SaveInfo save, string key)
    {
        if (save.ProgressDetailItems.TryGetValue(key, out var progressItems))
        {
            return progressItems;
        }

        return save.CollectionItems.TryGetValue(key, out var collectionItems)
            ? collectionItems
            : [];
    }

    private void ShowProgressDetailCard(string title, IReadOnlyList<SaveCollectionItemInfo> items)
    {
        ProgressDetailTitle.Text = title;
        ProgressDetailSummary.Text = $"{items.Count(item => item.IsCollected):N0}/{items.Count:N0} 已完成 · 点击条目打开攻略搜索";
        ProgressDetailItems.ItemsSource = items;
        UpdateProgressDetailCardSize();
        ProgressDetailOverlay.Visibility = Visibility.Visible;
    }

    private void UpdateProgressDetailCardSize()
    {
        var rootWidth = XamlRoot?.Size.Width ?? ActualWidth;
        var rootHeight = XamlRoot?.Size.Height ?? ActualHeight;
        if (rootWidth <= 0 || rootHeight <= 0)
        {
            return;
        }

        ProgressDetailCard.Width = Math.Floor(rootWidth * 0.8);
        ProgressDetailCard.Height = Math.Floor(rootHeight * 0.8);
    }

    private void ProgressDetailItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SaveCollectionItemInfo item })
        {
            return;
        }

        e.Handled = true;
        var query = item.EffectiveSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        HideProgressDetailCard();
        App.Current.Services.Navigation.Navigate(typeof(GuidePage), query);
    }

    private void CloseProgressDetail_Click(object sender, RoutedEventArgs e)
    {
        HideProgressDetailCard();
    }

    private void ProgressDetailOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideProgressDetailCard();
    }

    private void ProgressDetailCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void HideProgressDetailCard()
    {
        ProgressDetailOverlay.Visibility = Visibility.Collapsed;
        ProgressDetailItems.ItemsSource = null;
    }

    private BubbleHost? TryCreateBubbleHost(Border card, string text)
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
        var cardSnapshot = new Image
        {
            IsHitTestVisible = false,
            Opacity = 0,
            Stretch = Stretch.Fill,
            Visibility = Visibility.Collapsed
        };

        BubbleOverlay.Children.Add(bubble);
        BubbleOverlay.Children.Add(cardSnapshot);
        Canvas.SetZIndex(bubble, InactiveBubbleZIndex);
        Canvas.SetZIndex(cardSnapshot, InactiveSnapshotZIndex);

        var wrapper = new Grid { Margin = margin };
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
            CardSnapshot = cardSnapshot,
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
