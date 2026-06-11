using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class SavesViewModel : ViewModelBase
{
    private const int MaxConcurrentPostScanUpdates = 4;
    private readonly AppStateService _state;
    private readonly SaveLocatorService _locator;
    private readonly SaveParserService _parser;
    private readonly GameIconService _icons;
    private readonly SaveBackupService _backups;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly AutomaticScanMonitor _automaticScanMonitor;
    private SaveInfo? _selectedSave;
    private SavePlayerInfo? _selectedPlayer;
    private SaveBackupEntry? _selectedBackup;
    private string? _pendingSelectedPlayerKey;
    private bool _automaticScanningStarted;

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
        ImportSaveCommand = new AsyncRelayCommand(ImportSaveAsync, CanImport);
        ExportSaveCommand = new AsyncRelayCommand(ExportSaveAsync, CanExport);
        BackupCommand = new AsyncRelayCommand(BackupSelectedAsync, CanBackup);
        BackupAllCommand = new AsyncRelayCommand(BackupAllAsync, CanBackupAll);
        RestoreCommand = new AsyncRelayCommand(RestoreSelectedAsync, CanRestore);
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

    public ObservableCollection<SaveInfo> Saves { get; } = new RangeObservableCollection<SaveInfo>();
    public ObservableCollection<SaveBackupEntry> BackupEntries { get; } = new RangeObservableCollection<SaveBackupEntry>();
    public IAsyncRelayCommand ImportSaveCommand { get; }
    public IAsyncRelayCommand ExportSaveCommand { get; }
    public IAsyncRelayCommand BackupCommand { get; }
    public IAsyncRelayCommand BackupAllCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }

    public SaveInfo? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (SetProperty(ref _selectedSave, value))
            {
                _state.CurrentSave = value;
                SelectedPlayer = SelectPlayer(value, _pendingSelectedPlayerKey);
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(BackupStatus));
                OnPropertyChanged(nameof(SaveBackgroundVisibility));
                OnPropertyChanged(nameof(PlayerSelectorVisibility));
                LoadBackupEntries();
                Refresh();
                _ = App.Current.Services.Guide.RefreshAsync();
                App.Current.Services.Home.Refresh();
            }
        }
    }

    public SavePlayerInfo? SelectedPlayer
    {
        get => _selectedPlayer;
        set
        {
            if (SetProperty(ref _selectedPlayer, value))
            {
                OnPropertyChanged(nameof(PlayerSelectorVisibility));
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
    public Visibility PlayerSelectorVisibility => SelectedSave?.HasMultiplePlayers == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => _state.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SaveBackgroundVisibility => string.IsNullOrWhiteSpace(SelectedSave?.BackgroundImageUri)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string BackupStatus => SelectedSave?.LatestBackup is { } date
        ? $"最近备份：{FormatRelativeTime(date)}"
        : "尚无备份";

    public string BackupToolTip => SelectedSave?.LatestBackup is { } date
        ? date.ToString("yyyy-MM-dd HH:mm:ss")
        : string.Empty;

    private static string FormatRelativeTime(DateTime date)
    {
        var span = DateTime.Now - date;
        if (span.TotalSeconds < 60) return "刚刚";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}分钟前";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}小时前";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}天前";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}周前";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}个月前";
        return $"{(int)(span.TotalDays / 365)}年前";
    }
    public string SavesCountDisplay => $"存档: {Saves.Count}个";

    public void Refresh()
    {
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(SavesCountDisplay));
        OnPropertyChanged(nameof(PlayerSelectorVisibility));
        ImportSaveCommand.NotifyCanExecuteChanged();
        ExportSaveCommand.NotifyCanExecuteChanged();
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

    private async Task ScanAsync(string? preferredPath = null)
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
            var selectedPath = preferredPath ?? SelectedSave?.FolderPath;
            var selectedPlayerKey = SelectedPlayer?.PlayerKey;
            var sources = await _locator.LocateAsync();
            var parsedSaves = await Task.WhenAll(sources.Select(source => _parser.ParseAsync(source)));
            var results = parsedSaves
                .Where(save => save is not null)
                .Cast<SaveInfo>()
                .ToList();
            await ApplyPostScanDataAsync(results);

            ReplaceCollection(Saves, results);

            var nextSave = Saves.FirstOrDefault(save => string.Equals(save.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? Saves.FirstOrDefault();
            _pendingSelectedPlayerKey = string.Equals(nextSave?.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase)
                ? selectedPlayerKey
                : null;
            SelectedSave = nextSave;
            _pendingSelectedPlayerKey = null;
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

    private async Task ApplyPostScanDataAsync(IReadOnlyList<SaveInfo> saves)
    {
        if (saves.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 1, MaxConcurrentPostScanUpdates));
        var tasks = saves.Select(async save =>
        {
            await gate.WaitAsync();
            try
            {
                await _icons.ApplySaveIconsAsync(save);
                save.LatestBackup = _backups.GetLatestBackup(save.FolderName);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
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

    private static SavePlayerInfo? SelectPlayer(SaveInfo? save, string? playerKey)
    {
        if (save is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(playerKey))
        {
            var matched = save.Players.FirstOrDefault(player => string.Equals(player.PlayerKey, playerKey, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        return save.Players.FirstOrDefault();
    }

    private async Task ImportSaveAsync()
    {
        if (!_state.IsGameConfigured)
        {
            return;
        }

        var path = await _platform.ChooseSaveArchiveAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ImportSaveFromPathAsync(path, requireConfirmation: true);
    }

    public async Task<bool> ImportDroppedSaveAsync(string path, bool requireConfirmation)
        => await ImportSaveFromPathAsync(path, requireConfirmation);

    private async Task<bool> ImportSaveFromPathAsync(string path, bool requireConfirmation)
    {
        if (!_state.IsGameConfigured)
        {
            throw new InvalidOperationException("尚未连接游戏目录，不能导入存档。");
        }

        if (requireConfirmation
            && !await _dialogs.ConfirmAsync("导入存档", "同名存档会先自动备份再替换。确认导入这个存档？", "导入"))
        {
            return false;
        }

        SaveImportResult? result = null;
        await WithBusyAsync("正在导入存档...", async () =>
        {
            result = await _backups.ImportAsync(path);
            FeedbackMessage = result.ReplacedExisting ? "存档已导入，同名旧档已备份。" : "存档已导入。";
        }, "导入失败");

        if (result is not null)
        {
            await ScanAsync(Path.Combine(AppPaths.SaveSource, result.FolderName));
            App.Current.Services.Home.Refresh();
        }

        return result is not null;
    }

    private async Task ExportSaveAsync()
    {
        if (SelectedSave is not { ParseError: null } save)
        {
            return;
        }

        var suggested = $"{save.FolderName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var path = await _platform.ChooseSaveArchiveSavePathAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await WithBusyAsync("正在导出存档...", async () =>
        {
            await _backups.ExportAsync(save, path);
            FeedbackMessage = "存档已导出。";
        }, "导出失败");
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
        ReplaceCollection(
            BackupEntries,
            SelectedSave is null
                ? []
                : _backups.GetBackups(SelectedSave.FolderName));

        SelectedBackup = BackupEntries.FirstOrDefault();
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

    private bool CanImport() => _state.IsGameConfigured && !_state.IsGameRunning && !IsBusy;
    private bool CanExport() => SelectedSave is { ParseError: null } && !IsBusy;
    private bool CanBackupAll() => _state.IsGameConfigured && !IsBusy;
    private bool CanBackup() => SelectedSave is { ParseError: null } && !IsBusy;
    private bool CanRestore() => SelectedSave is not null && SelectedBackup is not null && !_state.IsGameRunning && !IsBusy;
}
