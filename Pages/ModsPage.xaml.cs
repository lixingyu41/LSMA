using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using LSMA.ViewModels;

namespace LSMA.Pages;

public sealed partial class ModsPage : Page
{
    private ModsViewModel _vm = null!;

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
        UpdateFilterButtonStyles(_vm.CurrentFilter);
        UpdateProblemCountColor();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModsViewModel.CurrentFilter))
        {
            UpdateFilterButtonStyles(_vm.CurrentFilter);
        }
        else if (e.PropertyName == nameof(ModsViewModel.HasProblems))
        {
            UpdateProblemCountColor();
        }
    }

    private void UpdateFilterButtonStyles(string currentFilter)
    {
        var selectedStyle = (Style)Application.Current.Resources["CardFilterButtonCheckedStyle"];
        var unselectedStyle = (Style)Application.Current.Resources["CardFilterButtonStyle"];

        AllFilterButton.Style = currentFilter == "全部" ? selectedStyle : unselectedStyle;
        NormalFilterButton.Style = currentFilter == "正常" ? selectedStyle : unselectedStyle;
        UpdateFilterButton.Style = currentFilter == "可更新" ? selectedStyle : unselectedStyle;
        ProblemFilterButton.Style = currentFilter == "有问题" ? selectedStyle : unselectedStyle;
        DisabledFilterButton.Style = currentFilter == "已禁用" ? selectedStyle : unselectedStyle;
        FavoriteFilterButton.Style = currentFilter == "收藏" ? selectedStyle : unselectedStyle;
        ArchivedFilterButton.Style = currentFilter == "已归档" ? selectedStyle : unselectedStyle;
    }

    private void UpdateProblemCountColor()
    {
        if (_vm.HasProblems)
        {
            ProblemCountText.Foreground = (SolidColorBrush)Application.Current.Resources["DangerBrush"];
        }
        else
        {
            ProblemCountText.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        }
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
