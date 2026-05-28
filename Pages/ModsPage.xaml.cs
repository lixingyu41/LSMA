using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;
using LSMA.ViewModels;

namespace LSMA.Pages;

public sealed partial class ModsPage : Page
{
    private static readonly Dictionary<string, int> FilterIndexMap = new()
    {
        ["全部"] = 0, ["正常"] = 1, ["可更新"] = 2,
        ["有问题"] = 3, ["已禁用"] = 4, ["收藏"] = 5,
    };

    private ModsViewModel _vm = null!;
    private Button[] _filterButtons = null!;
    private Storyboard? _currentAnimation;

    public ModsPage()
    {
        InitializeComponent();
        _vm = App.Current.Services.Mods;
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _filterButtons = [AllFilterButton, NormalFilterButton, UpdateFilterButton,
                           ProblemFilterButton, DisabledFilterButton, FavoriteFilterButton];
        FilterContainer.SizeChanged += OnFilterContainerSizeChanged;
        UpdateFilterSelection(_vm.CurrentFilter);
        UpdateProblemCountColor();
    }

    private void OnFilterContainerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
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

        PositionPillAtColumn(index, animate: true);
        UpdateButtonColors(index);
    }

    private void UpdateButtonColors(int selectedIndex)
    {
        var selectedFg = new SolidColorBrush(Color.FromArgb(255, 37, 32, 25)); // #252019
        var unselectedFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        for (int i = 0; i < _filterButtons.Length; i++)
        {
            if (_filterButtons[i].Content is StackPanel panel && panel.Children.Count > 1)
            {
                // Only change count TextBlock (second child); labels keep MutedTextStyle
                var countTb = panel.Children[1] as TextBlock;
                if (countTb is null) continue;

                if (countTb.Name == nameof(ProblemCountText))
                    continue; // handled by UpdateProblemCountColor

                countTb.Foreground = i == selectedIndex ? selectedFg : unselectedFg;
            }
        }

        // "正常" count always green — override unselected color
        if (NormalFilterButton.Content is StackPanel normalPanel && normalPanel.Children.Count > 1
            && normalPanel.Children[1] is TextBlock normalCount)
        {
            normalCount.Foreground = (SolidColorBrush)Application.Current.Resources["SuccessBrush"];
        }

        UpdateProblemCountColor();
    }

    private void PositionPillAtColumn(int columnIndex, bool animate)
    {
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
        _currentAnimation?.Stop();
        var duration = new Duration(TimeSpan.FromMilliseconds(250));
        var animation = new DoubleAnimation
        {
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
}
