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
    private readonly AppStateService _state;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly NexusDownloadService _downloadsService;
    private readonly ModPackageService _packages;
    private readonly SettingsService _settings;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly NexusModNameTranslationService _nameTranslations;
    private readonly NexusCoverCacheService _coverCache;
    private List<NexusModInfo> _loadedOnlineMods = [];
    private NexusModInfo? _selectedOnlineMod;
    private NexusFileInfo? _selectedOnlineFile;
    private int _onlineFilesLoadVersion;
    private long? _onlineFilesLoadedModId;
    private string _onlineQuery = string.Empty;
    private List<NexusFavorite> _favoriteValues = [];
    private CancellationTokenSource? _downloadCancellation;
    private bool _isDownloading;
    private bool _hasLoaded;
    private int _currentPage = 1;
    private bool _categoriesLoaded;
    private NexusCategory? _selectedOnlineCategory;
    private List<NexusModInfo> _browseOnlineMods = [];
    private int _browseCategoryId;
    private int _browseCurrentPage = 1;
    private bool _browseHasMorePages = true;
    private long? _browseSelectedModId;
    private string _browseSortFieldName = string.Empty;
    private string _browseSortDirection = string.Empty;
    private int? _browseSurpriseSeed;
    private string _activeOnlineQuery = string.Empty;
    private NexusSortOption? _selectedOnlineSortOption;
    private NexusSortDirectionOption? _selectedOnlineSortDirection;
    private int? _surpriseSeed;
    private static readonly NexusCategory AllCategories = new() { CategoryId = 0, Name = "全部分类" };
    private static readonly NexusSortOption DefaultSortOption = CreateSortOption("推荐数", "endorsements");
    private static readonly NexusSortDirectionOption DefaultSortDirection = new() { Name = "倒序", Value = "DESC" };
    private static readonly IReadOnlyList<NexusCategory> FixedCategories =
    [
        AllCategories,
        CreateCategory(1, "音频", "Audio"),
        CreateCategory(2, "建筑", "Buildings"),
        CreateCategory(3, "角色", "Characters"),
        CreateCategory(4, "新角色", "New Characters"),
        CreateCategory(5, "作弊", "Cheats"),
        CreateCategory(6, "服装", "Clothing"),
        CreateCategory(7, "制作", "Crafting"),
        CreateCategory(8, "作物", "Crops"),
        CreateCategory(9, "对话", "Dialogue"),
        CreateCategory(10, "事件", "Events"),
        CreateCategory(11, "扩展", "Expansions"),
        CreateCategory(12, "钓鱼", "Fishing"),
        CreateCategory(13, "家具", "Furniture"),
        CreateCategory(14, "玩法机制", "Gameplay Mechanics"),
        CreateCategory(15, "室内", "Interiors"),
        CreateCategory(16, "物品", "Items"),
        CreateCategory(17, "牲畜和动物", "Livestock and Animals"),
        CreateCategory(18, "地点", "Locations"),
        CreateCategory(19, "地图", "Maps"),
        CreateCategory(20, "杂项", "Miscellaneous"),
        CreateCategory(21, "模组工具", "Modding Tools"),
        CreateCategory(22, "宠物/马", "Pets / Horses"),
        CreateCategory(23, "玩家", "Player"),
        CreateCategory(24, "肖像", "Portraits"),
        CreateCategory(25, "用户界面", "User Interface"),
        CreateCategory(26, "视觉和图形", "Visuals and Graphics"),
    ];
    private static readonly IReadOnlyList<NexusSortOption> FixedSortOptions =
    [
        CreateSortOption("发布日期", "createdAt"),
        DefaultSortOption,
        CreateSortOption("下载次数", "downloads"),
        CreateSortOption("独立下载次数", "uniqueDownloads"),
        CreateSortOption("最后更新", "updatedAt"),
        CreateSortOption("模组名称", "name"),
        CreateSortOption("文件大小", "size"),
        CreateSortOption("最新评论", "lastComment"),
        CreateSortOption("随机", "random"),
    ];
    private static readonly IReadOnlyList<NexusSortDirectionOption> FixedSortDirections =
    [
        DefaultSortDirection,
        new() { Name = "正序", Value = "ASC" },
    ];

    public DownloadsViewModel(
        AppStateService state,
        NexusCredentialService credentials,
        NexusClient nexus,
        NexusFavoriteService favoritesService,
        NexusDownloadService downloadsService,
        ModPackageService packages,
        SettingsService settings,
        PlatformService platform,
        DialogService dialogs,
        NexusModNameTranslationService nameTranslations,
        NexusCoverCacheService coverCache)
    {
        _state = state;
        _credentials = credentials;
        _nexus = nexus;
        _favoritesService = favoritesService;
        _downloadsService = downloadsService;
        _packages = packages;
        _settings = settings;
        _platform = platform;
        _dialogs = dialogs;
        _nameTranslations = nameTranslations;
        _coverCache = coverCache;

        OnlineCategories.Add(AllCategories);
        _selectedOnlineCategory = AllCategories;
        foreach (var option in FixedSortOptions)
        {
            OnlineSortOptions.Add(option);
        }

        foreach (var direction in FixedSortDirections)
        {
            OnlineSortDirections.Add(direction);
        }

        _selectedOnlineSortOption = DefaultSortOption;
        _selectedOnlineSortDirection = DefaultSortDirection;

        SearchOnlineCommand = new AsyncRelayCommand(SearchOnlineAsync, () => !IsBusy);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedOnlineMod is not null);
        OpenNexusCommand = new AsyncRelayCommand<NexusModInfo?>(OpenNexusAsync, mod => mod is not null);
        LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync, () => SelectedOnlineMod is not null && !IsBusy);
        DownloadFileCommand = new AsyncRelayCommand(DownloadLatestFileAsync, () => SelectedOnlineMod is not null && !IsBusy);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _isDownloading);
        InstallMissingDependencyCommand = new AsyncRelayCommand<MissingDependencyAction?>(InstallMissingDependencyAsync, dependency => dependency?.CanDownload == true && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMorePages && !IsBusy);
    }

    public ObservableCollection<NexusCategory> OnlineCategories { get; } = [];
    public ObservableCollection<NexusSortOption> OnlineSortOptions { get; } = [];
    public ObservableCollection<NexusSortDirectionOption> OnlineSortDirections { get; } = [];

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

    public NexusSortOption? SelectedOnlineSortOption
    {
        get => _selectedOnlineSortOption;
        set
        {
            if (SetProperty(ref _selectedOnlineSortOption, value))
            {
                _surpriseSeed = null;
                if (_hasLoaded)
                {
                    _ = ReloadOnlineAsync();
                }
            }
        }
    }

    public NexusSortDirectionOption? SelectedOnlineSortDirection
    {
        get => _selectedOnlineSortDirection;
        set
        {
            if (SetProperty(ref _selectedOnlineSortDirection, value))
            {
                _surpriseSeed = null;
                if (_hasLoaded)
                {
                    _ = ReloadOnlineAsync();
                }
            }
        }
    }

    public ObservableCollection<NexusModInfo> OnlineMods { get; } = [];
    public ObservableCollection<NexusFileInfo> OnlineFiles { get; } = [];
    public ObservableCollection<DownloadQueueItem> DownloadQueue { get; } = [];
    public ObservableCollection<MissingDependencyAction> MissingDependencies { get; } = [];

    public NexusModInfo? SelectedOnlineMod
    {
        get => _selectedOnlineMod;
        set
        {
            if (ReferenceEquals(_selectedOnlineMod, value))
            {
                return;
            }

            if (_selectedOnlineMod is not null)
            {
                _selectedOnlineMod.IsSelected = false;
            }

            if (SetProperty(ref _selectedOnlineMod, value))
            {
                if (value is not null)
                {
                    value.IsSelected = true;
                }

                _onlineFilesLoadVersion++;
                _onlineFilesLoadedModId = null;
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
    public IAsyncRelayCommand<MissingDependencyAction?> InstallMissingDependencyCommand { get; }
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
            ApplyResultMarkers([mod]);
            await _coverCache.ApplyCachedAndQueueAsync([mod]);

            _loadedOnlineMods = [mod];
            OnlineQuery = string.Empty;
            HasMorePages = false;
            OnlineMods.Clear();
            OnlineMods.Add(mod);
            SelectedOnlineMod = mod;
            QueueNameTranslations([mod]);

            OnlineFiles.Clear();
            foreach (var file in await _nexus.GetFilesAsync(mod.ModId, key))
            {
                OnlineFiles.Add(file);
            }

            _onlineFilesLoadedModId = mod.ModId;
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
    {
        if (string.IsNullOrWhiteSpace(OnlineQuery))
        {
            OnlineQuery = string.Empty;
            if (!TryRestoreBrowseList()
                && (!string.IsNullOrWhiteSpace(_activeOnlineQuery) || OnlineMods.Count == 0))
            {
                await ReloadOnlineAsync();
            }

            return;
        }

        await ReloadOnlineAsync();
    }

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
        _surpriseSeed = null;
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

    private Task EnsureCategoriesAsync()
    {
        if (_categoriesLoaded)
        {
            return Task.CompletedTask;
        }

        OnlineCategories.Clear();
        foreach (var category in FixedCategories)
        {
            OnlineCategories.Add(category);
        }

        _categoriesLoaded = true;
        SelectedOnlineCategory ??= AllCategories;
        return Task.CompletedTask;
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
            var result = await _nexus.SearchModsAsync(
                query,
                SelectedCategoryName,
                offset,
                PageSize,
                SelectedSortFieldName,
                SelectedSortDirectionValue,
                GetSurpriseSeed(append));
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
        ApplyResultMarkers(_loadedOnlineMods);
        await _coverCache.ApplyCachedAndQueueAsync(newMods);

        if (append)
        {
            foreach (var item in newMods)
            {
                OnlineMods.Add(item);
            }
        }
        else
        {
            OnlineMods.Clear();
            foreach (var item in _loadedOnlineMods)
            {
                OnlineMods.Add(item);
            }
        }

        if (!append && OnlineMods.Count > 0)
        {
            SelectedOnlineMod = OnlineMods[0];
        }

        QueueNameTranslations(newMods);

        _activeOnlineQuery = query;
        if (string.IsNullOrWhiteSpace(query))
        {
            CacheBrowseList();
        }

        FeedbackMessage = string.IsNullOrWhiteSpace(query) || OnlineMods.Count > 0
            ? null
            : "Nexus 未找到匹配模组。";
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(LoadMoreVisibility));
    }

    private void CacheBrowseList()
    {
        _browseOnlineMods = OnlineMods.ToList();
        _browseCategoryId = SelectedOnlineCategory?.CategoryId ?? 0;
        _browseCurrentPage = _currentPage;
        _browseHasMorePages = HasMorePages;
        _browseSelectedModId = SelectedOnlineMod?.ModId;
        _browseSortFieldName = SelectedSortFieldName;
        _browseSortDirection = SelectedSortDirectionValue;
        _browseSurpriseSeed = _surpriseSeed;
    }

    private bool TryRestoreBrowseList()
    {
        if (_browseOnlineMods.Count == 0
            || _browseCategoryId != (SelectedOnlineCategory?.CategoryId ?? 0)
            || !string.Equals(_browseSortFieldName, SelectedSortFieldName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_browseSortDirection, SelectedSortDirectionValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _loadedOnlineMods = _browseOnlineMods.ToList();
        _currentPage = _browseCurrentPage;
        HasMorePages = _browseHasMorePages;
        _surpriseSeed = _browseSurpriseSeed;
        _activeOnlineQuery = string.Empty;
        OnlineMods.Clear();
        foreach (var item in _loadedOnlineMods)
        {
            OnlineMods.Add(item);
        }

        SelectedOnlineMod = _browseSelectedModId is { } selectedModId
            ? OnlineMods.FirstOrDefault(mod => mod.ModId == selectedModId) ?? OnlineMods.FirstOrDefault()
            : OnlineMods.FirstOrDefault();
        FeedbackMessage = null;
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(LoadMoreVisibility));
        Refresh();
        NotifyCommands();
        return true;
    }

    private string? SelectedCategoryName => SelectedOnlineCategory is { CategoryId: > 0 } category
        ? category.FilterName
        : null;

    private static NexusCategory CreateCategory(int id, string name, string searchName)
        => new() { CategoryId = id, Name = name, SearchName = searchName };

    private string SelectedSortFieldName => SelectedOnlineSortOption?.FieldName ?? DefaultSortOption.FieldName;

    private string SelectedSortDirectionValue => SelectedOnlineSortDirection?.Value ?? DefaultSortDirection.Value;

    private int? GetSurpriseSeed(bool append)
    {
        if (SelectedOnlineSortOption?.IsRandom != true)
        {
            return null;
        }

        if (!append || _surpriseSeed is null)
        {
            _surpriseSeed = Random.Shared.Next(1, int.MaxValue);
        }

        return _surpriseSeed;
    }

    private static NexusSortOption CreateSortOption(string name, string fieldName)
        => new() { Name = name, FieldName = fieldName };

    private void QueueNameTranslations(IReadOnlyList<NexusModInfo> mods)
        => _ = _nameTranslations.ApplyCachedAndQueueAsync(mods);

    private void ApplyResultMarkers(IEnumerable<NexusModInfo> mods)
    {
        var installedIds = _state.Mods
            .Where(mod => !mod.IsArchived && mod.NexusModId is not null)
            .Select(mod => mod.NexusModId!.Value)
            .ToHashSet();
        var favoriteIds = _favoriteValues
            .Select(value => value.ModId)
            .ToHashSet();

        foreach (var mod in mods)
        {
            mod.IsInstalled = installedIds.Contains(mod.ModId);
            mod.IsFavorite = favoriteIds.Contains(mod.ModId);
        }
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
        if (SelectedOnlineMod is not { } mod) return;
        if (_onlineFilesLoadedModId == mod.ModId)
        {
            return;
        }

        var loadVersion = ++_onlineFilesLoadVersion;
        await LoadFilesForSelectedModAsync(mod, loadVersion);
    }

    private async Task LoadFilesForSelectedModAsync(NexusModInfo mod, int loadVersion)
    {
        var key = RequireNexusKey();
        if (key is null) return;

        FeedbackMessage = "正在加载历史版本...";
        OnPropertyChanged(nameof(FeedbackMessage));

        IReadOnlyList<NexusFileInfo> files;
        try
        {
            files = await _nexus.GetFilesAsync(mod.ModId, key);
        }
        catch (NexusApiException exception)
        {
            if (loadVersion == _onlineFilesLoadVersion && ReferenceEquals(SelectedOnlineMod, mod))
            {
                await _dialogs.ShowMessageAsync("Nexus", exception.Message);
            }

            return;
        }

        if (loadVersion != _onlineFilesLoadVersion || !ReferenceEquals(SelectedOnlineMod, mod))
        {
            return;
        }

        OnlineFiles.Clear();
        foreach (var file in files)
        {
            OnlineFiles.Add(file);
        }

        _onlineFilesLoadedModId = mod.ModId;
        SelectedOnlineFile = SelectLatestFile(OnlineFiles);
        FeedbackMessage = OnlineFiles.Count > 0
            ? $"已加载 {OnlineFiles.Count} 个历史版本。"
            : "该模组没有可下载文件。";
        OnPropertyChanged(nameof(FeedbackMessage));
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

        MissingDependencies.Clear();
        OnPropertyChanged(nameof(MissingDependencies));
        await DownloadItemAsync(CreateQueueItem(mod, file), key);
    }

    public async Task<bool> InstallModByIdAsync(long modId, string? displayName = null)
    {
        if (IsBusy)
        {
            return false;
        }

        var key = RequireNexusKey();
        if (key is null)
        {
            return false;
        }

        DownloadQueueItem? queueItem = null;
        IsBusy = true;
        ProgressText = string.IsNullOrWhiteSpace(displayName)
            ? "正在获取前置模组..."
            : $"正在获取前置模组：{displayName}";
        Refresh();
        NotifyCommands();
        try
        {
            var mod = await _nexus.GetModAsync(modId, key);
            var file = SelectLatestFile(await _nexus.GetFilesAsync(mod.ModId, key));
            if (file is null)
            {
                FeedbackMessage = "该前置没有可下载文件。";
                OnPropertyChanged(nameof(FeedbackMessage));
                return false;
            }

            _favoriteValues = await _favoritesService.LoadAsync();
            ApplyResultMarkers([mod]);
            await _coverCache.ApplyCachedAndQueueAsync([mod]);
            SelectedOnlineMod = mod;
            OnlineFiles.Clear();
            OnlineFiles.Add(file);
            SelectedOnlineFile = file;
            queueItem = CreateQueueItem(mod, file);
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("Nexus", exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
            NotifyCommands();
        }

        await DownloadItemAsync(queueItem, key);
        return queueItem.State == DownloadState.Installed;
    }

    private async Task InstallMissingDependencyAsync(MissingDependencyAction? dependency)
    {
        if (dependency?.NexusModId is not { } modId)
        {
            FeedbackMessage = "该前置未绑定 Nexus ID，请在下载页手动搜索。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        if (await InstallModByIdAsync(modId, dependency.UniqueId))
        {
            MissingDependencies.Remove(dependency);
        }

        NotifyCommands();
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
            MergeMissingDependencies(plan.MissingDependencies);
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
            FeedbackMessage = plan.MissingDependencies.Count > 0
                ? $"安装完成：{result.InstalledCount} 个模组。仍缺少 {plan.MissingDependencies.Count} 个前置，可继续下载。"
                : $"安装完成：{result.InstalledCount} 个模组。";
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

    private void MergeMissingDependencies(IEnumerable<MissingDependencyAction> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (!MissingDependencies.Any(value => value.UniqueId.Equals(dependency.UniqueId, StringComparison.OrdinalIgnoreCase)))
            {
                MissingDependencies.Add(dependency);
            }
        }

        InstallMissingDependencyCommand.NotifyCanExecuteChanged();
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
        InstallMissingDependencyCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
    }
}
