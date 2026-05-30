using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class DownloadsViewModel : ViewModelBase
{
    private const string GameDomain = "stardewvalley";
    private const int StardewGameId = 1303;
    private const int PageSize = 20;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly NexusDownloadService _downloadsService;
    private readonly ModPackageService _packages;
    private readonly SettingsService _settings;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private List<NexusModInfo> _loadedOnlineMods = [];
    private NexusModInfo? _selectedOnlineMod;
    private NexusFileInfo? _selectedOnlineFile;
    private string _onlineQuery = string.Empty;
    private List<NexusFavorite> _favoriteValues = [];
    private CancellationTokenSource? _downloadCancellation;
    private bool _isDownloading;
    private bool _hasLoaded;
    private int _currentPage = 1;
    private bool _categoriesLoaded;
    private NexusCategory? _selectedOnlineCategory;
    private static readonly NexusCategory AllCategories = new() { CategoryId = 0, Name = "全部分类" };

    public DownloadsViewModel(
        NexusCredentialService credentials,
        NexusClient nexus,
        NexusFavoriteService favoritesService,
        NexusDownloadService downloadsService,
        ModPackageService packages,
        SettingsService settings,
        PlatformService platform,
        DialogService dialogs)
    {
        _credentials = credentials;
        _nexus = nexus;
        _favoritesService = favoritesService;
        _downloadsService = downloadsService;
        _packages = packages;
        _settings = settings;
        _platform = platform;
        _dialogs = dialogs;

        OnlineCategories.Add(AllCategories);
        _selectedOnlineCategory = AllCategories;

        SearchOnlineCommand = new AsyncRelayCommand(SearchOnlineAsync, () => !IsBusy);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedOnlineMod is not null);
        OpenNexusCommand = new AsyncRelayCommand<NexusModInfo?>(OpenNexusAsync, mod => mod is not null);
        LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync, () => SelectedOnlineMod is not null && !IsBusy);
        DownloadFileCommand = new AsyncRelayCommand(DownloadLatestFileAsync, () => SelectedOnlineMod is not null && !IsBusy);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _isDownloading);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMorePages && !IsBusy);
    }

    public ObservableCollection<NexusCategory> OnlineCategories { get; } = [];

    public NexusCategory? SelectedOnlineCategory
    {
        get => _selectedOnlineCategory;
        set
        {
            if (SetProperty(ref _selectedOnlineCategory, value) && _categoriesLoaded)
            {
                _ = ReloadOnlineAsync();
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
                SelectedOnlineFile = null;
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

    public string FavoriteButtonText => SelectedOnlineMod?.IsFavorite == true ? "已收藏" : "收藏";

    public IAsyncRelayCommand SearchOnlineCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand<NexusModInfo?> OpenNexusCommand { get; }
    public IAsyncRelayCommand LoadFilesCommand { get; }
    public IAsyncRelayCommand DownloadFileCommand { get; }
    public IRelayCommand CancelDownloadCommand { get; }
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

    public async Task FocusModAsync(long modId)
    {
        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        _hasLoaded = true;
        IsBusy = true;
        ProgressText = "正在加载更新模组...";
        Refresh();
        NotifyCommands();
        try
        {
            var mod = await _nexus.GetModAsync(modId, key);
            _favoriteValues = await _favoritesService.LoadAsync();
            mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);

            _loadedOnlineMods = [mod];
            OnlineQuery = string.Empty;
            HasMorePages = false;
            OnlineMods.Clear();
            OnlineMods.Add(mod);
            SelectedOnlineMod = mod;

            OnlineFiles.Clear();
            foreach (var file in await _nexus.GetFilesAsync(mod.ModId, key))
            {
                OnlineFiles.Add(file);
            }

            SelectedOnlineFile = SelectLatestFile(OnlineFiles);
            FeedbackMessage = OnlineFiles.Count > 0
                ? $"已加载 {mod.Name}，可直接下载更新文件。"
                : "该模组没有可下载文件。";
            OnPropertyChanged(nameof(FeedbackMessage));
            OnPropertyChanged(nameof(LoadMoreVisibility));
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
            NotifyCommands();
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
                await EnsureCategoriesAsync();
                _currentPage = 1;
                await LoadOnlinePageAsync(key, append: false);
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
                NotifyCommands();
            }
        }
    }

    private async Task RefreshAsync()
        => await ReloadOnlineAsync();

    private async Task SearchOnlineAsync()
        => await ReloadOnlineAsync();

    private async Task ReloadOnlineAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        _currentPage = 1;
        _loadedOnlineMods = [];
        _favoriteValues = [];
        HasMorePages = true;
        IsBusy = true;
        ProgressText = "正在加载...";
        Refresh();
        NotifyCommands();
        try
        {
            await EnsureCategoriesAsync();
            await LoadOnlinePageAsync(key, append: false);
            _hasLoaded = true;
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
            NotifyCommands();
        }
    }

    private async Task LoadMoreAsync()
    {
        if (!HasMorePages || IsBusy) return;
        _currentPage++;
        var key = RequireNexusKey();
        if (key is null) return;

        IsBusy = true;
        ProgressText = "正在加载...";
        Refresh();
        NotifyCommands();
        try
        {
            await EnsureCategoriesAsync();
            await LoadOnlinePageAsync(key, append: true);
        }
        catch (NexusApiException exception)
        {
            _currentPage--;
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
            NotifyCommands();
        }
    }

    private async Task EnsureCategoriesAsync()
    {
        if (_categoriesLoaded)
        {
            return;
        }

        var categories = await _nexus.GetModCategoriesAsync();
        OnlineCategories.Clear();
        OnlineCategories.Add(AllCategories);
        foreach (var category in categories
            .Where(category => !string.IsNullOrWhiteSpace(category.Name))
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            OnlineCategories.Add(category);
        }

        _categoriesLoaded = true;
        SelectedOnlineCategory ??= AllCategories;
    }

    private async Task LoadOnlinePageAsync(string key, bool append)
    {
        var query = OnlineQuery.Trim();
        var offset = append ? (_currentPage - 1) * PageSize : 0;
        List<NexusModInfo> newMods;
        var totalCount = 0;

        if (!append && long.TryParse(query, out var modId) && modId > 0)
        {
            newMods = [await _nexus.GetModAsync(modId, key)];
            totalCount = newMods.Count;
            HasMorePages = false;
        }
        else
        {
            var result = await _nexus.SearchModsAsync(query, SelectedCategoryName, offset, PageSize);
            newMods = result.Mods.ToList();
            totalCount = result.TotalCount;
            HasMorePages = offset + newMods.Count < totalCount;
        }

        if (append)
        {
            _loadedOnlineMods.AddRange(newMods);
        }
        else
        {
            _loadedOnlineMods = newMods;
            SelectedOnlineMod = null;
            OnlineFiles.Clear();
        }

        _favoriteValues = await _favoritesService.LoadAsync();
        foreach (var mod in _loadedOnlineMods)
        {
            mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);
        }

        OnlineMods.Clear();
        foreach (var item in _loadedOnlineMods) OnlineMods.Add(item);
        if (!append && OnlineMods.Count > 0)
        {
            SelectedOnlineMod = OnlineMods[0];
        }

        FeedbackMessage = string.IsNullOrWhiteSpace(query) || OnlineMods.Count > 0
            ? null
            : "Nexus 未找到匹配模组。";
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(LoadMoreVisibility));
    }

    private string? SelectedCategoryName => SelectedOnlineCategory is { CategoryId: > 0 } category
        ? category.Name
        : null;

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

        IsBusy = true;
        ProgressText = "正在刷新所有历史版本...";
        Refresh();
        NotifyCommands();
        try
        {
            OnlineFiles.Clear();
            foreach (var file in await _nexus.GetFilesAsync(mod.ModId, key))
            {
                OnlineFiles.Add(file);
            }
            SelectedOnlineFile = OnlineFiles.FirstOrDefault();
            FeedbackMessage = OnlineFiles.Count > 0
                ? $"已加载 {OnlineFiles.Count} 个历史版本。"
                : "该模组没有可下载文件。";
            OnPropertyChanged(nameof(FeedbackMessage));
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
            NotifyCommands();
        }
    }

    private async Task DownloadLatestFileAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedOnlineMod is not { } mod) return;

        NexusFileInfo? file;
        IsBusy = true;
        ProgressText = "正在获取最新文件...";
        Refresh();
        NotifyCommands();
        try
        {
            var files = await _nexus.GetFilesAsync(mod.ModId, key);
            file = SelectLatestFile(files);
            if (file is null)
            {
                FeedbackMessage = "该模组没有可下载文件。";
                OnPropertyChanged(nameof(FeedbackMessage));
                return;
            }

            OnlineFiles.Clear();
            OnlineFiles.Add(file);
            SelectedOnlineFile = file;
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
            return;
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
            NotifyCommands();
        }

        await DownloadItemAsync(CreateQueueItem(mod, file), key);
    }

    private static NexusFileInfo? SelectLatestFile(IReadOnlyList<NexusFileInfo> files)
    {
        var preferredFiles = files
            .Where(file => string.Equals(file.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (preferredFiles.Count == 0)
        {
            preferredFiles = files.ToList();
        }

        return preferredFiles
            .OrderByDescending(file => file.UploadedTimestamp)
            .ThenByDescending(file => file.FileId)
            .FirstOrDefault();
    }

    public async Task HandleNxmLinkAsync(string link)
    {
        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        if (!NexusDownloadToken.TryParse(link, out var token) || token is null)
        {
            if (NexusRequirementsPopupLink.TryParse(link, out var popup) && popup is not null)
            {
                await HandleRequirementsPopupLinkAsync(popup);
                return;
            }

            await _dialogs.ShowMessageAsync("下载链接无效", "该链接不是 Nexus 下载令牌；请直接在下载页点击“下载最新”。");
            return;
        }

        if (!string.Equals(token.GameDomain, GameDomain, StringComparison.OrdinalIgnoreCase))
        {
            await _dialogs.ShowMessageAsync("游戏不匹配", "该 Nexus 链接不是星露谷物语文件。");
            return;
        }

        if (token.IsExpired)
        {
            await _dialogs.ShowMessageAsync("下载链接已过期", "请回到 Nexus 文件页重新点击 Mod Manager Download。");
            return;
        }

        var item = await CreateQueueItemAsync(token, key);
        await DownloadItemAsync(item, key, token);
    }

    private static DownloadQueueItem CreateQueueItem(NexusModInfo mod, NexusFileInfo file)
    {
        return new DownloadQueueItem
        {
            ModId = mod.ModId,
            FileId = file.FileId,
            ModName = mod.Name,
            FileName = file.FileName ?? string.Empty
        };
    }

    private async Task<DownloadQueueItem> CreateQueueItemAsync(NexusDownloadToken token, string apiKey)
    {
        var modName = SelectedOnlineMod?.ModId == token.ModId
            ? SelectedOnlineMod.Name
            : $"Nexus 模组 {token.ModId}";
        var fileName = SelectedOnlineFile?.FileId == token.FileId
            ? SelectedOnlineFile.FileName ?? string.Empty
            : string.Empty;

        try
        {
            if (SelectedOnlineMod?.ModId != token.ModId)
            {
                modName = (await _nexus.GetModAsync(token.ModId, apiKey)).Name;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                var file = (await _nexus.GetFilesAsync(token.ModId, apiKey))
                    .FirstOrDefault(value => value.FileId == token.FileId);
                fileName = file?.FileName ?? file?.Name ?? string.Empty;
            }
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
        }

        return new DownloadQueueItem
        {
            ModId = token.ModId,
            FileId = token.FileId,
            ModName = modName,
            FileName = fileName
        };
    }

    private async Task DownloadItemAsync(DownloadQueueItem item, string key, NexusDownloadToken? token = null)
    {
        DownloadQueue.Add(item);
        _downloadCancellation = new CancellationTokenSource();
        _isDownloading = true;
        NotifyCommands();
        IsBusy = true;
        ProgressText = "正在下载...";
        Refresh();
        try
        {
            var path = await _downloadsService.DownloadAsync(item, key, token, _downloadCancellation.Token);
            await InstallDownloadedPackageAsync(item, path);
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Canceled;
            FeedbackMessage = "下载已取消。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (NexusApiException exception) when (exception.RequiresBrowserDownload && token is null)
        {
            item.State = DownloadState.Pending;
            item.Error = "等待 Nexus 确认";
            ProgressText = "等待 Nexus 确认...";
            FeedbackMessage = "LSMA 正在自动完成 Nexus 下载确认。";
            OnPropertyChanged(nameof(FeedbackMessage));
            Refresh();
            var browserToken = await _dialogs.ShowNexusDownloadBrowserAsync(
                CreateNexusFilePageUrl(item.ModId, item.FileId),
                item.ModId,
                item.FileId,
                _credentials.GetWebLogin(),
                _settings.Current.NexusDownloadDebugStepMode,
                _settings.Current.NexusDownloadDebugShowWebViewMode,
                UpdateNexusDownloadProgress,
                _downloadCancellation.Token);
            if (browserToken is null)
            {
                item.State = DownloadState.Canceled;
                FeedbackMessage = "下载确认已取消。";
                OnPropertyChanged(nameof(FeedbackMessage));
                return;
            }

            await DownloadPendingBrowserItemAsync(item, key, browserToken, CancellationToken.None);
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

    private static string CreateNexusFilePageUrl(long modId, long fileId)
        => $"https://www.nexusmods.com/stardewvalley/mods/{modId}?tab=files&file_id={fileId}&nmm=1";

    private void UpdateNexusDownloadProgress(string message)
    {
        ProgressText = message;
        FeedbackMessage = message;
        OnPropertyChanged(nameof(FeedbackMessage));
        Refresh();
    }

    private async Task DownloadPendingBrowserItemAsync(
        DownloadQueueItem item,
        string key,
        NexusDownloadToken token,
        CancellationToken cancellationToken)
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isDownloading = true;
        IsBusy = true;
        ProgressText = "正在下载...";
        NotifyCommands();
        Refresh();
        try
        {
            var path = await _downloadsService.DownloadAsync(item, key, token, _downloadCancellation.Token);
            await InstallDownloadedPackageAsync(item, path);
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

    private async Task InstallDownloadedPackageAsync(DownloadQueueItem item, string packagePath)
    {
        item.State = DownloadState.AwaitingInstall;
        ProgressText = "正在安装模组...";
        FeedbackMessage = $"{Path.GetFileName(packagePath)} 下载完成，正在安装。";
        OnPropertyChanged(nameof(FeedbackMessage));
        Refresh();

        ModInstallPlan? plan = null;
        try
        {
            await App.Current.Services.Mods.ScanForLaunchAsync();
            plan = await _packages.InspectAsync(packagePath);
            if (!plan.CanInstall)
            {
                var message = plan.Blockers.Count > 0
                    ? string.Join(Environment.NewLine, plan.Blockers)
                    : "没有识别到可安装模组。";
                item.State = DownloadState.InstallFailed;
                item.Error = message;
                FeedbackMessage = $"自动安装失败：{message}";
                OnPropertyChanged(nameof(FeedbackMessage));
                await _dialogs.ShowMessageAsync("安装未完成", message);
                return;
            }

            if (plan.Items.Any(value => value.ExistingMod is not null) && _settings.Current.BackupSaveBeforeUpdate)
            {
                ProgressText = "正在备份存档...";
                Refresh();
                await App.Current.Services.Saves.BackupForLaunchAsync();
            }

            ProgressText = "正在安全安装模组...";
            Refresh();
            var result = await _packages.InstallAsync(plan);
            if (result.FailedCount > 0)
            {
                var message = string.Join(Environment.NewLine, result.Messages);
                item.State = DownloadState.InstallFailed;
                item.Error = message;
                FeedbackMessage = $"自动安装失败：成功 {result.InstalledCount}，失败 {result.FailedCount}。{Environment.NewLine}{message}";
                OnPropertyChanged(nameof(FeedbackMessage));
                await _dialogs.ShowMessageAsync("安装未完成", message);
                return;
            }

            item.State = DownloadState.Installed;
            item.Error = null;
            FeedbackMessage = $"安装完成：{result.InstalledCount} 个模组。";
            OnPropertyChanged(nameof(FeedbackMessage));
            await App.Current.Services.Mods.ScanForLaunchAsync();
        }
        catch (Exception exception)
        {
            item.State = DownloadState.InstallFailed;
            item.Error = exception.Message;
            FeedbackMessage = $"自动安装失败：{exception.Message}";
            OnPropertyChanged(nameof(FeedbackMessage));
            await _dialogs.ShowMessageAsync("安装失败", exception.Message);
        }
        finally
        {
            if (plan is not null)
            {
                await _packages.CleanupPreparedPackageAsync(plan);
            }
        }
    }

    private async Task HandleRequirementsPopupLinkAsync(NexusRequirementsPopupLink popup)
    {
        if (popup.GameId != StardewGameId)
        {
            await _dialogs.ShowMessageAsync("游戏不匹配", "该 Nexus 链接不是星露谷物语文件。");
            return;
        }

        await _dialogs.ShowMessageAsync("继续下载", "请回到下载页选择同一个文件后点“下载最新”，LSMA 会自动完成 Nexus 确认。");
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
        SearchOnlineCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OpenNexusCommand.NotifyCanExecuteChanged();
        LoadFilesCommand.NotifyCanExecuteChanged();
        DownloadFileCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
    }
}
