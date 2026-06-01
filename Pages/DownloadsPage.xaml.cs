using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using LSMA.Models;
using LSMA.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace LSMA.Pages;

public sealed partial class DownloadsPage : Page
{
    private const double MinCoverPreviewScale = 0.5;
    private const double MaxCoverPreviewScale = 6.0;
    private const double DownloadsListMinWidth = 360;
    private const double DownloadsDetailMinWidth = 320;
    private ScrollViewer? _onlineModsScrollViewer;
    private double _onlineCoverPreviewScale = 1;
    private bool _onlineCoverPreviewDragging;
    private bool _onlineCoverPreviewDragged;
    private Point _onlineCoverPreviewLastPoint;
    private bool _downloadsSplitterDragging;
    private double _downloadsSplitterStartX;
    private double _downloadsListStartWidth;

    public DownloadsPage()
    {
        InitializeComponent();
        DataContext = App.Current.Services.Downloads;
        Loaded += async (_, _) => await App.Current.Services.Downloads.StartAsync();
    }

    private void OnlineSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || DataContext is not DownloadsViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        _ = viewModel.SearchOnlineCommand.ExecuteAsync(null);
    }

    private void OnlineModsList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_onlineModsScrollViewer is not null)
        {
            return;
        }

        _onlineModsScrollViewer = FindDescendant<ScrollViewer>(OnlineModsList);
        if (_onlineModsScrollViewer is not null)
        {
            _onlineModsScrollViewer.ViewChanged += OnlineModsScrollViewer_ViewChanged;
        }
    }

    private void OnlineModsScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || DataContext is not DownloadsViewModel viewModel
            || scrollViewer.ScrollableHeight <= 0
            || scrollViewer.VerticalOffset + scrollViewer.ViewportHeight < scrollViewer.ExtentHeight - 180)
        {
            return;
        }

        if (viewModel.LoadMoreCommand.CanExecute(null))
        {
            _ = viewModel.LoadMoreCommand.ExecuteAsync(null);
        }
    }

    private void OnlineModListItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NexusModInfo mod })
        {
            mod.IsPointerOver = true;
        }
    }

    private void OnlineModListItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NexusModInfo mod })
        {
            mod.IsPointerOver = false;
        }
    }

    private void OnlineCover_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: NexusModInfo mod }
            || DataContext is not DownloadsViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedOnlineMod = mod;
        if (!string.IsNullOrWhiteSpace(mod.CoverImageUri))
        {
            ShowOnlineCoverPreview(mod.CoverImageUri);
        }

        e.Handled = true;
    }

    private void OnlineFilesComboBox_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not DownloadsViewModel viewModel
            || !viewModel.LoadFilesCommand.CanExecute(null))
        {
            return;
        }

        _ = viewModel.LoadFilesCommand.ExecuteAsync(null);
    }

    private void ShowOnlineCoverPreview(string uri)
    {
        OnlineCoverPreviewImage.Source = new BitmapImage(new Uri(uri));
        ResetOnlineCoverPreviewTransform();
        OnlineCoverPreviewOverlay.Visibility = Visibility.Visible;
        OnlineCoverPreviewOverlay.Focus(FocusState.Programmatic);
    }

    private void HideOnlineCoverPreview()
    {
        OnlineCoverPreviewOverlay.Visibility = Visibility.Collapsed;
        OnlineCoverPreviewImage.Source = null;
        _onlineCoverPreviewDragging = false;
        _onlineCoverPreviewDragged = false;
    }

    private void ResetOnlineCoverPreviewTransform()
    {
        _onlineCoverPreviewScale = 1;
        OnlineCoverPreviewTransform.ScaleX = 1;
        OnlineCoverPreviewTransform.ScaleY = 1;
        OnlineCoverPreviewTransform.TranslateX = 0;
        OnlineCoverPreviewTransform.TranslateY = 0;
    }

    private void OnlineCoverPreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HideOnlineCoverPreview();
            e.Handled = true;
        }
    }

    private void OnlineCoverPreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideOnlineCoverPreview();
        e.Handled = true;
    }

    private void OnlineCoverPreviewImage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(OnlineCoverPreviewImage).Properties.MouseWheelDelta;
        var factor = delta > 0 ? 1.1 : 1 / 1.1;
        _onlineCoverPreviewScale = Math.Clamp(_onlineCoverPreviewScale * factor, MinCoverPreviewScale, MaxCoverPreviewScale);
        OnlineCoverPreviewTransform.ScaleX = _onlineCoverPreviewScale;
        OnlineCoverPreviewTransform.ScaleY = _onlineCoverPreviewScale;
        e.Handled = true;
    }

    private void OnlineCoverPreviewImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _onlineCoverPreviewDragging = true;
        _onlineCoverPreviewDragged = false;
        _onlineCoverPreviewLastPoint = e.GetCurrentPoint(OnlineCoverPreviewOverlay).Position;
        OnlineCoverPreviewImage.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnlineCoverPreviewImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_onlineCoverPreviewDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(OnlineCoverPreviewOverlay).Position;
        var dx = point.X - _onlineCoverPreviewLastPoint.X;
        var dy = point.Y - _onlineCoverPreviewLastPoint.Y;
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
        {
            _onlineCoverPreviewDragged = true;
        }

        OnlineCoverPreviewTransform.TranslateX += dx;
        OnlineCoverPreviewTransform.TranslateY += dy;
        _onlineCoverPreviewLastPoint = point;
        e.Handled = true;
    }

    private void OnlineCoverPreviewImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _onlineCoverPreviewDragging = false;
        OnlineCoverPreviewImage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnlineCoverPreviewImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_onlineCoverPreviewDragged)
        {
            HideOnlineCoverPreview();
        }

        _onlineCoverPreviewDragged = false;
        e.Handled = true;
    }

    private void DownloadsSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _downloadsSplitterDragging = true;
        _downloadsSplitterStartX = e.GetCurrentPoint(DownloadsSplitGrid).Position.X;
        _downloadsListStartWidth = DownloadsListColumn.ActualWidth;
        DownloadsSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DownloadsSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_downloadsSplitterDragging)
        {
            return;
        }

        var pointerX = e.GetCurrentPoint(DownloadsSplitGrid).Position.X;
        var deltaX = pointerX - _downloadsSplitterStartX;
        var maxListWidth = Math.Max(
            DownloadsListMinWidth,
            DownloadsSplitGrid.ActualWidth - DownloadsSplitter.ActualWidth - DownloadsDetailMinWidth);
        var listWidth = Math.Clamp(_downloadsListStartWidth + deltaX, DownloadsListMinWidth, maxListWidth);
        DownloadsListColumn.Width = new GridLength(listWidth);
        DownloadsDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        e.Handled = true;
    }

    private void DownloadsSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _downloadsSplitterDragging = false;
        DownloadsSplitter.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
