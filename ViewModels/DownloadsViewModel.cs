using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class DownloadsViewModel : ViewModelBase
{
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly NexusDownloadService _downloadsService;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private List<NexusModInfo> _loadedOnlineMods = [];
    private NexusModInfo? _selectedOnlineMod;
    private NexusFileInfo? _selectedOnlineFile;
    private string _onlineQuery = string.Empty;
    private string _onlinePanelTitle = "趋势";
    private List<NexusFavorite> _favoriteValues = [];
    private CancellationTokenSource? _downloadCancellation;
    private bool _isDownloading;
    private string _onlineSortValue = "趋势";
    private bool _hasLoaded;
    private int _currentPage = 1;
    private const int PageSize = 20;

    public DownloadsViewModel(
        NexusCredentialService credentials,
        NexusClient nexus,
        NexusFavoriteService favoritesService,
        NexusDownloadService downloadsService,
        PlatformService platform,
        DialogService dialogs)
    {
        _credentials = credentials;
        _nexus = nexus;
        _favoritesService = favoritesService;
        _downloadsService = downloadsService;
        _platform = platform;
        _dialogs = dialogs;

        SearchOnlineCommand = new RelayCommand(ApplyOnlineFilter);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedOnlineMod is not null);
        OpenNexusCommand = new AsyncRelayCommand<NexusModInfo?>(OpenNexusAsync, mod => mod is not null);
        LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync, () => SelectedOnlineMod is not null && !IsBusy);
        DownloadFileCommand = new AsyncRelayCommand(DownloadSelectedFileAsync, () => SelectedOnlineFile is not null && !IsBusy);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _isDownloading);
        BrowseCommand = new AsyncRelayCommand<string>(BrowseAsync, _ => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMorePages && !IsBusy);
    }

    public List<string> OnlineSortOptions { get; } = ["趋势", "最多下载", "最多支持", "最近更新", "最新上架"];

    public string SelectedOnlineSort
    {
        get => _onlineSortValue;
        set
        {
            if (SetProperty(ref _onlineSortValue, value))
            {
                ApplyOnlineFilter();
            }
        }
    }

    public ObservableCollection<NexusModInfo> OnlineMods { get; } = [];
    public ObservableCollection<NexusFileInfo> OnlineFiles { get; } = [];
    public ObservableCollection<DownloadQueueItem> DownloadQueue { get; } = [];

    public NexusModInfo? SelectedOnlineMod
    {
        get => _selectedOnlineMod;
        set
        {
            if (SetProperty(ref _selectedOnlineMod, value))
            {
                OnlineFiles.Clear();
                OnPropertyChanged(nameof(FavoriteButtonText));
                NotifyCommands();
            }
        }
    }

    public NexusFileInfo? SelectedOnlineFile
    {
        get => _selectedOnlineFile;
        set
        {
            if (SetProperty(ref _selectedOnlineFile, value))
            {
                NotifyCommands();
            }
        }
    }

    public string OnlineQuery
    {
        get => _onlineQuery;
        set => SetProperty(ref _onlineQuery, value);
    }

    public string OnlinePanelTitle
    {
        get => _onlinePanelTitle;
        private set => SetProperty(ref _onlinePanelTitle, value);
    }

    public string FavoriteButtonText => SelectedOnlineMod?.IsFavorite == true ? "已收藏" : "收藏";

    public IRelayCommand SearchOnlineCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand<NexusModInfo?> OpenNexusCommand { get; }
    public IAsyncRelayCommand LoadFilesCommand { get; }
    public IAsyncRelayCommand DownloadFileCommand { get; }
    public IRelayCommand CancelDownloadCommand { get; }
    public IAsyncRelayCommand<string> BrowseCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }

    public bool HasMorePages { get; private set; } = true;
    public Visibility LoadMoreVisibility => HasMorePages ? Visibility.Visible : Visibility.Collapsed;

    public async Task StartAsync()
    {
        if (!_hasLoaded)
        {
            await AutoBrowseAsync();
        }
    }

    private async Task AutoBrowseAsync()
    {
        const int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var key = RequireNexusKey();
            if (key is null) return;

            IsBusy = true;
            ProgressText = $"正在加载... ({attempt}/{maxRetries})";
            Refresh();
            try
            {
                await SetupOnlinePanelAsync("趋势", key);
                _hasLoaded = true;
                return;
            }
            catch (NexusApiException)
            {
                if (attempt >= maxRetries)
                {
                    await _dialogs.ShowMessageAsync("Nexus", "多次加载失败，请检查网络后重试。");
                    return;
                }
                await Task.Delay(1000);
            }
            finally
            {
                IsBusy = false;
                ProgressText = string.Empty;
                Refresh();
            }
        }
    }

    private async Task RefreshAsync()
    {
        _hasLoaded = false;
        _currentPage = 1;
        _loadedOnlineMods = [];
        _favoriteValues = [];
        HasMorePages = true;
        await AutoBrowseAsync();
    }

    private async Task LoadMoreAsync()
    {
        if (!HasMorePages) return;
        _currentPage++;
        var key = RequireNexusKey();
        if (key is null) return;

        IsBusy = true;
        ProgressText = "加载更多...";
        Refresh();
        try
        {
            await SetupOnlinePanelAsync(OnlinePanelTitle, key, append: true);
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task SetupOnlinePanelAsync(string? feed, string key, bool append = false)
    {
        var newMods = (feed switch
        {
            "最新" => await _nexus.GetLatestAddedAsync(key, append ? _currentPage : 1),
            "最近更新" => await _nexus.GetLatestUpdatedAsync(key, append ? _currentPage : 1),
            _ => await _nexus.GetTrendingAsync(key, append ? _currentPage : 1)
        }).ToList();
        HasMorePages = newMods.Count >= PageSize;

        if (append)
            _loadedOnlineMods.AddRange(newMods);
        else
            _loadedOnlineMods = newMods;

        _favoriteValues = await _favoritesService.LoadAsync();
        foreach (var mod in _loadedOnlineMods)
            mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);

        OnlinePanelTitle = feed ?? "趋势";
        OnlineMods.Clear();
        foreach (var item in _loadedOnlineMods) OnlineMods.Add(item);
        OnPropertyChanged(nameof(LoadMoreVisibility));
    }

    private async Task BrowseAsync(string? feed)
    {
        var key = RequireNexusKey();
        if (key is null) return;

        IsBusy = true;
        ProgressText = "正在加载...";
        Refresh();
        try
        {
            await SetupOnlinePanelAsync(feed, key);
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private void ApplyOnlineFilter()
    {
        var query = OnlineQuery.Trim();
        IEnumerable<NexusModInfo> values = _loadedOnlineMods.Where(mod =>
            string.IsNullOrWhiteSpace(query)
            || mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || mod.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || mod.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || mod.ModId.ToString().Contains(query, StringComparison.Ordinal));
        values = SelectedOnlineSort switch
        {
            "最多下载" => values.OrderByDescending(mod => mod.Downloads),
            "最多支持" => values.OrderByDescending(mod => mod.Endorsements),
            "最近更新" => values.OrderByDescending(mod => mod.UpdatedTimestamp),
            "最新上架" => values.OrderByDescending(mod => mod.ModId),
            _ => values
        };
        OnlineMods.Clear();
        foreach (var item in _loadedOnlineMods) OnlineMods.Add(item);
    }

    private async Task ToggleFavoriteAsync()
    {
        if (SelectedOnlineMod is not { } mod) return;

        _favoriteValues = await _favoritesService.LoadAsync();
        await _favoritesService.ToggleAsync(mod, _favoriteValues);
        mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);
        FeedbackMessage = mod.IsFavorite ? "已加入收藏。" : "已取消收藏。";
        OnPropertyChanged(nameof(FavoriteButtonText));
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private Task OpenNexusAsync(NexusModInfo? mod)
    {
        return mod is null
            ? Task.CompletedTask
            : _platform.OpenUriAsync($"https://www.nexusmods.com/stardewvalley/mods/{mod.ModId}");
    }

    private async Task LoadFilesAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedOnlineMod is not { } mod) return;

        try
        {
            OnlineFiles.Clear();
            foreach (var file in await _nexus.GetFilesAsync(mod.ModId, key))
            {
                OnlineFiles.Add(file);
            }
            SelectedOnlineFile = OnlineFiles.FirstOrDefault();
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
        }
    }

    private async Task DownloadSelectedFileAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedOnlineMod is not { } mod || SelectedOnlineFile is not { } file) return;

        var item = new DownloadQueueItem
        {
            ModId = mod.ModId,
            FileId = file.FileId,
            ModName = mod.Name,
            FileName = file.FileName ?? string.Empty
        };
        DownloadQueue.Add(item);
        _downloadCancellation = new CancellationTokenSource();
        _isDownloading = true;
        NotifyCommands();
        IsBusy = true;
        ProgressText = "正在下载...";
        Refresh();
        try
        {
            var path = await _downloadsService.DownloadAsync(item, key, _downloadCancellation.Token);
            FeedbackMessage = $"{file.FileName} 下载完成。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Canceled;
            FeedbackMessage = "下载已取消。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (Exception exception)
        {
            item.State = DownloadState.Failed;
            item.Error = exception.Message;
            await _dialogs.ShowMessageAsync("下载失败", exception.Message);
        }
        finally
        {
            _isDownloading = false;
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
            NotifyCommands();
        }
    }

    private void CancelDownload()
    {
        _downloadCancellation?.Cancel();
    }

    private string? RequireNexusKey()
    {
        var key = _credentials.GetKey();
        if (key is null)
        {
            _ = _dialogs.ShowMessageAsync("需要 Nexus 授权码", "请先在设置页保存个人 Nexus 授权码。");
        }
        return key;
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(TaskStatus));
    }

    public string TaskStatus => IsBusy ? ProgressText : $"显示 {OnlineMods.Count} 个模组";

    private void NotifyCommands()
    {
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OpenNexusCommand.NotifyCanExecuteChanged();
        LoadFilesCommand.NotifyCanExecuteChanged();
        DownloadFileCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
    }
}
