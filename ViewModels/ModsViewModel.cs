using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;
using LSMA.Pages;
namespace LSMA.ViewModels;

public sealed class ModsViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly GameRunLockService _runLock;
    private readonly ModScannerService _scanner;
    private readonly ModAnalyzerService _analyzer;
    private readonly ModTranslationService _translations;
    private readonly ModBackupService _backups;
    private readonly ModTransactionService _transactions;
    private readonly ModPackageService _packages;
    private readonly ModPackService _modPacks;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly NexusCoverCacheService _coverCache;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly AutomaticScanMonitor _automaticScanMonitor;
    private List<ModInfo> _allMods = [];
    private ModSummaryCounts _summaryCounts;
    private bool _summaryCountsDirty = true;
    private ModInfo? _selectedMod;
    private string _filter = "全部";
    private ModInstallPlan? _pendingPlan;
    private ModPackInfo? _selectedModPack;
    private string _nexusModIdInput = string.Empty;
    private bool _isEditingNexusBinding;
    private bool _automaticScanningStarted;
    private bool _isModPackPanelOpen;

    public ModsViewModel(
        AppStateService state,
        GameRunLockService runLock,
        ModScannerService scanner,
        ModAnalyzerService analyzer,
        ModTranslationService translations,
        ModBackupService backups,
        ModTransactionService transactions,
        ModPackageService packages,
        ModPackService modPacks,
        NexusCredentialService credentials,
        NexusClient nexus,
        NexusFavoriteService favoritesService,
        NexusCoverCacheService coverCache,
        PlatformService platform,
        DialogService dialogs,
        UiDispatcherService dispatcher)
    {
        _state = state;
        _runLock = runLock;
        _scanner = scanner;
        _analyzer = analyzer;
        _translations = translations;
        _backups = backups;
        _transactions = transactions;
        _packages = packages;
        _modPacks = modPacks;
        _credentials = credentials;
        _nexus = nexus;
        _favoritesService = favoritesService;
        _coverCache = coverCache;
        _platform = platform;
        _dialogs = dialogs;
        _automaticScanMonitor = new AutomaticScanMonitor(dispatcher, ScanAutomaticallyAsync);
        FilterCommand = new RelayCommand<string>(ApplyFilter);
        EnableCommand = new AsyncRelayCommand(() => ChangeEnabledAsync(true), CanEnable);
        DisableCommand = new AsyncRelayCommand(() => ChangeEnabledAsync(false), CanDisable);
        BackupCommand = new AsyncRelayCommand(BackupAsync, HasSelected);
        OpenModFolderCommand = new AsyncRelayCommand(OpenModFolderAsync, HasSelected);
        OpenBackupsCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(AppPaths.ModBackups));
        ChoosePackageCommand = new RelayCommand(() => App.Current.Services.Navigation.Navigate(typeof(DownloadsPage)));
        InstallPackageCommand = new AsyncRelayCommand(InstallPackageAsync, CanInstallPackage);
        CancelPackageCommand = new AsyncRelayCommand(ClearPackagePlanAsync);
        ToggleModPackPanelCommand = new AsyncRelayCommand(ToggleModPackPanelAsync, CanUseModPacks);
        CreateModPackCommand = new AsyncRelayCommand(CreateModPackAsync, CanModifyModPacks);
        CaptureCurrentModsCommand = new AsyncRelayCommand(CaptureCurrentModsAsync, CanModifySelectedModPack);
        RenameModPackCommand = new AsyncRelayCommand(RenameModPackAsync, CanModifySelectedModPack);
        SwitchModPackCommand = new AsyncRelayCommand(SwitchModPackAsync, CanSwitchModPack);
        ImportModPackCommand = new AsyncRelayCommand(ImportModPackAsync, CanModifyModPacks);
        MergeModPackCommand = new AsyncRelayCommand(MergeModPackAsync, CanModifySelectedModPack);
        ExportModPackWithFilesCommand = new AsyncRelayCommand(() => ExportModPackAsync(true), HasSelectedModPack);
        ExportModPackMetadataCommand = new AsyncRelayCommand(() => ExportModPackAsync(false), HasSelectedModPack);
        DownloadMissingModPackFilesCommand = new AsyncRelayCommand(DownloadMissingModPackFilesAsync, CanDownloadMissingModPackFiles);
        InstallMissingDependencyCommand = new AsyncRelayCommand<MissingDependencyAction?>(InstallMissingDependencyAsync, dependency => dependency?.CanDownload == true && CanModify());
        DeleteModPackCommand = new AsyncRelayCommand(DeleteModPackAsync, CanDeleteModPack);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, CanUseOnlineNow);
        PrepareSelectedUpdateCommand = new AsyncRelayCommand(PrepareSelectedUpdateAsync, CanPrepareSelectedUpdate);
        OpenSelectedNexusPageCommand = new AsyncRelayCommand(OpenSelectedNexusPageAsync, CanOpenSelectedNexusPage);
        OpenSelectedAuthorPageCommand = new AsyncRelayCommand(OpenSelectedAuthorPageAsync, HasSelected);
        BindNexusIdCommand = new AsyncRelayCommand(BindNexusIdAsync, () => SelectedMod is not null);
        EditNexusBindingCommand = new RelayCommand(ToggleNexusBindingEditor, () => SelectedMod is not null);
        NexusIdClickCommand = new RelayCommand(HandleNexusIdClick, () => SelectedMod is not null);
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

    public ObservableCollection<ModInfo> Mods { get; } = new RangeObservableCollection<ModInfo>();
    public ObservableCollection<ModPackInfo> ModPacks { get; } = new RangeObservableCollection<ModPackInfo>();
    public ObservableCollection<ModPackEntry> ModPackEntries { get; } = new RangeObservableCollection<ModPackEntry>();
    public ObservableCollection<MissingDependencyAction> MissingDependencies { get; } = new RangeObservableCollection<MissingDependencyAction>();
    public IRelayCommand<string> FilterCommand { get; }
    public IAsyncRelayCommand EnableCommand { get; }
    public IAsyncRelayCommand DisableCommand { get; }
    public IAsyncRelayCommand BackupCommand { get; }
    public IAsyncRelayCommand OpenModFolderCommand { get; }
    public IRelayCommand NexusIdClickCommand { get; }
    public IAsyncRelayCommand OpenBackupsCommand { get; }
    public IRelayCommand ChoosePackageCommand { get; }
    public IAsyncRelayCommand InstallPackageCommand { get; }
    public IAsyncRelayCommand CancelPackageCommand { get; }
    public IAsyncRelayCommand ToggleModPackPanelCommand { get; }
    public IAsyncRelayCommand CreateModPackCommand { get; }
    public IAsyncRelayCommand CaptureCurrentModsCommand { get; }
    public IAsyncRelayCommand RenameModPackCommand { get; }
    public IAsyncRelayCommand SwitchModPackCommand { get; }
    public IAsyncRelayCommand ImportModPackCommand { get; }
    public IAsyncRelayCommand MergeModPackCommand { get; }
    public IAsyncRelayCommand ExportModPackWithFilesCommand { get; }
    public IAsyncRelayCommand ExportModPackMetadataCommand { get; }
    public IAsyncRelayCommand DownloadMissingModPackFilesCommand { get; }
    public IAsyncRelayCommand<MissingDependencyAction?> InstallMissingDependencyCommand { get; }
    public IAsyncRelayCommand DeleteModPackCommand { get; }
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand PrepareSelectedUpdateCommand { get; }
    public IAsyncRelayCommand OpenSelectedNexusPageCommand { get; }
    public IAsyncRelayCommand OpenSelectedAuthorPageCommand { get; }
    public IAsyncRelayCommand BindNexusIdCommand { get; }
    public IRelayCommand EditNexusBindingCommand { get; }
    public ModInfo? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (ReferenceEquals(_selectedMod, value))
            {
                return;
            }

            if (_selectedMod is not null)
            {
                _selectedMod.IsSelected = false;
            }

            if (SetProperty(ref _selectedMod, value))
            {
                if (value is not null)
                {
                    value.IsSelected = true;
                }

                NexusModIdInput = value?.NexusModId?.ToString() ?? string.Empty;
                _isEditingNexusBinding = false;
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(NexusBindingEditorVisibility));
                OnPropertyChanged(nameof(NexusBoundVisibility));
                OnPropertyChanged(nameof(CancelNexusBindingVisibility));
                OnPropertyChanged(nameof(NexusBindingText));
                OnPropertyChanged(nameof(EnableVisibility));
                OnPropertyChanged(nameof(DisableVisibility));
                OnPropertyChanged(nameof(NexusIdDisplayText));
                OnPropertyChanged(nameof(OpenNexusPageVisibility));
                OnPropertyChanged(nameof(DependencySectionVisibility));
                OnPropertyChanged(nameof(IssuesSectionVisibility));
                OnPropertyChanged(nameof(NoNexusIdMessageVisibility));
                NotifyCommands();
            }
        }
    }

    public ModPackInfo? SelectedModPack
    {
        get => _selectedModPack;
        set
        {
            if (SetProperty(ref _selectedModPack, value))
            {
                RefreshModPackEntries();
                OnPropertyChanged(nameof(SelectedModPackVisibility));
                OnPropertyChanged(nameof(SelectedModPackTitle));
                OnPropertyChanged(nameof(SelectedModPackStatus));
                NotifyCommands();
            }
        }
    }

    public string NexusModIdInput
    {
        get => _nexusModIdInput;
        set => SetProperty(ref _nexusModIdInput, value);
    }

    public Visibility UnavailableVisibility => _state.IsGameConfigured ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AvailableVisibility => _state.IsGameConfigured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => _state.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => SelectedMod is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NexusBindingEditorVisibility => SelectedMod is not null && _isEditingNexusBinding
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility NexusBoundVisibility => SelectedMod?.NexusModId is not null && !_isEditingNexusBinding
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CancelNexusBindingVisibility => SelectedMod?.NexusModId is not null && _isEditingNexusBinding
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility IssuesSectionVisibility => SelectedMod?.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoNexusIdMessageVisibility => SelectedMod?.NexusModId is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EnableVisibility => SelectedMod is { IsEnabled: false, IsArchived: false } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DisableVisibility => SelectedMod is { IsEnabled: true, IsArchived: false } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenNexusPageVisibility => SelectedMod?.NexusModId is null ? Visibility.Collapsed : Visibility.Visible;
    public string NexusBindingText => SelectedMod?.NexusModId is { } id ? $"Nexus Mod ID：{id}" : "未绑定 Nexus Mod ID";
    public string NexusIdDisplayText => SelectedMod?.NexusModId is { } id ? $"ID：{id}" : "手动匹配ID";
    public Visibility DependencySectionVisibility => SelectedMod?.HasRequiredDependencies == true ? Visibility.Visible : Visibility.Collapsed;
    public string InstalledCount => SummaryCounts.Installed.ToString();
    public string HealthyCount => SummaryCounts.Healthy.ToString();
    public string ProblemCount => SummaryCounts.Problem.ToString();
    public string DisabledCount => SummaryCounts.Disabled.ToString();
    public string MissingDependencyCount => SummaryCounts.MissingDependency.ToString();
    public string FavoriteCount => SummaryCounts.Favorite.ToString();
    public string UpdateCount => SummaryCounts.Update.ToString();
    private ModSummaryCounts SummaryCounts
    {
        get
        {
            if (_summaryCountsDirty)
            {
                _summaryCounts = ModSummaryCounts.Create(_allMods);
                _summaryCountsDirty = false;
            }

            return _summaryCounts;
        }
    }
    public string CurrentFilter => _filter;
    public bool HasProblems => SummaryCounts.Problem > 0;
    public Visibility FilterAndListVisibility => AvailableVisibility == Visibility.Visible && PlanVisibility == Visibility.Collapsed
        && !_isModPackPanelOpen
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModPackPanelVisibility => AvailableVisibility == Visibility.Visible && PlanVisibility == Visibility.Collapsed
        && _isModPackPanelOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility SelectedModPackVisibility => SelectedModPack is null ? Visibility.Collapsed : Visibility.Visible;
    public string SelectedModPackTitle => SelectedModPack?.Name ?? "未选择模组包";
    public string SelectedModPackStatus => SelectedModPack?.StatusText ?? "请选择一个模组包";
    public string TaskStatus => IsBusy ? ProgressText : $"当前筛选：{_filter}，显示 {Mods.Count} 个模组";
    public ModInstallPlan? PendingPlan
    {
        get => _pendingPlan;
        private set
        {
            if (SetProperty(ref _pendingPlan, value))
            {
                OnPropertyChanged(nameof(PlanVisibility));
                OnPropertyChanged(nameof(FilterAndListVisibility));
                OnPropertyChanged(nameof(ModPackPanelVisibility));
                OnPropertyChanged(nameof(MissingDependencyPanelVisibility));
                InstallPackageCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public Visibility PlanVisibility => PendingPlan is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MissingDependencyPanelVisibility => MissingDependencies.Count > 0 && PendingPlan is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void Refresh()
    {
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(FilterAndListVisibility));
        OnPropertyChanged(nameof(ModPackPanelVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(MissingDependencyPanelVisibility));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(HealthyCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(TaskStatus));
        OnPropertyChanged(nameof(SelectedModPackStatus));
        NotifyCommands();
    }

    public async Task StartAutomaticScanningAsync()
    {
        _automaticScanningStarted = true;
        ConfigureAutomaticWatchers();
        await ScanAsync();
    }

    public Task ScanForLaunchAsync() => ScanAsync();

    public async Task RefreshTranslationsAsync()
    {
        var selectedPath = SelectedMod?.FolderPath;
        await _translations.ApplyAsync(_allMods);
        ApplyFilter(_filter);
        SelectedMod = Mods.FirstOrDefault(mod => string.Equals(mod.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? Mods.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedMod));
    }

    private async Task ToggleModPackPanelAsync()
    {
        if (_isModPackPanelOpen)
        {
            _isModPackPanelOpen = false;
            Refresh();
            return;
        }

        if (PendingPlan is not null)
        {
            await ClearPackagePlanAsync();
        }

        _isModPackPanelOpen = true;
        await LoadModPacksAsync(ensureInitialized: true);
        Refresh();
    }

    private async Task LoadModPacksAsync(bool ensureInitialized)
    {
        var selectedId = SelectedModPack?.Id;
        var catalog = ensureInitialized
            ? await _modPacks.EnsureInitializedAsync()
            : await _modPacks.LoadAsync();
        SyncModPacks(catalog, selectedId);
    }

    private void SyncModPacks(ModPackCatalog catalog, string? preferredPackId = null)
    {
        ReplaceCollection(ModPacks, catalog.Packs
            .OrderByDescending(pack => pack.IsActive)
            .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase));

        SelectedModPack = ModPacks.FirstOrDefault(pack => pack.Id == preferredPackId)
            ?? ModPacks.FirstOrDefault(pack => pack.IsActive)
            ?? ModPacks.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedModPackStatus));
    }

    private void RefreshModPackEntries()
    {
        if (SelectedModPack is null)
        {
            ReplaceCollection(ModPackEntries, []);
            return;
        }

        var entries = SelectedModPack.Entries
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var entry in entries)
        {
            FillActiveModPackNexusId(entry);
        }

        ReplaceCollection(ModPackEntries, entries);
    }

    private void FillActiveModPackNexusId(ModPackEntry entry)
    {
        if (entry.NexusModId is not null || SelectedModPack?.IsActive != true)
        {
            return;
        }

        var current = _allMods.FirstOrDefault(mod =>
            !mod.IsArchived
            && !string.IsNullOrWhiteSpace(entry.UniqueId)
            && string.Equals(mod.Manifest?.UniqueID, entry.UniqueId, StringComparison.OrdinalIgnoreCase))
            ?? _allMods.FirstOrDefault(mod =>
                !mod.IsArchived
                && !string.IsNullOrWhiteSpace(entry.FolderName)
                && string.Equals(mod.FolderName, entry.FolderName, StringComparison.OrdinalIgnoreCase));

        if (current?.NexusModId is { } nexusModId)
        {
            entry.NexusModId = nexusModId;
        }
    }

    private async Task CreateModPackAsync()
    {
        var name = await _dialogs.PromptTextAsync("新建空包", "输入模组包名称", "新模组包", "创建");
        if (name is null)
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.CreateEmptyPackAsync(name),
            "已创建空模组包。",
            selectNewestWhenNoPreferred: true);
    }

    private async Task RenameModPackAsync()
    {
        if (SelectedModPack is not { } pack)
        {
            return;
        }

        var name = await _dialogs.PromptTextAsync("重命名模组包", "输入新的模组包名称", pack.Name, "保存");
        if (name is null)
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.RenameAsync(pack.Id, name),
            "已重命名模组包。",
            pack.Id);
    }

    private async Task CaptureCurrentModsAsync()
    {
        if (SelectedModPack is not { } pack
            || !await _dialogs.ConfirmAsync("复制当前 Mods", $"将用当前游戏 Mods 目录覆盖“{pack.Name}”的包内容，继续？", "复制"))
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.CaptureCurrentModsAsync(pack.Id),
            "已复制当前 Mods 到模组包。",
            pack.Id);
    }

    private async Task SwitchModPackAsync()
    {
        if (SelectedModPack is not { } pack
            || !await _dialogs.ConfirmAsync("切换模组包", $"将把当前 Mods 移回当前包，并加载“{pack.Name}”。游戏未运行时才能执行。", "切换"))
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.SwitchAsync(pack.Id),
            $"已切换到模组包：{pack.Name}。",
            pack.Id);
        await ScanAsync();
    }

    private async Task ImportModPackAsync()
    {
        var path = await _platform.ChooseModPackAsync();
        if (path is null)
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.ImportAsync(path, null, _credentials.GetKey()),
            "已导入为新的模组包。",
            selectNewestWhenNoPreferred: true);
    }

    private async Task MergeModPackAsync()
    {
        if (SelectedModPack is not { } pack)
        {
            return;
        }

        var path = await _platform.ChooseModPackAsync();
        if (path is null)
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.ImportAsync(path, pack.Id, _credentials.GetKey()),
            $"已合并到模组包：{pack.Name}。",
            pack.Id);
        if (pack.IsActive)
        {
            await ScanAsync();
        }
    }

    private async Task ExportModPackAsync(bool includeFiles)
    {
        if (SelectedModPack is not { } pack)
        {
            return;
        }

        var suggested = FileSystemHelper.SafeFilePart(pack.Name);
        var path = await _platform.ChooseModPackSavePathAsync(suggested);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ProgressText = includeFiles ? "正在导出带文件模组包..." : "正在导出轻量模组包...";
        Refresh();
        try
        {
            await _modPacks.ExportAsync(pack.Id, path, new ModPackExportOptions { IncludeModFiles = includeFiles });
            FeedbackMessage = includeFiles ? "已导出带文件模组包。" : "已导出轻量模组包。";
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("导出失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task DownloadMissingModPackFilesAsync()
    {
        if (SelectedModPack is not { } pack)
        {
            return;
        }

        var key = RequireNexusKey();
        if (key is null)
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.DownloadMissingAsync(pack.Id, key),
            $"已处理“{pack.Name}”的缺失模组文件。",
            pack.Id);
        if (pack.IsActive)
        {
            await ScanAsync();
        }
    }

    private async Task DeleteModPackAsync()
    {
        if (SelectedModPack is not { } pack
            || !await _dialogs.ConfirmAsync("删除模组包", $"将删除“{pack.Name}”的包记录和仓库文件，继续？", "删除"))
        {
            return;
        }

        await RunModPackOperationAsync(
            () => _modPacks.DeleteAsync(pack.Id),
            $"已删除模组包：{pack.Name}。");
    }

    private async Task RunModPackOperationAsync(
        Func<Task<ModPackCatalog>> operation,
        string successText,
        string? preferredPackId = null,
        bool selectNewestWhenNoPreferred = false)
    {
        IsBusy = true;
        ProgressText = "正在处理模组包...";
        Refresh();
        try
        {
            var catalog = await operation();
            var selectedId = preferredPackId;
            if (selectedId is null && selectNewestWhenNoPreferred)
            {
                selectedId = catalog.Packs.OrderByDescending(pack => pack.CreatedAt).FirstOrDefault()?.Id;
            }

            SyncModPacks(catalog, selectedId);
            FeedbackMessage = successText;
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("模组包操作失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

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
            SetMissingDependencies(PendingPlan.MissingDependencies);
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
            _summaryCountsDirty = true;
            await _translations.ApplyAsync(_allMods);
            _state.Mods = _allMods;
            await SyncFavoritesAsync();
            await _coverCache.ApplyCachedAndQueueAsync(_allMods);
            ApplyFilter(_filter);
            SelectedMod = Mods.FirstOrDefault(mod => string.Equals(mod.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? Mods.FirstOrDefault();
            if (_isModPackPanelOpen)
            {
                RefreshModPackEntries();
            }

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
            "收藏" => mod.IsFavorite,
            "可更新" => mod.HasUpdate,
            _ => true
        });
        ReplaceCollection(Mods, items);

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

    private async Task SyncFavoritesAsync()
    {
        try
        {
            var favorites = await _favoritesService.LoadAsync();
            foreach (var mod in _allMods)
            {
                mod.IsFavorite = mod.NexusModId is { } id && favorites.Any(f => f.ModId == id);
            }

            _summaryCountsDirty = true;
        }
        catch
        {
            // Silently ignore favorites sync errors
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
            || !await _dialogs.ConfirmAsync("执行安装计划", CreateInstallConfirmation(plan), "开始安装"))
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
            SetMissingDependencies(plan.MissingDependencies);
            FeedbackMessage = plan.MissingDependencies.Count > 0
                ? $"安装完成：成功 {result.InstalledCount}，失败 {result.FailedCount}。仍缺少 {plan.MissingDependencies.Count} 个前置。"
                : $"安装完成：成功 {result.InstalledCount}，失败 {result.FailedCount}。";
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
        SetMissingDependencies(Array.Empty<MissingDependencyAction>());
        FeedbackMessage = "已取消安装计划。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task InstallMissingDependencyAsync(MissingDependencyAction? dependency)
    {
        if (dependency?.NexusModId is not { } modId)
        {
            FeedbackMessage = "该前置未绑定 Nexus ID，请在下载页手动搜索。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        var installed = await App.Current.Services.Downloads.InstallModByIdAsync(modId, dependency.UniqueId);
        if (installed)
        {
            var nextDependencies = MissingDependencies
                .Where(value => !value.UniqueId.Equals(dependency.UniqueId, StringComparison.OrdinalIgnoreCase))
                .Concat(App.Current.Services.Downloads.MissingDependencies)
                .GroupBy(value => value.UniqueId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            SetMissingDependencies(nextDependencies);
            OnPropertyChanged(nameof(MissingDependencyPanelVisibility));
            await ScanAsync();
        }

        NotifyCommands();
    }

    private static string CreateInstallConfirmation(ModInstallPlan plan)
    {
        var message = $"将安装或更新 {plan.Items.Count} 个模组。已有版本会自动备份，旧配置会尽量保留。";
        return plan.MissingDependencies.Count == 0
            ? message
            : $"{message}{Environment.NewLine}检测到缺少前置：{string.Join("、", plan.MissingDependencies.Select(dependency => dependency.UniqueId))}。仍会安装当前模组，补齐前置前游戏可能无法启动。";
    }

    private void SetMissingDependencies(IEnumerable<MissingDependencyAction> dependencies)
    {
        ReplaceCollection(MissingDependencies, dependencies);

        OnPropertyChanged(nameof(MissingDependencyPanelVisibility));
        InstallMissingDependencyCommand.NotifyCanExecuteChanged();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        if (destination is RangeObservableCollection<T> range)
        {
            range.ReplaceWith(source);
            return;
        }

        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
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

            _summaryCountsDirty = true;
            ApplyFilter(_filter);
            OnPropertyChanged(nameof(UpdateCount));
            FeedbackMessage = $"更新检查完成，发现 {SummaryCounts.Update} 个可更新模组。";
            OnPropertyChanged(nameof(FeedbackMessage));
            App.Current.Services.Home.Refresh();
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
        if (SelectedMod?.NexusModId is not { } modId)
        {
            FeedbackMessage = "该模组未绑定 Nexus Mod ID。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        App.Current.Services.Navigation.Navigate(typeof(DownloadsPage));
        await App.Current.Services.Downloads.FocusModAsync(modId);
    }

    private async Task OpenSelectedNexusPageAsync()
    {
        if (SelectedMod?.NexusModId is not { } modId)
        {
            FeedbackMessage = "该模组未绑定 Nexus Mod ID。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        try
        {
            await _platform.OpenUriAsync($"https://www.nexusmods.com/stardewvalley/mods/{modId}");
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("打开N网", exception.Message);
        }
    }

    private async Task OpenSelectedAuthorPageAsync()
    {
        if (SelectedMod is not { } mod)
        {
            return;
        }

        var author = CleanNexusAuthorName(await ResolveNexusAuthorNameAsync(mod))
            ?? CleanNexusAuthorName(mod.Author);
        if (author is null)
        {
            FeedbackMessage = "该模组没有可跳转的作者名。";
            OnPropertyChanged(nameof(FeedbackMessage));
            return;
        }

        try
        {
            await _platform.OpenUriAsync(CreateNexusAuthorPageUrl(author));
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("打开作者页", exception.Message);
        }
    }

    private async Task<string?> ResolveNexusAuthorNameAsync(ModInfo mod)
    {
        if (mod.NexusModId is not { } modId)
        {
            return null;
        }

        try
        {
            if (await _nexus.GetModFromGraphQlAsync(modId) is { Author.Length: > 0 } graphMod)
            {
                return graphMod.Author;
            }
        }
        catch
        {
            // Fall back to the local manifest author below; author links are non-critical.
        }

        var key = _credentials.GetKey();
        if (key is null)
        {
            return null;
        }

        try
        {
            return (await _nexus.GetModAsync(modId, key)).Author;
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanNexusAuthorName(string? value)
    {
        var author = value?.Trim();
        return string.IsNullOrWhiteSpace(author)
            || string.Equals(author, "未知作者", StringComparison.OrdinalIgnoreCase)
            || string.Equals(author, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? null
                : author;
    }

    private static string CreateNexusAuthorPageUrl(string author)
        => $"https://next.nexusmods.com/profile/{Uri.EscapeDataString(author.Trim())}/mods";

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
        OnPropertyChanged(nameof(NoNexusIdMessageVisibility));
        OnPropertyChanged(nameof(OpenNexusPageVisibility));
        OpenSelectedNexusPageCommand.NotifyCanExecuteChanged();
    }

    private void ToggleNexusBindingEditor()
    {
        _isEditingNexusBinding = !_isEditingNexusBinding;
        NexusModIdInput = SelectedMod?.NexusModId?.ToString() ?? string.Empty;
        OnPropertyChanged(nameof(NexusBindingEditorVisibility));
        OnPropertyChanged(nameof(NexusBoundVisibility));
        OnPropertyChanged(nameof(CancelNexusBindingVisibility));
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
        ToggleModPackPanelCommand.NotifyCanExecuteChanged();
        CreateModPackCommand.NotifyCanExecuteChanged();
        CaptureCurrentModsCommand.NotifyCanExecuteChanged();
        RenameModPackCommand.NotifyCanExecuteChanged();
        SwitchModPackCommand.NotifyCanExecuteChanged();
        ImportModPackCommand.NotifyCanExecuteChanged();
        MergeModPackCommand.NotifyCanExecuteChanged();
        ExportModPackWithFilesCommand.NotifyCanExecuteChanged();
        ExportModPackMetadataCommand.NotifyCanExecuteChanged();
        DownloadMissingModPackFilesCommand.NotifyCanExecuteChanged();
        InstallMissingDependencyCommand.NotifyCanExecuteChanged();
        DeleteModPackCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        PrepareSelectedUpdateCommand.NotifyCanExecuteChanged();
        OpenSelectedNexusPageCommand.NotifyCanExecuteChanged();
        OpenSelectedAuthorPageCommand.NotifyCanExecuteChanged();
        BindNexusIdCommand.NotifyCanExecuteChanged();
        EditNexusBindingCommand.NotifyCanExecuteChanged();
    }

    private bool CanModify() => _state.IsGameConfigured && !_state.IsGameRunning && !IsBusy;
    private bool CanInstallPackage() => CanModify() && PendingPlan is { CanInstall: true };
    private bool CanUseOnlineNow() => !IsBusy;
    private bool CanPrepareSelectedUpdate() => !IsBusy && SelectedMod is { NexusModId: not null, HasUpdate: true };
    private bool CanUseModPacks() => _state.IsGameConfigured && !IsBusy;
    private bool CanModifyModPacks() => CanModify() && _isModPackPanelOpen;
    private bool HasSelectedModPack() => SelectedModPack is not null && !IsBusy;
    private bool CanModifySelectedModPack() => CanModifyModPacks() && SelectedModPack is not null;
    private bool CanSwitchModPack() => CanModifySelectedModPack() && SelectedModPack is { CanSwitch: true };
    private bool CanDownloadMissingModPackFiles() => CanModifySelectedModPack() && SelectedModPack is { MissingCount: > 0 };
    private bool CanDeleteModPack() => CanModifySelectedModPack() && SelectedModPack is { IsActive: false };
    private bool HasSelected() => SelectedMod is not null && !IsBusy;
    private bool CanOpenSelectedNexusPage() => !IsBusy && SelectedMod?.NexusModId is not null;
    private bool CanModifySelected() => HasSelected() && !_state.IsGameRunning && SelectedMod is { IsArchived: false };
    private bool CanEnable() => CanModifySelected() && SelectedMod is { IsEnabled: false };
    private bool CanDisable() => CanModifySelected() && SelectedMod is { IsEnabled: true };

    private readonly record struct ModSummaryCounts(
        int Installed,
        int Healthy,
        int Problem,
        int Disabled,
        int MissingDependency,
        int Favorite,
        int Update)
    {
        public static ModSummaryCounts Create(IReadOnlyList<ModInfo> mods)
        {
            var healthy = 0;
            var problem = 0;
            var disabled = 0;
            var missingDependency = 0;
            var favorite = 0;
            var update = 0;

            foreach (var mod in mods)
            {
                if (mod.IsEnabled && mod.Issues.Count == 0)
                {
                    healthy++;
                }

                if (mod.Issues.Count > 0)
                {
                    problem++;
                }

                if (!mod.IsEnabled && !mod.IsArchived)
                {
                    disabled++;
                }

                if (mod.Issues.Any(issue => issue.Message.Contains("前置", StringComparison.Ordinal)))
                {
                    missingDependency++;
                }

                if (mod.IsFavorite)
                {
                    favorite++;
                }

                if (mod.HasUpdate)
                {
                    update++;
                }
            }

            return new ModSummaryCounts(mods.Count, healthy, problem, disabled, missingDependency, favorite, update);
        }
    }
}
