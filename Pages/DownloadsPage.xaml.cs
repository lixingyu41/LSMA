using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using LSMA.Models;
using LSMA.ViewModels;
using Windows.System;

namespace LSMA.Pages;

public sealed partial class DownloadsPage : Page
{
    private ScrollViewer? _onlineModsScrollViewer;

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
