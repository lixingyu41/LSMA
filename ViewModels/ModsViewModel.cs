using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class ModsViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly GameRunLockService _runLock;
    private readonly ModScannerService _scanner;
    private readonly ModAnalyzerService _analyzer;
    private readonly ModBackupService _backups;
    private readonly ModTransactionService _transactions;
    private readonly ModPackageService _packages;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly NexusDownloadService _downloadsService;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly AutomaticScanMonitor _automaticScanMonitor;
    private List<ModInfo> _allMods = [];
    private ModInfo? _selectedMod;
    private string _filter = "全部";
    private ModInstallPlan? _pendingPlan;
    private NexusModInfo? _selectedOnlineMod;
    private NexusFileInfo? _selectedOnlineFile;
    private string _onlineQuery = string.Empty;
    private string _onlinePanelTitle = "发现";
    private bool _onlinePanelVisible;
    private string _nexusModIdInput = string.Empty;
    private bool _isEditingNexusBinding;
    private List<NexusModInfo> _loadedOnlineMods = [];
    private List<NexusFavorite> _favoriteValues = [];
    private string _onlineSort = "默认";
    private NexusCategory? _selectedCategory;
    private CancellationTokenSource? _downloadCancellation;
    private bool _isDownloading;
    private bool _automaticScanningStarted;

    public ModsViewModel(
        AppStateService state,
        GameRunLockService runLock,
        ModScannerService scanner,
        ModAnalyzerService analyzer,
        ModBackupService backups,
        ModTransactionService transactions,
        ModPackageService packages,
        NexusCredentialService credentials,
        NexusClient nexus,
        NexusFavoriteService favoritesService,
        NexusDownloadService downloadsService,
        PlatformService platform,
        DialogService dialogs,
        UiDispatcherService dispatcher)
    {
        _state = state;
        _runLock = runLock;
        _scanner = scanner;
        _analyzer = analyzer;
        _backups = backups;
        _transactions = transactions;
        _packages = packages;
        _credentials = credentials;
        _nexus = nexus;
        _favoritesService = favoritesService;
        _downloadsService = downloadsService;
        _platform = platform;
        _dialogs = dialogs;
        _automaticScanMonitor = new AutomaticScanMonitor(dispatcher, ScanAutomaticallyAsync);
        FilterCommand = new RelayCommand<string>(ApplyFilter);
        EnableCommand = new AsyncRelayCommand(() => ChangeEnabledAsync(true), CanEnable);
        DisableCommand = new AsyncRelayCommand(() => ChangeEnabledAsync(false), CanDisable);
        BackupCommand = new AsyncRelayCommand(BackupAsync, HasSelected);
        OpenModFolderCommand = new AsyncRelayCommand(OpenModFolderAsync, HasSelected);
        OpenBackupsCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(AppPaths.ModBackups));
        ChoosePackageCommand = new AsyncRelayCommand(ChoosePackageAsync, CanModify);
        InstallPackageCommand = new AsyncRelayCommand(InstallPackageAsync, CanInstallPackage);
        CancelPackageCommand = new AsyncRelayCommand(ClearPackagePlanAsync);
        BrowseCommand = new AsyncRelayCommand<string>(BrowseAsync, CanUseOnline);
        ShowInstalledCommand = new RelayCommand(() => OnlinePanelVisible = false);
        ShowFavoritesCommand = new AsyncRelayCommand(ShowFavoritesAsync);
        ShowDownloadsCommand = new AsyncRelayCommand(ShowDownloadsAsync);
        SearchOnlineCommand = new RelayCommand(ApplyOnlineFilter);
        SortOnlineCommand = new RelayCommand<string>(SetOnlineSort);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedOnlineMod is not null);
        OpenNexusCommand = new AsyncRelayCommand(OpenNexusAsync, () => SelectedOnlineMod is not null);
        LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync, () => SelectedOnlineMod is not null && !IsBusy);
        DownloadFileCommand = new AsyncRelayCommand(DownloadSelectedFileAsync, () => SelectedOnlineFile is not null && !IsBusy);
        CancelDownloadCommand = new RelayCommand(CancelDownload, () => _isDownloading);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, CanUseOnlineNow);
        BindNexusIdCommand = new AsyncRelayCommand(BindNexusIdAsync, () => SelectedMod is not null);
        EditNexusBindingCommand = new RelayCommand(ToggleNexusBindingEditor, () => SelectedMod is not null);
        NexusIdClickCommand = new RelayCommand(HandleNexusIdClick, () => SelectedMod is not null);
        PrepareSelectedUpdateCommand = new AsyncRelayCommand(PrepareSelectedUpdateAsync, () => SelectedMod?.NexusModId is not null && !IsBusy);
        ImportFavoritesCommand = new AsyncRelayCommand(ImportFavoritesAsync);
        ExportFavoritesCommand = new AsyncRelayCommand(ExportFavoritesAsync);
        _state.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppStateService.IsGameRunning) or nameof(AppStateService.GameDirectory))
            {
                Refresh();
            }

            if (args.PropertyName == nameof(AppStateService.GameDirectory) && _automaticScanningStarted)
            {
                ConfigureAutomaticWatchers();
                _automaticScanMonitor.RequestRefresh();
            }
        };
    }

    public ObservableCollection<ModInfo> Mods { get; } = [];
    public ObservableCollection<NexusModInfo> OnlineMods { get; } = [];
    public ObservableCollection<NexusFileInfo> OnlineFiles { get; } = [];
    public ObservableCollection<NexusFavorite> Favorites { get; } = [];
    public ObservableCollection<DownloadQueueItem> DownloadQueue { get; } = [];
    public ObservableCollection<NexusCategory> Categories { get; } = [];
    public IRelayCommand<string> FilterCommand { get; }
    public IAsyncRelayCommand EnableCommand { get; }
    public IAsyncRelayCommand DisableCommand { get; }
    public IAsyncRelayCommand BackupCommand { get; }
    public IAsyncRelayCommand OpenModFolderCommand { get; }
    public IRelayCommand NexusIdClickCommand { get; }
    public IAsyncRelayCommand OpenBackupsCommand { get; }
    public IAsyncRelayCommand ChoosePackageCommand { get; }
    public IAsyncRelayCommand InstallPackageCommand { get; }
    public IAsyncRelayCommand CancelPackageCommand { get; }
    public IAsyncRelayCommand<string> BrowseCommand { get; }
    public IRelayCommand ShowInstalledCommand { get; }
    public IAsyncRelayCommand ShowFavoritesCommand { get; }
    public IAsyncRelayCommand ShowDownloadsCommand { get; }
    public IRelayCommand SearchOnlineCommand { get; }
    public IRelayCommand<string> SortOnlineCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand OpenNexusCommand { get; }
    public IAsyncRelayCommand LoadFilesCommand { get; }
    public IAsyncRelayCommand DownloadFileCommand { get; }
    public IRelayCommand CancelDownloadCommand { get; }
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand BindNexusIdCommand { get; }
    public IRelayCommand EditNexusBindingCommand { get; }
    public IAsyncRelayCommand PrepareSelectedUpdateCommand { get; }
    public IAsyncRelayCommand ImportFavoritesCommand { get; }
    public IAsyncRelayCommand ExportFavoritesCommand { get; }

    public ModInfo? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (SetProperty(ref _selectedMod, value))
            {
                NexusModIdInput = value?.NexusModId?.ToString() ?? string.Empty;
                _isEditingNexusBinding = value?.NexusModId is null;
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(NexusBindingEditorVisibility));
                OnPropertyChanged(nameof(NexusBoundVisibility));
                OnPropertyChanged(nameof(CancelNexusBindingVisibility));
                OnPropertyChanged(nameof(NexusBindingText));
                OnPropertyChanged(nameof(EnableVisibility));
                OnPropertyChanged(nameof(DisableVisibility));
                OnPropertyChanged(nameof(NexusIdDisplayText));
                OnPropertyChanged(nameof(DependencySectionVisibility));
                NotifyCommands();
            }
        }
    }

    public NexusModInfo? SelectedOnlineMod
    {
        get => _selectedOnlineMod;
        set
        {
            if (SetProperty(ref _selectedOnlineMod, value))
            {
                OnlineFiles.Clear();
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

    public string NexusModIdInput
    {
        get => _nexusModIdInput;
        set => SetProperty(ref _nexusModIdInput, value);
    }

    public NexusCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyOnlineFilter();
            }
        }
    }

    public bool OnlinePanelVisible
    {
        get => _onlinePanelVisible;
        set
        {
            if (SetProperty(ref _onlinePanelVisible, value))
            {
                OnPropertyChanged(nameof(OnlinePanelVisibility));
            }
        }
    }

    public Visibility UnavailableVisibility => _state.IsGameConfigured ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AvailableVisibility => _state.IsGameConfigured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => _state.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => SelectedMod is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NexusBindingEditorVisibility => SelectedMod is not null && (SelectedMod.NexusModId is null || _isEditingNexusBinding)
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility NexusBoundVisibility => SelectedMod?.NexusModId is not null && !_isEditingNexusBinding
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CancelNexusBindingVisibility => SelectedMod?.NexusModId is not null && _isEditingNexusBinding
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility EnableVisibility => SelectedMod is { IsEnabled: false, IsArchived: false } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DisableVisibility => SelectedMod is { IsEnabled: true, IsArchived: false } ? Visibility.Visible : Visibility.Collapsed;
    public string NexusBindingText => SelectedMod?.NexusModId is { } id ? $"Nexus Mod ID：{id}" : "未绑定 Nexus Mod ID";
    public string NexusIdDisplayText => SelectedMod?.NexusModId is { } id ? $"ID：{id}" : "手动匹配ID";
    public Visibility DependencySectionVisibility => SelectedMod?.HasRequiredDependencies == true ? Visibility.Visible : Visibility.Collapsed;
    public string InstalledCount => _allMods.Count.ToString();
    public string HealthyCount => _allMods.Count(mod => mod.IsEnabled && mod.Issues.Count == 0).ToString();
    public string ProblemCount => _allMods.Count(mod => mod.Issues.Count > 0).ToString();
    public string DisabledCount => _allMods.Count(mod => !mod.IsEnabled && !mod.IsArchived).ToString();
    public string MissingDependencyCount => _allMods.Count(mod => mod.Issues.Any(issue => issue.Message.Contains("前置", StringComparison.Ordinal))).ToString();
    public string FavoriteCount => _allMods.Count(mod => mod.IsFavorite).ToString();
    public string ArchivedCount => _allMods.Count(mod => mod.IsArchived).ToString();
    public string UpdateCount => _allMods.Count(mod => mod.HasUpdate).ToString();
    public string CurrentFilter => _filter;
    public bool HasProblems => _allMods.Any(mod => mod.Issues.Count > 0);
    public string TaskStatus => IsBusy ? ProgressText : $"当前筛选：{_filter}，显示 {Mods.Count} 个模组";
    public ModInstallPlan? PendingPlan
    {
        get => _pendingPlan;
        private set
        {
            if (SetProperty(ref _pendingPlan, value))
            {
                OnPropertyChanged(nameof(PlanVisibility));
                InstallPackageCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public Visibility PlanVisibility => PendingPlan is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OnlinePanelVisibility => OnlinePanelVisible ? Visibility.Visible : Visibility.Collapsed;
    public string OnlinePanelTitle
    {
        get => _onlinePanelTitle;
        private set => SetProperty(ref _onlinePanelTitle, value);
    }
    public string RateLimitText => _nexus.RateLimitStatus;

    public void Refresh()
    {
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(TaskStatus));
        NotifyCommands();
    }

    public async Task StartAutomaticScanningAsync()
    {
        _automaticScanningStarted = true;
        ConfigureAutomaticWatchers();
        await ScanAsync();
    }

    public Task ScanForLaunchAsync() => ScanAsync();

    public async Task InspectPackageAsync(string packagePath)
    {
        if (!CanModify())
        {
            return;
        }

        if (_allMods.Count == 0)
        {
            await ScanAsync();
        }

        IsBusy = true;
        ProgressText = "正在预检压缩包...";
        Refresh();
        try
        {
            PendingPlan = await _packages.InspectAsync(packagePath);
            FeedbackMessage = PendingPlan.Summary;
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("预检失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    public async Task<int> AutoRepairAsync()
    {
        var nestedRepairs = _allMods
            .Where(mod => mod.IsEnabled && mod.CanRepairNestedDirectory)
            .ToList();
        var problems = _allMods
            .Where(mod => mod.IsEnabled && !mod.CanRepairNestedDirectory
                && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Error))
            .ToList();
        if (problems.Count == 0 && nestedRepairs.Count == 0)
        {
            return 0;
        }

        if (!await _dialogs.ConfirmAsync(
                "一键修复",
                $"将修复 {nestedRepairs.Count} 个明确的目录问题，并禁用 {problems.Count} 个阻断启动的模组。每项变更前都会自动创建恢复点。",
                "开始安全修复"))
        {
            return -1;
        }

        IsBusy = true;
        ProgressText = "正在创建恢复点并执行安全修复...";
        Refresh();
        var completed = 0;
        try
        {
            foreach (var mod in nestedRepairs)
            {
                await _transactions.RepairNestedDirectoryAsync(mod);
                completed++;
            }

            foreach (var mod in problems)
            {
                await _transactions.SetEnabledAsync(mod, false);
                completed++;
            }

            FeedbackMessage = $"已完成 {completed} 项安全修复。";
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("修复未全部完成", $"{completed} 个模组已处理。{exception.Message}");
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            await ScanAsync();
            Refresh();
        }

        return completed;
    }

    private async Task ScanAsync()
    {
        if (!_state.IsGameConfigured || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在自动刷新本地模组...";
        Refresh();
        try
        {
            var selectedPath = SelectedMod?.FolderPath;
            _runLock.Refresh();
            _allMods = _analyzer.Analyze(await _scanner.ScanAsync()).ToList();
            _state.Mods = _allMods;
            ApplyFilter(_filter);
            SelectedMod = Mods.FirstOrDefault(mod => string.Equals(mod.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? Mods.FirstOrDefault();
            App.Current.Services.Home.Refresh();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("自动刷新失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task ScanAutomaticallyAsync()
    {
        if (IsBusy)
        {
            _automaticScanMonitor.RequestRefresh();
            return;
        }

        ConfigureAutomaticWatchers();
        await ScanAsync();
    }

    private void ConfigureAutomaticWatchers()
    {
        if (!_automaticScanningStarted || _state.GameDirectory is not { } game)
        {
            _automaticScanMonitor.ReplaceWatchers();
            return;
        }

        var modsPath = Path.Combine(game.Path, "Mods");
        var disabledPath = Path.Combine(game.Path, "Mods.Disabled");
        _automaticScanMonitor.ReplaceWatchers(
            new AutomaticScanWatchTarget(
                game.Path,
                true,
                path => IsWithinDirectory(path, modsPath) || IsWithinDirectory(path, disabledPath)),
            new AutomaticScanWatchTarget(AppPaths.ArchivedMods, true));
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter(string? filter)
    {
        _filter = string.IsNullOrWhiteSpace(filter) ? "全部" : filter;
        var items = _allMods.Where(mod => _filter switch
        {
            "正常" => mod.IsEnabled && mod.Issues.Count == 0,
            "缺少前置" => mod.Issues.Any(issue => issue.Message.Contains("前置", StringComparison.Ordinal)),
            "有问题" => mod.Issues.Count > 0,
            "已禁用" => !mod.IsEnabled,
            "已归档" => mod.IsArchived,
            "收藏" => mod.IsFavorite,
            "可更新" => mod.HasUpdate,
            _ => true
        });
        Mods.Clear();
        foreach (var mod in items)
        {
            Mods.Add(mod);
        }

        OnPropertyChanged(nameof(TaskStatus));
        OnPropertyChanged(nameof(CurrentFilter));
        OnPropertyChanged(nameof(HasProblems));
    }

    private async Task ChangeEnabledAsync(bool enabled)
    {
        if (SelectedMod is not { } mod)
        {
            return;
        }

        await RunModificationAsync(
            () => _transactions.SetEnabledAsync(mod, enabled),
            enabled ? "模组已启用。" : "模组已禁用。");
    }

    private Task OpenModFolderAsync()
    {
        return SelectedMod is null ? Task.CompletedTask : _platform.OpenFolderAsync(SelectedMod.FolderPath);
    }

    private async Task BackupAsync()
    {
        if (SelectedMod is not { } mod)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在创建模组备份...";
        Refresh();
        try
        {
            await _backups.CreateAsync(mod, "手动备份");
            FeedbackMessage = "备份已创建。";
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("备份失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task ChoosePackageAsync()
    {
        var path = await _platform.ChooseArchiveAsync();
        if (path is not null)
        {
            await InspectPackageAsync(path);
        }
    }

    private async Task InstallPackageAsync()
    {
        if (PendingPlan is not { CanInstall: true } plan
            || !await _dialogs.ConfirmAsync("执行安装计划", $"将安装或更新 {plan.Items.Count} 个模组。已有版本会自动备份，旧配置会尽量保留。", "开始安装"))
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在安全安装模组...";
        Refresh();
        try
        {
            if (plan.Items.Any(item => item.ExistingMod is not null)
                && App.Current.Services.Settings.Current.BackupSaveBeforeUpdate)
            {
                await App.Current.Services.Saves.BackupForLaunchAsync();
            }

            var result = await _packages.InstallAsync(plan);
            FeedbackMessage = $"安装完成：成功 {result.InstalledCount}，失败 {result.FailedCount}。";
            PendingPlan = null;
            IsBusy = false;
            await ScanAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("安装未完成", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task ClearPackagePlanAsync()
    {
        if (PendingPlan is { } plan)
        {
            await _packages.CleanupPreparedPackageAsync(plan);
        }

        PendingPlan = null;
        FeedbackMessage = "已取消安装计划。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task BrowseAsync(string? feed)
    {
        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在加载 Nexus 模组...";
        Refresh();
        try
        {
            _loadedOnlineMods = (feed switch
            {
                "最新" => await _nexus.GetLatestAddedAsync(key),
                "最近更新" => await _nexus.GetLatestUpdatedAsync(key),
                _ => await _nexus.GetTrendingAsync(key)
            }).ToList();
            _favoriteValues = await _favoritesService.LoadAsync();
            if (Categories.Count == 0)
            {
                Categories.Add(new NexusCategory { CategoryId = 0, Name = "全部分类" });
                foreach (var category in await _nexus.GetCategoriesAsync(key))
                {
                    Categories.Add(category);
                }
                SelectedCategory = Categories[0];
            }
            foreach (var mod in _loadedOnlineMods)
            {
                mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);
            }

            OnlinePanelTitle = feed ?? "趋势";
            OnlinePanelVisible = true;
            ApplyOnlineFilter();
            OnPropertyChanged(nameof(RateLimitText));
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
        IEnumerable<NexusModInfo> values = _loadedOnlineMods.Where(mod => (SelectedCategory is null || SelectedCategory.CategoryId == 0 || mod.CategoryId == SelectedCategory.CategoryId)
            && (string.IsNullOrWhiteSpace(query)
            || mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || mod.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || mod.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        values = _onlineSort switch
        {
            "最多支持" => values.OrderByDescending(mod => mod.Endorsements),
            "最多下载" => values.OrderByDescending(mod => mod.Downloads),
            "最近更新" => values.OrderByDescending(mod => mod.UpdatedTimestamp),
            _ => values
        };
        OnlineMods.Clear();
        foreach (var item in values)
        {
            OnlineMods.Add(item);
        }

        FeedbackMessage = string.IsNullOrWhiteSpace(query)
            ? "在线列表已加载。"
            : "当前搜索基于已加载的 Nexus 列表筛选。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private void SetOnlineSort(string? sort)
    {
        _onlineSort = sort ?? "默认";
        ApplyOnlineFilter();
    }

    private async Task ShowFavoritesAsync()
    {
        _favoriteValues = await _favoritesService.LoadAsync();
        Favorites.Clear();
        foreach (var favorite in _favoriteValues)
        {
            Favorites.Add(favorite);
        }

        OnlinePanelTitle = "收藏";
        OnlinePanelVisible = true;
    }

    private async Task ToggleFavoriteAsync()
    {
        if (SelectedOnlineMod is not { } mod)
        {
            return;
        }

        _favoriteValues = await _favoritesService.LoadAsync();
        await _favoritesService.ToggleAsync(mod, _favoriteValues);
        mod.IsFavorite = _favoriteValues.Any(value => value.ModId == mod.ModId);
        FeedbackMessage = mod.IsFavorite ? "已加入收藏。" : "已取消收藏。";
        await ShowFavoritesAsync();
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private Task OpenNexusAsync()
    {
        return SelectedOnlineMod is null
            ? Task.CompletedTask
            : _platform.OpenUriAsync($"https://www.nexusmods.com/stardewvalley/mods/{SelectedOnlineMod.ModId}");
    }

    private async Task LoadFilesAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedOnlineMod is not { } mod)
        {
            return;
        }

        try
        {
            OnlineFiles.Clear();
            foreach (var file in await _nexus.GetFilesAsync(mod.ModId, key))
            {
                OnlineFiles.Add(file);
            }

            SelectedOnlineFile = OnlineFiles.FirstOrDefault();
            OnPropertyChanged(nameof(RateLimitText));
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("获取文件失败", exception.Message);
        }
    }

    private async Task DownloadSelectedFileAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedOnlineMod is not { } mod || SelectedOnlineFile is not { } file)
        {
            return;
        }

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
        CancelDownloadCommand.NotifyCanExecuteChanged();
        IsBusy = true;
        ProgressText = "正在下载模组...";
        Refresh();
        try
        {
            var path = await _downloadsService.DownloadAsync(item, key, _downloadCancellation.Token);
            await SaveDownloadQueueAsync();
            IsBusy = false;
            ProgressText = string.Empty;
            await InspectPackageAsync(path);
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Canceled;
            FeedbackMessage = "下载已取消。";
            await SaveDownloadQueueAsync();
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (Exception exception)
        {
            await SaveDownloadQueueAsync();
            await _dialogs.ShowMessageAsync("下载未完成", exception.Message);
        }
        finally
        {
            _isDownloading = false;
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            CancelDownloadCommand.NotifyCanExecuteChanged();
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private void CancelDownload()
    {
        _downloadCancellation?.Cancel();
    }

    private async Task ShowDownloadsAsync()
    {
        DownloadQueue.Clear();
        if (File.Exists(AppPaths.DownloadQueueFile))
        {
            foreach (var item in await JsonHelper.ReadAsync<List<DownloadQueueItem>>(AppPaths.DownloadQueueFile) ?? [])
            {
                DownloadQueue.Add(item);
            }
        }

        OnlinePanelTitle = "下载";
        OnlinePanelVisible = true;
    }

    private async Task SaveDownloadQueueAsync()
    {
        await JsonHelper.WriteAsync(AppPaths.DownloadQueueFile, DownloadQueue.ToList());
    }

    private async Task CheckUpdatesAsync()
    {
        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在检查已绑定模组的更新...";
        Refresh();
        try
        {
            foreach (var mod in _allMods.Where(mod => !mod.IsArchived && mod.NexusModId is not null))
            {
                var remote = await _nexus.GetModAsync(mod.NexusModId!.Value, key);
                mod.RemoteVersion = remote.Version;
            }

            ApplyFilter(_filter);
            OnPropertyChanged(nameof(UpdateCount));
            FeedbackMessage = $"更新检查完成，发现 {_allMods.Count(mod => mod.HasUpdate)} 个可更新模组。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("更新检查", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task PrepareSelectedUpdateAsync()
    {
        var key = RequireNexusKey();
        if (key is null || SelectedMod?.NexusModId is not { } modId)
        {
            return;
        }

        try
        {
            SelectedOnlineMod = await _nexus.GetModAsync(modId, key);
            _loadedOnlineMods = [SelectedOnlineMod];
            OnlineMods.Clear();
            OnlineMods.Add(SelectedOnlineMod);
            OnlinePanelTitle = "更新文件";
            OnlinePanelVisible = true;
            await LoadFilesAsync();
        }
        catch (NexusApiException exception)
        {
            await _dialogs.ShowMessageAsync("准备更新失败", exception.Message);
        }
    }

    public void NotifyFeedbackMessage(string message)
    {
        FeedbackMessage = message;
    }

    private void HandleNexusIdClick()
    {
        if (SelectedMod?.NexusModId is { } id)
        {
            _platform.CopyText(id.ToString());
            FeedbackMessage = "Nexus Mod ID 已复制。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        else
        {
            ToggleNexusBindingEditor();
        }
    }

    private async Task BindNexusIdAsync()
    {
        if (SelectedMod is null || !long.TryParse(NexusModIdInput.Trim(), out var id) || id <= 0)
        {
            FeedbackMessage = "请输入有效的 Nexus Mod ID。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        SelectedMod.NexusModId = id;
        if (SelectedMod.Manifest?.UniqueID is { Length: > 0 } uniqueId)
        {
            await App.Current.Services.Settings.UpdateAsync(settings => settings.NexusBindings[uniqueId] = id);
        }

        FeedbackMessage = "更新来源绑定已保存。";
        _isEditingNexusBinding = false;
        OnPropertyChanged(nameof(FeedbackMessage));
        OnPropertyChanged(nameof(SelectedMod));
        OnPropertyChanged(nameof(NexusBindingEditorVisibility));
        OnPropertyChanged(nameof(NexusBoundVisibility));
        OnPropertyChanged(nameof(CancelNexusBindingVisibility));
        OnPropertyChanged(nameof(NexusBindingText));
        OnPropertyChanged(nameof(NexusIdDisplayText));
    }

    private void ToggleNexusBindingEditor()
    {
        _isEditingNexusBinding = !_isEditingNexusBinding;
        NexusModIdInput = SelectedMod?.NexusModId?.ToString() ?? string.Empty;
        OnPropertyChanged(nameof(NexusBindingEditorVisibility));
        OnPropertyChanged(nameof(NexusBoundVisibility));
        OnPropertyChanged(nameof(CancelNexusBindingVisibility));
    }

    private async Task ExportFavoritesAsync()
    {
        var path = await _platform.ChooseJsonSavePathAsync("LSMA-Favorites");
        if (path is null)
        {
            return;
        }

        await JsonHelper.WriteAsync(path, await _favoritesService.LoadAsync());
        FeedbackMessage = "收藏已导出。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task ImportFavoritesAsync()
    {
        var path = await _platform.ChooseJsonAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            _favoriteValues = await JsonHelper.ReadAsync<List<NexusFavorite>>(path) ?? [];
            await _favoritesService.SaveAsync(_favoriteValues.GroupBy(item => item.ModId).Select(group => group.First()).ToList());
            await ShowFavoritesAsync();
            FeedbackMessage = "收藏已导入。";
            OnPropertyChanged(nameof(FeedbackMessage));
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("导入失败", exception.Message);
        }
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

    private async Task RunModificationAsync(Func<Task> action, string successText)
    {
        IsBusy = true;
        ProgressText = "正在自动备份并执行操作...";
        Refresh();
        try
        {
            await action();
            FeedbackMessage = successText;
            IsBusy = false;
            await ScanAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("操作未完成", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private void NotifyCommands()
    {
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();
        OpenModFolderCommand.NotifyCanExecuteChanged();
        NexusIdClickCommand.NotifyCanExecuteChanged();
        ChoosePackageCommand.NotifyCanExecuteChanged();
        InstallPackageCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OpenNexusCommand.NotifyCanExecuteChanged();
        LoadFilesCommand.NotifyCanExecuteChanged();
        DownloadFileCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        BindNexusIdCommand.NotifyCanExecuteChanged();
        EditNexusBindingCommand.NotifyCanExecuteChanged();
        PrepareSelectedUpdateCommand.NotifyCanExecuteChanged();
    }

    private bool CanModify() => _state.IsGameConfigured && !_state.IsGameRunning && !IsBusy;
    private bool CanInstallPackage() => CanModify() && PendingPlan is { CanInstall: true };
    private bool CanUseOnline(string? _) => !IsBusy;
    private bool CanUseOnlineNow() => !IsBusy;
    private bool HasSelected() => SelectedMod is not null && !IsBusy;
    private bool CanModifySelected() => HasSelected() && !_state.IsGameRunning && SelectedMod is { IsArchived: false };
    private bool CanEnable() => CanModifySelected() && SelectedMod is { IsEnabled: false };
    private bool CanDisable() => CanModifySelected() && SelectedMod is { IsEnabled: true };
}
