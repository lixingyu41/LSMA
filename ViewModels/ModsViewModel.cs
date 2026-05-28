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
    private readonly ModBackupService _backups;
    private readonly ModTransactionService _transactions;
    private readonly ModPackageService _packages;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly NexusFavoriteService _favoritesService;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly AutomaticScanMonitor _automaticScanMonitor;
    private List<ModInfo> _allMods = [];
    private ModInfo? _selectedMod;
    private string _filter = "全部";
    private ModInstallPlan? _pendingPlan;
    private string _nexusModIdInput = string.Empty;
    private bool _isEditingNexusBinding;
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
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, CanUseOnlineNow);
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

    public ObservableCollection<ModInfo> Mods { get; } = [];
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
    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand BindNexusIdCommand { get; }
    public IRelayCommand EditNexusBindingCommand { get; }
    public ModInfo? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (SetProperty(ref _selectedMod, value))
            {
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
                OnPropertyChanged(nameof(DependencySectionVisibility));
                OnPropertyChanged(nameof(IssuesSectionVisibility));
                OnPropertyChanged(nameof(NoNexusIdMessageVisibility));
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
    public string NexusBindingText => SelectedMod?.NexusModId is { } id ? $"Nexus Mod ID：{id}" : "未绑定 Nexus Mod ID";
    public string NexusIdDisplayText => SelectedMod?.NexusModId is { } id ? $"ID：{id}" : "手动匹配ID";
    public Visibility DependencySectionVisibility => SelectedMod?.HasRequiredDependencies == true ? Visibility.Visible : Visibility.Collapsed;
    public string InstalledCount => _allMods.Count.ToString();
    public string HealthyCount => _allMods.Count(mod => mod.IsEnabled && mod.Issues.Count == 0).ToString();
    public string ProblemCount => _allMods.Count(mod => mod.Issues.Count > 0).ToString();
    public string DisabledCount => _allMods.Count(mod => !mod.IsEnabled && !mod.IsArchived).ToString();
    public string MissingDependencyCount => _allMods.Count(mod => mod.Issues.Any(issue => issue.Message.Contains("前置", StringComparison.Ordinal))).ToString();
    public string FavoriteCount => _allMods.Count(mod => mod.IsFavorite).ToString();
    public string UpdateCount => _allMods.Count(mod => mod.HasUpdate).ToString();
    public string CurrentFilter => _filter;
    public bool HasProblems => _allMods.Any(mod => mod.Issues.Count > 0);
    public Visibility FilterAndListVisibility => AvailableVisibility == Visibility.Visible && PlanVisibility == Visibility.Collapsed
        ? Visibility.Visible : Visibility.Collapsed;
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
                InstallPackageCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public Visibility PlanVisibility => PendingPlan is null ? Visibility.Collapsed : Visibility.Visible;

    public void Refresh()
    {
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(FilterAndListVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(HealthyCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(UpdateCount));
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
            await SyncFavoritesAsync();
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

    private async Task SyncFavoritesAsync()
    {
        try
        {
            var favorites = await _favoritesService.LoadAsync();
            foreach (var mod in _allMods)
            {
                mod.IsFavorite = mod.NexusModId is { } id && favorites.Any(f => f.ModId == id);
            }
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
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        BindNexusIdCommand.NotifyCanExecuteChanged();
        EditNexusBindingCommand.NotifyCanExecuteChanged();
    }

    private bool CanModify() => _state.IsGameConfigured && !_state.IsGameRunning && !IsBusy;
    private bool CanInstallPackage() => CanModify() && PendingPlan is { CanInstall: true };
    private bool CanUseOnlineNow() => !IsBusy;
    private bool HasSelected() => SelectedMod is not null && !IsBusy;
    private bool CanModifySelected() => HasSelected() && !_state.IsGameRunning && SelectedMod is { IsArchived: false };
    private bool CanEnable() => CanModifySelected() && SelectedMod is { IsEnabled: false };
    private bool CanDisable() => CanModifySelected() && SelectedMod is { IsEnabled: true };
}
