using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly SettingsService _settings;
    private readonly GameLocatorService _locator;
    private readonly GameIconService _icons;
    private readonly GameLaunchService _launcher;
    private readonly GameRunLockService _runLock;
    private readonly SmapiLogService _logs;
    private readonly LastKnownGoodService _lastKnownGood;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private LastKnownGoodSnapshot? _availableSnapshot;

    public HomeViewModel(
        AppStateService state,
        SettingsService settings,
        GameLocatorService locator,
        GameIconService icons,
        GameLaunchService launcher,
        GameRunLockService runLock,
        SmapiLogService logs,
        LastKnownGoodService lastKnownGood,
        PlatformService platform,
        DialogService dialogs)
    {
        _state = state;
        _settings = settings;
        _locator = locator;
        _icons = icons;
        _launcher = launcher;
        _runLock = runLock;
        _logs = logs;
        _lastKnownGood = lastKnownGood;
        _platform = platform;
        _dialogs = dialogs;
        ChooseDirectoryCommand = new AsyncRelayCommand(ChooseDirectoryAsync, CanInteract);
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(true), CanCheck);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, CanLaunch);
        RepairCommand = new AsyncRelayCommand(RepairAsync, CanCheck);
        CopyReportCommand = new RelayCommand(CopyReport);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync);
        OpenLogsCommand = new AsyncRelayCommand(OpenLogsAsync);
        RestoreStableStateCommand = new AsyncRelayCommand(RestoreStableStateAsync, () => _availableSnapshot is not null && !_state.IsGameRunning && !IsBusy);
        _state.PropertyChanged += (_, _) => Refresh();
    }

    public IAsyncRelayCommand ChooseDirectoryCommand { get; }
    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand LaunchGameCommand { get; }
    public IAsyncRelayCommand RepairCommand { get; }
    public IRelayCommand CopyReportCommand { get; }
    public IAsyncRelayCommand ExportReportCommand { get; }
    public IAsyncRelayCommand OpenLogsCommand { get; }
    public IAsyncRelayCommand RestoreStableStateCommand { get; }

    public Visibility MissingDirectoryVisibility => _state.IsGameConfigured ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ConnectedVisibility => _state.IsGameConfigured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => _state.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LogDetailVisibility => _state.LogSummary.HasLog ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReadyVisibility => Health == HealthLevel.Ready ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AttentionVisibility => Health == HealthLevel.Attention ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BlockedVisibility => Health == HealthLevel.Blocked ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StableRecoveryVisibility => _state.LogSummary.HasCrash && _availableSnapshot is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModAttentionVisibility => ModErrorCount > 0 || ModUpdateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LogAttentionVisibility => _state.LogSummary.HasCrash || _state.LogSummary.ErrorCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HomeBackgroundVisibility => Visibility.Visible;
    public string HomeBackgroundImageUri => "ms-appx:///Assets/Home/stardew-valley-steam-library-hero-2x.jpg";
    public string ConnectionText => _state.IsGameConfigured ? "已连接游戏" : "未找到星露谷物语安装目录";
    public string StatusTitle => Health switch
    {
        HealthLevel.Ready => "可以启动",
        HealthLevel.Attention => "建议处理",
        _ => "不建议启动"
    };
    public string StatusDetail => !_state.IsGameConfigured
        ? "尚未连接有效的游戏目录。"
        : _state.HasPendingRecovery
            ? "存在未完成的存档恢复任务，请先在存档页处理。"
        : _state.IsGameRunning
            ? "游戏正在运行，文件修改操作已暂停。"
            : _settings.Current.DefaultLaunchTarget == LaunchTarget.Smapi && !_state.GameDirectory!.HasSmapi
                ? "默认使用 SMAPI，但未检测到 SMAPI 启动程序。"
                    : _state.LogSummary.HasCrash
                    ? "最近一次 SMAPI 日志显示崩溃，请先检查错误。"
                    : _state.CurrentSave is not null && (_state.CurrentSave.LatestBackup is null || _state.CurrentSave.LatestBackup < DateTime.Now.AddDays(-7))
                        ? "当前存档近期没有备份，建议在启动前创建备份。"
                    : _state.LogSummary.WarningCount > 0
                        ? "最近日志包含警告，启动前建议查看摘要。"
                        : _state.Mods.Any(mod => mod.IsEnabled && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Warning))
                            ? "部分模组存在非阻断提醒，建议启动前查看。"
                        : "目录和启动程序就绪，未发现阻断问题。";
    public string ModSummaryText
    {
        get
        {
            var errors = ModErrorCount;
            var updates = ModUpdateCount;
            return (errors, updates) switch
            {
                (> 0, > 0) => $"{errors} 个错误 · {updates} 个可更新",
                (> 0, 0) => $"{errors} 个错误",
                (0, > 0) => $"{updates} 个可更新",
                _ => string.Empty
            };
        }
    }
    public string SaveSummaryText => _state.CurrentSave is null
        ? "未发现本机存档"
        : $"{_state.CurrentSave.FarmerName} · {_state.CurrentSave.DateDisplay}\n{(_state.CurrentSave.LatestBackup is null ? "尚无备份" : $"备份 {_state.CurrentSave.LatestBackup:MM-dd HH:mm}")}";
    public string SuggestionSummaryText => _state.CurrentSave is null
        ? "未发现可分析的存档"
        : App.Current.Services.Guide.Suggestions.FirstOrDefault()?.Title ?? "今日没有特别提醒";
    public string LogSummaryText => _state.LogSummary.DisplaySummary;
    public IReadOnlyList<LogIssue> LogIssues => _state.LogSummary.Issues;
    public string MostLikelyCause => _state.LogSummary.MostLikelyCause;
    public string RecommendedAction => _state.LogSummary.RecommendedAction;

    private int ModErrorCount => _state.Mods.Count(mod => mod.IsEnabled && !mod.IsArchived
        && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Error));

    private int ModUpdateCount => _state.Mods.Count(mod => !mod.IsArchived && mod.HasUpdate);

    private HealthLevel Health
    {
        get
        {
            if (!_state.IsGameConfigured || _state.IsGameRunning || _state.HasPendingRecovery)
            {
                return HealthLevel.Blocked;
            }

            if (_settings.Current.DefaultLaunchTarget == LaunchTarget.Smapi && !_state.GameDirectory!.HasSmapi)
            {
                return HealthLevel.Blocked;
            }

            if (_state.LogSummary.HasCrash || _state.LogSummary.ErrorCount > 0
                || _state.Mods.Any(mod => mod.IsEnabled && !mod.IsArchived
                    && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Error)))
            {
                return HealthLevel.Blocked;
            }

            return _state.LogSummary.WarningCount > 0
                || _state.Mods.Any(mod => mod.IsEnabled && mod.Issues.Any(issue => issue.Severity == IssueSeverity.Warning))
                || _state.CurrentSave is not null && (_state.CurrentSave.LatestBackup is null || _state.CurrentSave.LatestBackup < DateTime.Now.AddDays(-7))
                ? HealthLevel.Attention
                : HealthLevel.Ready;
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(MissingDirectoryVisibility));
        OnPropertyChanged(nameof(ConnectedVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(LogDetailVisibility));
        OnPropertyChanged(nameof(BusyVisibility));
        OnPropertyChanged(nameof(ReadyVisibility));
        OnPropertyChanged(nameof(AttentionVisibility));
        OnPropertyChanged(nameof(BlockedVisibility));
        OnPropertyChanged(nameof(StableRecoveryVisibility));
        OnPropertyChanged(nameof(ModAttentionVisibility));
        OnPropertyChanged(nameof(LogAttentionVisibility));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(ModSummaryText));
        OnPropertyChanged(nameof(SaveSummaryText));
        OnPropertyChanged(nameof(SuggestionSummaryText));
        OnPropertyChanged(nameof(LogSummaryText));
        OnPropertyChanged(nameof(LogIssues));
        OnPropertyChanged(nameof(MostLikelyCause));
        OnPropertyChanged(nameof(RecommendedAction));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(FeedbackMessage));
        ChooseDirectoryCommand.NotifyCanExecuteChanged();
        CheckCommand.NotifyCanExecuteChanged();
        LaunchGameCommand.NotifyCanExecuteChanged();
        RepairCommand.NotifyCanExecuteChanged();
        RestoreStableStateCommand.NotifyCanExecuteChanged();
    }

    public async Task CheckAsync(bool showFeedback)
    {
        if (!_state.IsGameConfigured)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在进行启动前检查...";
        Refresh();
        try
        {
            _runLock.Refresh();
            _state.LogSummary = await _logs.AnalyzeLatestAsync();
            await App.Current.Services.Mods.ScanForLaunchAsync();
            await App.Current.Services.Saves.ScanForLaunchAsync();
            _availableSnapshot = await _lastKnownGood.GetLatestAsync();
            if (showFeedback)
            {
                FeedbackMessage = $"检查完成：{_state.LogSummary.DisplaySummary}";
            }
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private async Task ChooseDirectoryAsync()
    {
        IsBusy = true;
        Refresh();
        try
        {
            var selected = await _platform.ChooseFolderAsync();
            if (selected is null)
            {
                return;
            }

            if (!await _locator.ConfigureAsync(selected))
            {
                await _dialogs.ShowMessageAsync(
                    "目录无效",
                    "这不是有效的星露谷物语安装目录。请选择包含 Stardew Valley.exe 或 StardewModdingAPI.exe 的文件夹。");
                return;
            }

            await App.Current.Services.NpcNames.PrepareAsync(_state.GameDirectory?.Path);
            await App.Current.Services.GuideCatalog.PrepareAsync();
            await _icons.PrepareAsync();
            FeedbackMessage = "游戏目录已连接。";
            App.Current.Services.SettingsPage.Refresh();
            App.Current.Services.Mods.Refresh();
            App.Current.Services.Saves.Refresh();
            await App.Current.Services.Guide.RefreshAsync();
            await CheckAsync(false);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private async Task LaunchGameAsync()
    {
        if (_settings.Current.DefaultLaunchMode != LaunchMode.Quick)
        {
            await CheckAsync(false);
        }

        var target = _settings.Current.DefaultLaunchTarget;
        var mode = _settings.Current.DefaultLaunchMode;
        var check = _launcher.PrepareLaunch(target, mode);
        if (check.CanFallbackToVanilla)
        {
            if (!await _dialogs.ConfirmAsync(check.Title, check.Message, "启动原版"))
            {
                return;
            }

            target = LaunchTarget.Vanilla;
            check = _launcher.PrepareLaunch(target, mode);
        }
        else if (!check.CanLaunch)
        {
            await _dialogs.ShowMessageAsync(check.Title, check.Message);
            return;
        }

        if (check.RequiresConfirmation
            && !await _dialogs.ConfirmAsync(check.Title, check.Message, "仍然启动"))
        {
            return;
        }

        if (_settings.Current.BackupSaveBeforeLaunch)
        {
            await App.Current.Services.Saves.BackupForLaunchAsync();
        }

        var started = await _launcher.LaunchAsync(
            target,
            mode,
            mode == LaunchMode.Diagnostic ? AfterDiagnosticExitAsync : null);
        FeedbackMessage = started ? "游戏已启动。" : "游戏启动失败，请查看日志。";
        Refresh();
    }

    private async Task RepairAsync()
    {
        await CheckAsync(false);
        var repaired = await App.Current.Services.Mods.AutoRepairAsync();
        FeedbackMessage = repaired switch
        {
            > 0 => $"已完成 {repaired} 项安全修复，可重新执行检查。",
            0 => "未发现可自动处理的阻断模组。",
            _ => "已取消修复操作。"
        };
        Refresh();
    }

    private void CopyReport()
    {
        _platform.CopyText(_logs.CreateReport(_state.LogSummary, _state.GameDirectory));
        FeedbackMessage = "诊断摘要已复制。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task ExportReportAsync()
    {
        var path = await _platform.ChooseMarkdownSavePathAsync("LSMA-Diagnostic-Report");
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, _logs.CreateReport(_state.LogSummary, _state.GameDirectory));
        FeedbackMessage = "诊断报告已导出。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private Task OpenLogsAsync()
    {
        var directory = _state.LogSummary.SourcePath is null
            ? AppPaths.SmapiLogs
            : Path.GetDirectoryName(_state.LogSummary.SourcePath)!;
        return _platform.OpenFolderAsync(directory);
    }

    private async Task AfterDiagnosticExitAsync()
    {
        await CheckAsync(false);
        if (!_state.LogSummary.HasCrash && _state.LogSummary.ErrorCount == 0)
        {
            _availableSnapshot = await _lastKnownGood.CaptureIfCleanAsync();
        }

        Refresh();
    }

    private async Task RestoreStableStateAsync()
    {
        if (_availableSnapshot is null
            || !await _dialogs.ConfirmAsync("恢复稳定状态", "将恢复到上次无错误退出时的模组组合。当前模组状态会先自动备份，确认继续？", "安全恢复"))
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在备份当前模组并恢复稳定状态...";
        Refresh();
        try
        {
            await _lastKnownGood.RestoreAsync(_availableSnapshot);
            FeedbackMessage = "已恢复到上次可正常游玩的模组状态。";
            IsBusy = false;
            await CheckAsync(false);
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("恢复失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private bool CanInteract() => !IsBusy;
    private bool CanCheck() => _state.IsGameConfigured && !IsBusy;
    private bool CanLaunch() => _state.IsGameConfigured && !_state.IsGameRunning && !_state.HasPendingRecovery && !IsBusy;
}
