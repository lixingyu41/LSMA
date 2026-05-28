using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class SavesViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly SaveLocatorService _locator;
    private readonly SaveParserService _parser;
    private readonly GameIconService _icons;
    private readonly SaveBackupService _backups;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly AutomaticScanMonitor _automaticScanMonitor;
    private SaveInfo? _selectedSave;
    private SaveBackupEntry? _selectedBackup;
    private bool _automaticScanningStarted;
    private bool _isFriendshipExpanded;

    public SavesViewModel(
        AppStateService state,
        SaveLocatorService locator,
        SaveParserService parser,
        GameIconService icons,
        SaveBackupService backups,
        PlatformService platform,
        DialogService dialogs,
        UiDispatcherService dispatcher)
    {
        _state = state;
        _locator = locator;
        _parser = parser;
        _icons = icons;
        _backups = backups;
        _platform = platform;
        _dialogs = dialogs;
        _automaticScanMonitor = new AutomaticScanMonitor(dispatcher, ScanAutomaticallyAsync);
        BackupCommand = new AsyncRelayCommand(BackupSelectedAsync, CanBackup);
        BackupAllCommand = new AsyncRelayCommand(BackupAllAsync, CanBackupAll);
        RestoreCommand = new AsyncRelayCommand(RestoreSelectedAsync, CanRestore);
        OpenBackupsCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(AppPaths.SaveBackups));
        ToggleFriendshipsCommand = new RelayCommand(() => IsFriendshipExpanded = !IsFriendshipExpanded);
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

    public ObservableCollection<SaveInfo> Saves { get; } = [];
    public ObservableCollection<SaveBackupEntry> BackupEntries { get; } = [];
    public IAsyncRelayCommand BackupCommand { get; }
    public IAsyncRelayCommand BackupAllCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand OpenBackupsCommand { get; }
    public IRelayCommand ToggleFriendshipsCommand { get; }

    public SaveInfo? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (SetProperty(ref _selectedSave, value))
            {
                _state.CurrentSave = value;
                IsFriendshipExpanded = false;
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(BackupStatus));
                OnPropertyChanged(nameof(SelectedSaveStats));
                OnPropertyChanged(nameof(FriendshipsToggleVisibility));
                LoadBackupEntries();
                Refresh();
                _ = App.Current.Services.Guide.RefreshAsync();
                App.Current.Services.Home.Refresh();
            }
        }
    }

    public SaveBackupEntry? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                RestoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility UnavailableVisibility => _state.IsGameConfigured ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AvailableVisibility => _state.IsGameConfigured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailVisibility => SelectedSave is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RunningVisibility => _state.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    public string BackupStatus => SelectedSave?.LatestBackup is { } date ? $"最近备份：{date:yyyy-MM-dd HH:mm}" : "尚无备份";
    public string TaskStatus => IsBusy ? ProgressText : "就绪";
    public string SavesCountDisplay => $"共发现 {Saves.Count} 个存档";
    public string SelectedSaveStats => SelectedSave is null
        ? string.Empty
        : $"{SelectedSave.DateDisplay} · {SelectedSave.MoneyDisplay} · 总收入 {SelectedSave.TotalIncomeDisplay} · {SelectedSave.PlayTimeDisplay} · 剩余 {SelectedSave.RemainingDaysInSeason} 天";

    public bool IsFriendshipExpanded
    {
        get => _isFriendshipExpanded;
        set
        {
            if (SetProperty(ref _isFriendshipExpanded, value))
            {
                OnPropertyChanged(nameof(RestFriendshipsVisibility));
                OnPropertyChanged(nameof(FriendshipsToggleText));
                OnPropertyChanged(nameof(FriendshipsToggleVisibility));
            }
        }
    }

    public Visibility RestFriendshipsVisibility => IsFriendshipExpanded ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FriendshipsToggleVisibility => SelectedSave is { HasMoreFriendships: true }
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string FriendshipsToggleText => IsFriendshipExpanded
        ? "收起"
        : SelectedSave is { HasMoreFriendships: true }
            ? $"展开全部（+{SelectedSave.RestFriendships.Count()} 人）"
            : "展开全部";

    public void Refresh()
    {
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(TaskStatus));
        OnPropertyChanged(nameof(SavesCountDisplay));
        BackupCommand.NotifyCanExecuteChanged();
        BackupAllCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    public async Task StartAutomaticScanningAsync()
    {
        _automaticScanningStarted = true;
        ConfigureAutomaticWatchers();
        await ScanAsync();
    }

    public Task ScanForLaunchAsync() => ScanAsync();

    public async Task BackupForLaunchAsync()
    {
        if (SelectedSave is null)
        {
            await ScanAsync();
        }

        if (SelectedSave is null || SelectedSave.ParseError is not null)
        {
            return;
        }

        var record = await _backups.CreateAsync(SelectedSave, "启动前自动备份");
        SelectedSave.LatestBackup = record.CreatedAt;
        OnPropertyChanged(nameof(BackupStatus));
        LoadBackupEntries();
    }

    private async Task ScanAsync()
    {
        if (!_state.IsGameConfigured || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在自动读取本机存档...";
        Refresh();
        try
        {
            var selectedPath = SelectedSave?.FolderPath;
            var results = new List<SaveInfo>();
            foreach (var source in await _locator.LocateAsync())
            {
                var save = await _parser.ParseAsync(source);
                if (save is not null)
                {
                    _icons.ApplySaveIcons(save);
                    save.LatestBackup = _backups.GetLatestBackup(save.FolderName);
                    results.Add(save);
                }
            }

            Saves.Clear();
            foreach (var save in results)
            {
                Saves.Add(save);
            }

            SelectedSave = Saves.FirstOrDefault(save => string.Equals(save.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? Saves.FirstOrDefault();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("自动读取存档失败", exception.Message);
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
        ConfigureAutomaticWatchers();
    }

    private void ConfigureAutomaticWatchers()
    {
        if (!_automaticScanningStarted || !_state.IsGameConfigured)
        {
            _automaticScanMonitor.ReplaceWatchers();
            return;
        }

        if (Directory.Exists(AppPaths.SaveSource))
        {
            _automaticScanMonitor.ReplaceWatchers(new AutomaticScanWatchTarget(AppPaths.SaveSource, true));
            return;
        }

        var parent = Path.GetDirectoryName(AppPaths.SaveSource)!;
        if (Directory.Exists(parent))
        {
            _automaticScanMonitor.ReplaceWatchers(
                new AutomaticScanWatchTarget(parent, true, path => IsWithinDirectory(path, AppPaths.SaveSource)));
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _automaticScanMonitor.ReplaceWatchers(
            new AutomaticScanWatchTarget(appData, false, path => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task BackupSelectedAsync()
    {
        if (SelectedSave is not { ParseError: null } save)
        {
            return;
        }

        await WithBusyAsync("正在创建存档备份...", async () =>
        {
            var record = await _backups.CreateAsync(save);
            save.LatestBackup = record.CreatedAt;
            FeedbackMessage = "存档备份已创建。";
            OnPropertyChanged(nameof(BackupStatus));
            LoadBackupEntries();
            await App.Current.Services.Guide.RefreshAsync();
        }, "备份失败");
    }

    private async Task BackupAllAsync()
    {
        await WithBusyAsync("正在备份全部存档...", async () =>
        {
            var count = await _backups.CreateAllAsync(Saves);
            FeedbackMessage = $"已备份 {count} 个存档。";
            LoadBackupEntries();
        }, "备份失败");
    }

    private async Task RestoreSelectedAsync()
    {
        if (SelectedSave is not { } save || SelectedBackup is not { } backup
            || !await _dialogs.ConfirmAsync("恢复存档", "恢复会替换当前存档内容。LSMA 将先自动备份当前状态，确认继续？", "安全恢复"))
        {
            return;
        }

        await WithBusyAsync("正在备份当前状态并恢复存档...", async () =>
        {
            await _backups.RestoreAsync(save, backup);
            FeedbackMessage = "存档已安全恢复。";
            IsBusy = false;
            await ScanAsync();
            App.Current.Services.Home.Refresh();
        }, "恢复未完成");
    }

    private async Task WithBusyAsync(string progress, Func<Task> operation, string errorTitle)
    {
        IsBusy = true;
        ProgressText = progress;
        Refresh();
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync(errorTitle, exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            Refresh();
        }
    }

    private void LoadBackupEntries()
    {
        BackupEntries.Clear();
        if (SelectedSave is not null)
        {
            foreach (var item in _backups.GetBackups(SelectedSave.FolderName))
            {
                BackupEntries.Add(item);
            }
        }

        SelectedBackup = BackupEntries.FirstOrDefault();
    }

    private bool CanBackupAll() => _state.IsGameConfigured && !IsBusy;
    private bool CanBackup() => SelectedSave is { ParseError: null } && !IsBusy;
    private bool CanRestore() => SelectedSave is not null && SelectedBackup is not null && !_state.IsGameRunning && !IsBusy;
}
