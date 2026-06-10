using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using LSMA.Models;
using LSMA.ViewModels;

namespace LSMA.Pages;

public sealed partial class ModsPage : Page
{
    private const double MinCoverPreviewScale = 0.5;
    private const double MaxCoverPreviewScale = 6.0;
    private const double ModsListMinWidth = 360;
    private const double ModsDetailMinWidth = 360;
    private static readonly Dictionary<string, int> FilterIndexMap = new()
    {
        ["全部"] = 0, ["可更新"] = 1,
        ["有问题"] = 2, ["已禁用"] = 3, ["收藏"] = 4,
    };

    private ModsViewModel _vm = null!;
    private Button[] _filterButtons = null!;
    private int _selectedFilterIndex = 0;
    private Storyboard? _currentAnimation;
    private bool _filterEventsAttached;
    private double _modCoverPreviewScale = 1;
    private bool _modCoverPreviewDragging;
    private bool _modCoverPreviewDragged;
    private Point _modCoverPreviewLastPoint;
    private bool _modsSplitterDragging;
    private double _modsSplitterStartX;
    private double _modsListStartWidth;
    private bool _modsSplitterLayoutApplied;
    private static SolidColorBrush SelectedFilterForeground =>
        (SolidColorBrush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];

    public ModsPage()
    {
        InitializeComponent();
        _vm = App.Current.Services.Mods;
        DataContext = _vm;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_filterEventsAttached)
        {
            _filterButtons = [AllFilterButton, UpdateFilterButton,
                               ProblemFilterButton, DisabledFilterButton, FavoriteFilterButton];
            foreach (var btn in _filterButtons)
            {
                btn.PointerEntered += FilterButton_PointerEntered;
                btn.PointerExited += FilterButton_PointerExited;
            }

            FilterContainer.SizeChanged += OnFilterContainerSizeChanged;
            _filterEventsAttached = true;
        }

        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        ModsSplitGrid.SizeChanged -= ModsSplitGrid_SizeChanged;
        ModsSplitGrid.SizeChanged += ModsSplitGrid_SizeChanged;
        ApplySavedModsSplitterRatio();
        UpdateFilterSelection(_vm.CurrentFilter);
        UpdateProblemCountColor();
        _vm.StartDailyUpdateCheckIfNeeded();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        ModsSplitGrid.SizeChanged -= ModsSplitGrid_SizeChanged;
    }

    private void OnFilterContainerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_filterButtons is null || _currentAnimation?.GetCurrentState() == ClockState.Active)
            return;

        if (FilterIndexMap.TryGetValue(_vm.CurrentFilter, out var index))
        {
            PositionPillAtColumn(index, animate: false);
            UpdateButtonColors(index);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModsViewModel.CurrentFilter))
        {
            UpdateFilterSelection(_vm.CurrentFilter);
        }
        else if (e.PropertyName == nameof(ModsViewModel.HasProblems))
        {
            UpdateProblemCountColor();
        }
    }

    private void UpdateFilterSelection(string currentFilter)
    {
        if (!FilterIndexMap.TryGetValue(currentFilter, out var index))
            return;

        _selectedFilterIndex = index;
        PositionPillAtColumn(index, animate: true);
        UpdateButtonColors(index);
    }

    private void FilterButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button btn && FilterIndexMap.TryGetValue(_vm.CurrentFilter, out var idx)
            && _filterButtons[idx] == btn)
        {
            // Selected button hovered — keep Normal state (no hover tint on yellow pill)
            VisualStateManager.GoToState(btn, "Normal", false);
        }
    }

    private void FilterButton_PointerExited(object sender, PointerRoutedEventArgs e) { }

    private void UpdateButtonColors(int selectedIndex)
    {
        if (_filterButtons is null) return;
        var selectedFg = SelectedFilterForeground;
        var unselectedFg = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];

        for (int i = 0; i < _filterButtons.Length; i++)
        {
            if (_filterButtons[i].Content is Grid outerGrid
                && outerGrid.Children.FirstOrDefault() is Grid innerGrid
                && innerGrid.Children.Count > 1)
            {
                bool isSelected = i == selectedIndex;

                // Update label (first child) — dark when selected, revert to MutedTextStyle when not
                if (innerGrid.Children[0] is TextBlock labelTb)
                {
                    if (isSelected)
                        labelTb.Foreground = selectedFg;
                    else
                        labelTb.ClearValue(TextBlock.ForegroundProperty);
                }

                // Update count (second child)
                if (innerGrid.Children[1] is TextBlock countTb)
                {
                    if (countTb.Name == nameof(ProblemCountText) && !isSelected)
                        continue;

                    countTb.Foreground = isSelected ? selectedFg : unselectedFg;
                }
            }
        }

        UpdateProblemCountColor();
    }

    private void PositionPillAtColumn(int columnIndex, bool animate)
    {
        if (_filterButtons is null) return;
        double containerWidth = FilterContainer.ActualWidth;
        if (containerWidth <= 0)
        {
            FilterSelectionPill.Visibility = Visibility.Collapsed;
            return;
        }

        FilterSelectionPill.Visibility = Visibility.Visible;
        double columnWidth = containerWidth / _filterButtons.Length;
        double targetX = columnIndex * columnWidth;

        if (animate)
        {
            AnimatePillToX(targetX);
        }
        else
        {
            PillTransform.X = targetX;
        }
    }

    private void AnimatePillToX(double targetX)
    {
        // Capture current X before Stop (Stop reverts to pre-animation value)
        double fromX = PillTransform.X;
        _currentAnimation?.Stop();
        var duration = new Duration(TimeSpan.FromMilliseconds(250));
        var animation = new DoubleAnimation
        {
            From = fromX,
            To = targetX,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, PillTransform);
        Storyboard.SetTargetProperty(animation, "X");
        _currentAnimation = new Storyboard();
        _currentAnimation.Children.Add(animation);
        _currentAnimation.Begin();
    }

    private void UpdateProblemCountColor()
    {
        if (_selectedFilterIndex == FilterIndexMap["有问题"])
        {
            ProblemCountText.Foreground = SelectedFilterForeground;
            return;
        }

        ProblemCountText.Foreground = _vm.HasProblems
            ? (SolidColorBrush)Application.Current.Resources["DangerBrush"]
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
    }

    private void Package_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "预检模组压缩包";
        }
    }

    private async void Package_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is StorageFile file)
        {
            await App.Current.Services.Mods.InspectPackageAsync(file.Path);
        }
    }

    private CancellationTokenSource? _hideEditButtonCts;

    private void NexusIdContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hideEditButtonCts?.Cancel();
        if (_vm.SelectedMod?.NexusModId is not null)
        {
            NexusIdEditButton.Visibility = Visibility.Visible;
        }
    }

    private async void NexusIdContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hideEditButtonCts?.Cancel();
        _hideEditButtonCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(800, _hideEditButtonCts.Token);
            NexusIdEditButton.Visibility = Visibility.Collapsed;
        }
        catch (TaskCanceledException) { }
    }

    private void NexusIdText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _vm.NexusIdClickCommand.Execute(null);
    }

    private void AuthorText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_vm.OpenSelectedAuthorPageCommand.CanExecute(null))
        {
            _vm.OpenSelectedAuthorPageCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void FolderPathText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Click to copy path
        if (_vm.SelectedMod?.FolderPath is { Length: > 0 } path)
        {
            var dp = new DataPackage();
            dp.SetText(path);
            Clipboard.SetContent(dp);
            _vm.NotifyFeedbackMessage("路径已复制。");
        }
    }

    private void FolderPathText_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Right-click to open folder
        _vm.OpenModFolderCommand.Execute(null);
    }

    private void ModListItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModInfo mod })
        {
            mod.IsPointerOver = true;
        }
    }

    private void ModListItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModInfo mod })
        {
            mod.IsPointerOver = false;
        }
    }

    private void ModCover_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModInfo mod })
        {
            return;
        }

        _vm.SelectedMod = mod;
        if (!string.IsNullOrWhiteSpace(mod.CoverImageUri))
        {
            ShowModCoverPreview(mod.CoverImageUri);
        }

        e.Handled = true;
    }

    private void ShowModCoverPreview(string uri)
    {
        ModCoverPreviewImage.Source = new BitmapImage(new Uri(uri));
        ResetModCoverPreviewTransform();
        ModCoverPreviewOverlay.Visibility = Visibility.Visible;
        ModCoverPreviewOverlay.Focus(FocusState.Programmatic);
    }

    private void HideModCoverPreview()
    {
        ModCoverPreviewOverlay.Visibility = Visibility.Collapsed;
        ModCoverPreviewImage.Source = null;
        _modCoverPreviewDragging = false;
        _modCoverPreviewDragged = false;
    }

    private void ResetModCoverPreviewTransform()
    {
        _modCoverPreviewScale = 1;
        ModCoverPreviewTransform.ScaleX = 1;
        ModCoverPreviewTransform.ScaleY = 1;
        ModCoverPreviewTransform.TranslateX = 0;
        ModCoverPreviewTransform.TranslateY = 0;
    }

    private void ModCoverPreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HideModCoverPreview();
            e.Handled = true;
        }
    }

    private void ModCoverPreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideModCoverPreview();
        e.Handled = true;
    }

    private void ModCoverPreviewImage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ModCoverPreviewImage).Properties.MouseWheelDelta;
        var factor = delta > 0 ? 1.1 : 1 / 1.1;
        _modCoverPreviewScale = Math.Clamp(_modCoverPreviewScale * factor, MinCoverPreviewScale, MaxCoverPreviewScale);
        ModCoverPreviewTransform.ScaleX = _modCoverPreviewScale;
        ModCoverPreviewTransform.ScaleY = _modCoverPreviewScale;
        e.Handled = true;
    }

    private void ModCoverPreviewImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _modCoverPreviewDragging = true;
        _modCoverPreviewDragged = false;
        _modCoverPreviewLastPoint = e.GetCurrentPoint(ModCoverPreviewOverlay).Position;
        ModCoverPreviewImage.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ModCoverPreviewImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_modCoverPreviewDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(ModCoverPreviewOverlay).Position;
        var dx = point.X - _modCoverPreviewLastPoint.X;
        var dy = point.Y - _modCoverPreviewLastPoint.Y;
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
        {
            _modCoverPreviewDragged = true;
        }

        ModCoverPreviewTransform.TranslateX += dx;
        ModCoverPreviewTransform.TranslateY += dy;
        _modCoverPreviewLastPoint = point;
        e.Handled = true;
    }

    private void ModCoverPreviewImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _modCoverPreviewDragging = false;
        ModCoverPreviewImage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ModCoverPreviewImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_modCoverPreviewDragged)
        {
            HideModCoverPreview();
        }

        _modCoverPreviewDragged = false;
        e.Handled = true;
    }

    private void ModsSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _modsSplitterDragging = true;
        _modsSplitterStartX = e.GetCurrentPoint(ModsSplitGrid).Position.X;
        _modsListStartWidth = ModsListColumn.ActualWidth;
        ModsSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ModsSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_modsSplitterDragging)
        {
            return;
        }

        var pointerX = e.GetCurrentPoint(ModsSplitGrid).Position.X;
        var deltaX = pointerX - _modsSplitterStartX;
        var maxListWidth = Math.Max(
            ModsListMinWidth,
            ModsSplitGrid.ActualWidth - ModsSplitter.ActualWidth - ModsDetailMinWidth);
        var listWidth = Math.Clamp(_modsListStartWidth + deltaX, ModsListMinWidth, maxListWidth);
        ModsListColumn.Width = new GridLength(listWidth);
        ModsDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        e.Handled = true;
    }

    private void ModsSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _modsSplitterDragging = false;
        ModsSplitter.ReleasePointerCapture(e.Pointer);
        SaveModsSplitterRatio();
        e.Handled = true;
    }

    private void ModsSplitGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplySavedModsSplitterRatio();
    }

    private void ApplySavedModsSplitterRatio()
    {
        if (_modsSplitterLayoutApplied)
        {
            return;
        }

        var ratio = App.Current.Services.Settings.Current.ModsPageListRatio;
        if (ratio <= 0 || ratio >= 1 || !double.IsFinite(ratio))
        {
            return;
        }

        var availableWidth = ModsSplitGrid.ActualWidth - ModsSplitter.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var maxListWidth = Math.Max(ModsListMinWidth, availableWidth - ModsDetailMinWidth);
        var listWidth = Math.Clamp(availableWidth * ratio, ModsListMinWidth, maxListWidth);
        ModsListColumn.Width = new GridLength(listWidth);
        ModsDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        _modsSplitterLayoutApplied = true;
    }

    private async void SaveModsSplitterRatio()
    {
        var availableWidth = ModsSplitGrid.ActualWidth - ModsSplitter.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(ModsListColumn.ActualWidth / availableWidth, 0.05, 0.95);
        if (!double.IsFinite(ratio))
        {
            return;
        }

        try
        {
            await App.Current.Services.Settings.UpdateAsync(settings => settings.ModsPageListRatio = ratio);
        }
        catch
        {
            // Persisting layout is non-critical.
        }
    }
}
