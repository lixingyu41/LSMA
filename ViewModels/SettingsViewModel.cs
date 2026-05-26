using CommunityToolkit.Mvvm.Input;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly SettingsService _settings;
    private readonly GameLocatorService _locator;
    private readonly PlatformService _platform;
    private readonly DialogService _dialogs;
    private readonly NexusCredentialService _credentials;
    private readonly NexusClient _nexus;
    private readonly SmapiLogService _diagnostics;
    private readonly CacheService _cache;
    private readonly AssetCacheService _assetCache;
    private string _nexusKeyInput = string.Empty;
    private bool _backupSaveBeforeLaunch;
    private bool _backupSaveBeforeUpdate;
    private bool _localAssetCacheEnabled;
    private double _modBackupRetention = 20;
    private double _saveBackupRetention = 20;
    private string _externalArchiveToolPath = string.Empty;
    private string? _nexusConnectionStatus;
    private string _xnbToolPath = string.Empty;
    private string _xnbArgumentsTemplate = "\"{input}\" \"{output}\"";

    public SettingsViewModel(
        AppStateService state,
        SettingsService settings,
        GameLocatorService locator,
        PlatformService platform,
        DialogService dialogs,
        NexusCredentialService credentials,
        NexusClient nexus,
        SmapiLogService diagnostics,
        CacheService cache,
        AssetCacheService assetCache)
    {
        _state = state;
        _settings = settings;
        _locator = locator;
        _platform = platform;
        _dialogs = dialogs;
        _credentials = credentials;
        _nexus = nexus;
        _diagnostics = diagnostics;
        _cache = cache;
        _assetCache = assetCache;
        ChooseDirectoryCommand = new AsyncRelayCommand(ChooseDirectoryAsync, CanInteract);
        OpenGameDirectoryCommand = new AsyncRelayCommand(OpenGameDirectoryAsync, () => _state.IsGameConfigured);
        DarkThemeCommand = new AsyncRelayCommand(() => SelectDisplayThemeAsync(AppTheme.Dark));
        LightThemeCommand = new AsyncRelayCommand(() => SelectDisplayThemeAsync(AppTheme.Light));
        SystemThemeCommand = new AsyncRelayCommand(ToggleSystemThemeFollowAsync);
        StardropPaletteCommand = new AsyncRelayCommand(() => SetPaletteAsync(AppPalette.Stardrop));
        JunimoPaletteCommand = new AsyncRelayCommand(() => SetPaletteAsync(AppPalette.Junimo));
        MoonlightPaletteCommand = new AsyncRelayCommand(() => SetPaletteAsync(AppPalette.Moonlight));
        CranberryPaletteCommand = new AsyncRelayCommand(() => SetPaletteAsync(AppPalette.Cranberry));
        UseSmapiCommand = new AsyncRelayCommand(() => SetTargetAsync(LaunchTarget.Smapi));
        UseVanillaCommand = new AsyncRelayCommand(() => SetTargetAsync(LaunchTarget.Vanilla));
        UseQuickModeCommand = new AsyncRelayCommand(() => SetModeAsync(LaunchMode.Quick));
        UseSafeModeCommand = new AsyncRelayCommand(() => SetModeAsync(LaunchMode.Safe));
        UseDiagnosticModeCommand = new AsyncRelayCommand(() => SetModeAsync(LaunchMode.Diagnostic));
        OpenNexusPageCommand = new AsyncRelayCommand(() => _platform.OpenUriAsync("https://www.nexusmods.com/users/myaccount?tab=api%20access"));
        SaveNexusKeyCommand = new AsyncRelayCommand(SaveNexusKeyAsync, CanSaveNexus);
        ClearNexusKeyCommand = new AsyncRelayCommand(ClearNexusKeyAsync, () => _credentials.HasCredential && !IsBusy);
        TestNexusConnectionCommand = new AsyncRelayCommand(TestNexusConnectionAsync, () => _credentials.HasCredential && !IsBusy);
        SaveBackupSettingsCommand = new AsyncRelayCommand(SaveBackupSettingsAsync, CanInteract);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, CanInteract);
        BuildAssetCacheCommand = new AsyncRelayCommand(BuildAssetCacheAsync, CanInteract);
        ClearAssetCacheCommand = new AsyncRelayCommand(ClearAssetCacheAsync, CanInteract);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync, CanInteract);
        OpenSettingsDirectoryCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(Path.GetDirectoryName(AppPaths.SettingsFile)!));
        OpenBackupDirectoryCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(Path.GetDirectoryName(AppPaths.ModBackups)!));
        OpenLogDirectoryCommand = new AsyncRelayCommand(() => _platform.OpenFolderAsync(AppPaths.Logs));
    }

    public IAsyncRelayCommand ChooseDirectoryCommand { get; }
    public IAsyncRelayCommand OpenGameDirectoryCommand { get; }
    public IAsyncRelayCommand DarkThemeCommand { get; }
    public IAsyncRelayCommand LightThemeCommand { get; }
    public IAsyncRelayCommand SystemThemeCommand { get; }
    public IAsyncRelayCommand StardropPaletteCommand { get; }
    public IAsyncRelayCommand JunimoPaletteCommand { get; }
    public IAsyncRelayCommand MoonlightPaletteCommand { get; }
    public IAsyncRelayCommand CranberryPaletteCommand { get; }
    public IAsyncRelayCommand UseSmapiCommand { get; }
    public IAsyncRelayCommand UseVanillaCommand { get; }
    public IAsyncRelayCommand UseQuickModeCommand { get; }
    public IAsyncRelayCommand UseSafeModeCommand { get; }
    public IAsyncRelayCommand UseDiagnosticModeCommand { get; }
    public IAsyncRelayCommand OpenNexusPageCommand { get; }
    public IAsyncRelayCommand SaveNexusKeyCommand { get; }
    public IAsyncRelayCommand ClearNexusKeyCommand { get; }
    public IAsyncRelayCommand TestNexusConnectionCommand { get; }
    public IAsyncRelayCommand SaveBackupSettingsCommand { get; }
    public IAsyncRelayCommand ClearCacheCommand { get; }
    public IAsyncRelayCommand BuildAssetCacheCommand { get; }
    public IAsyncRelayCommand ClearAssetCacheCommand { get; }
    public IAsyncRelayCommand ExportDiagnosticsCommand { get; }
    public IAsyncRelayCommand OpenSettingsDirectoryCommand { get; }
    public IAsyncRelayCommand OpenBackupDirectoryCommand { get; }
    public IAsyncRelayCommand OpenLogDirectoryCommand { get; }

    public string NexusKeyInput
    {
        get => _nexusKeyInput;
        set
        {
            if (SetProperty(ref _nexusKeyInput, value))
            {
                SaveNexusKeyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool BackupSaveBeforeLaunch
    {
        get => _backupSaveBeforeLaunch;
        set => SetProperty(ref _backupSaveBeforeLaunch, value);
    }

    public bool BackupSaveBeforeUpdate
    {
        get => _backupSaveBeforeUpdate;
        set => SetProperty(ref _backupSaveBeforeUpdate, value);
    }

    public bool LocalAssetCacheEnabled
    {
        get => _localAssetCacheEnabled;
        set => SetProperty(ref _localAssetCacheEnabled, value);
    }

    public double ModBackupRetention
    {
        get => _modBackupRetention;
        set => SetProperty(ref _modBackupRetention, value);
    }

    public double SaveBackupRetention
    {
        get => _saveBackupRetention;
        set => SetProperty(ref _saveBackupRetention, value);
    }

    public string ExternalArchiveToolPath
    {
        get => _externalArchiveToolPath;
        set => SetProperty(ref _externalArchiveToolPath, value);
    }

    public string XnbToolPath
    {
        get => _xnbToolPath;
        set => SetProperty(ref _xnbToolPath, value);
    }

    public string XnbArgumentsTemplate
    {
        get => _xnbArgumentsTemplate;
        set => SetProperty(ref _xnbArgumentsTemplate, value);
    }

    public string DirectoryStatus => _state.IsGameConfigured ? "已连接游戏" : "未配置游戏目录";
    public string DirectoryPath => _state.GameDirectory?.Path ?? "尚未选择";
    public Visibility CanOpenDirectoryVisibility => _state.IsGameConfigured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NexusTutorialVisibility => _credentials.HasCredential ? Visibility.Collapsed : Visibility.Visible;
    public string NexusStatus => _nexusConnectionStatus
        ?? (_credentials.HasCredential ? "授权码已安全保存在 Windows 凭据中" : "尚未保存授权码");
    public string NexusRateLimitStatus => _nexus.RateLimitStatus;
    public string VersionDisplay
    {
        get
        {
            var version = typeof(LSMA.App).Assembly.GetName().Version;
            return version is null ? "版本 -" : $"版本 {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }
    public bool IsSmapiSelected => _settings.Current.DefaultLaunchTarget == LaunchTarget.Smapi;
    public bool IsVanillaSelected => _settings.Current.DefaultLaunchTarget == LaunchTarget.Vanilla;
    public bool IsQuickModeSelected => _settings.Current.DefaultLaunchMode == LaunchMode.Quick;
    public bool IsSafeModeSelected => _settings.Current.DefaultLaunchMode == LaunchMode.Safe;
    public bool IsDiagnosticModeSelected => _settings.Current.DefaultLaunchMode == LaunchMode.Diagnostic;
    public bool IsDarkThemeSelected => CurrentDisplayTheme == AppTheme.Dark;
    public bool IsLightThemeSelected => CurrentDisplayTheme == AppTheme.Light;
    public bool IsSystemThemeSelected => IsFollowingSystem;
    public bool IsStardropPaletteSelected => _settings.Current.Palette == AppPalette.Stardrop;
    public bool IsJunimoPaletteSelected => _settings.Current.Palette == AppPalette.Junimo;
    public bool IsMoonlightPaletteSelected => _settings.Current.Palette == AppPalette.Moonlight;
    public bool IsCranberryPaletteSelected => _settings.Current.Palette == AppPalette.Cranberry;
    private bool IsFollowingSystem => _settings.Current.Theme == AppTheme.System;
    private AppTheme CurrentDisplayTheme => IsFollowingSystem
        ? App.Current.GetSystemTheme()
        : _settings.Current.Theme;

    public void Refresh()
    {
        BackupSaveBeforeLaunch = _settings.Current.BackupSaveBeforeLaunch;
        BackupSaveBeforeUpdate = _settings.Current.BackupSaveBeforeUpdate;
        LocalAssetCacheEnabled = _settings.Current.LocalAssetCacheEnabled;
        ModBackupRetention = _settings.Current.ModBackupRetention;
        SaveBackupRetention = _settings.Current.SaveBackupRetention;
        ExternalArchiveToolPath = _settings.Current.ExternalArchiveToolPath ?? string.Empty;
        XnbToolPath = _settings.Current.XnbToolPath ?? string.Empty;
        XnbArgumentsTemplate = _settings.Current.XnbArgumentsTemplate;
        OnPropertyChanged(nameof(DirectoryStatus));
        OnPropertyChanged(nameof(DirectoryPath));
        OnPropertyChanged(nameof(CanOpenDirectoryVisibility));
        OnPropertyChanged(nameof(NexusTutorialVisibility));
        OnPropertyChanged(nameof(NexusStatus));
        OnPropertyChanged(nameof(NexusRateLimitStatus));
        OnPropertyChanged(nameof(VersionDisplay));
        OnPropertyChanged(nameof(IsSmapiSelected));
        OnPropertyChanged(nameof(IsVanillaSelected));
        OnPropertyChanged(nameof(IsQuickModeSelected));
        OnPropertyChanged(nameof(IsSafeModeSelected));
        OnPropertyChanged(nameof(IsDiagnosticModeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsStardropPaletteSelected));
        OnPropertyChanged(nameof(IsJunimoPaletteSelected));
        OnPropertyChanged(nameof(IsMoonlightPaletteSelected));
        OnPropertyChanged(nameof(IsCranberryPaletteSelected));
        NotifyCommands();
    }

    private async Task ChooseDirectoryAsync()
    {
        IsBusy = true;
        NotifyCommands();
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

            Refresh();
            App.Current.Services.Home.Refresh();
            App.Current.Services.Mods.Refresh();
            App.Current.Services.Saves.Refresh();
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private Task OpenGameDirectoryAsync()
    {
        return _state.GameDirectory is null ? Task.CompletedTask : _platform.OpenFolderAsync(_state.GameDirectory.Path);
    }

    private async Task SaveNexusKeyAsync()
    {
        var key = NexusKeyInput.Trim();
        if (key.Length == 0)
        {
            return;
        }

        try
        {
            _credentials.Save(key);
            _nexusConnectionStatus = null;
            NexusKeyInput = string.Empty;
            FeedbackMessage = "授权码已保存到 Windows 凭据。";
            Refresh();
        }
        catch (Exception)
        {
            await _dialogs.ShowMessageAsync("保存失败", "无法写入 Windows 凭据，请检查系统账户状态。");
        }
    }

    private async Task ClearNexusKeyAsync()
    {
        _credentials.Clear();
        _nexusConnectionStatus = null;
        FeedbackMessage = "授权码已清除。";
        Refresh();
        await Task.CompletedTask;
    }

    private async Task ClearCacheAsync()
    {
        if (!await _dialogs.ConfirmAsync("清空缓存", "将清除 LSMA 临时缓存，不会移除备份或游戏文件。", "清空"))
        {
            return;
        }

        IsBusy = true;
        ProgressText = "正在清空缓存...";
        NotifyCommands();
        try
        {
            await _cache.ClearAsync();
            FeedbackMessage = "本地缓存已清空。";
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("清空缓存失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            NotifyCommands();
        }
    }

    private async Task BuildAssetCacheAsync()
    {
        IsBusy = true;
        ProgressText = "正在从本机游戏目录生成素材缓存...";
        NotifyCommands();
        try
        {
            await SaveBackupSettingsAsync();
            var count = await _assetCache.BuildAsync();
            FeedbackMessage = $"本地素材缓存已生成，共处理 {count} 项。";
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("无法生成素材缓存", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            NotifyCommands();
        }
    }

    private async Task ClearAssetCacheAsync()
    {
        if (!await _dialogs.ConfirmAsync("清空素材缓存", "仅会清空 LSMA 的本地缓存，不会修改游戏文件。", "清空"))
        {
            return;
        }

        await _assetCache.ClearAsync();
        FeedbackMessage = "本地素材缓存已清空。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task ExportDiagnosticsAsync()
    {
        var path = await _platform.ChooseMarkdownSavePathAsync("LSMA-Diagnostic-Report");
        if (path is null)
        {
            return;
        }

        var summary = await _diagnostics.AnalyzeLatestAsync();
        await File.WriteAllTextAsync(path, _diagnostics.CreateReport(summary, _state.GameDirectory));
        FeedbackMessage = "诊断信息已导出。";
        OnPropertyChanged(nameof(FeedbackMessage));
    }

    private async Task TestNexusConnectionAsync()
    {
        var key = _credentials.GetKey();
        if (key is null)
        {
            await _dialogs.ShowMessageAsync("未保存授权码", "请先保存 Nexus 授权码。");
            return;
        }

        IsBusy = true;
        ProgressText = "正在连接 Nexus Mods...";
        NotifyCommands();
        try
        {
            var result = await _nexus.TestConnectionAsync(key);
            _nexusConnectionStatus = result.Success
                ? $"{result.Message}{(result.IsPremium ? " · Premium" : string.Empty)}"
                : result.Message;
            FeedbackMessage = result.Message;
            OnPropertyChanged(nameof(NexusStatus));
            OnPropertyChanged(nameof(NexusRateLimitStatus));
            if (!result.Success)
            {
                await _dialogs.ShowMessageAsync("连接测试", result.Message);
            }
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            NotifyCommands();
        }
    }

    private async Task SaveBackupSettingsAsync()
    {
        await _settings.UpdateAsync(settings =>
        {
            settings.BackupSaveBeforeLaunch = BackupSaveBeforeLaunch;
            settings.BackupSaveBeforeUpdate = BackupSaveBeforeUpdate;
            settings.ModBackupRetention = Math.Clamp((int)ModBackupRetention, 1, 200);
            settings.SaveBackupRetention = Math.Clamp((int)SaveBackupRetention, 1, 200);
            settings.LocalAssetCacheEnabled = LocalAssetCacheEnabled;
            settings.ExternalArchiveToolPath = string.IsNullOrWhiteSpace(ExternalArchiveToolPath) ? null : ExternalArchiveToolPath.Trim();
            settings.XnbToolPath = string.IsNullOrWhiteSpace(XnbToolPath) ? null : XnbToolPath.Trim();
            settings.XnbArgumentsTemplate = string.IsNullOrWhiteSpace(XnbArgumentsTemplate)
                ? "\"{input}\" \"{output}\""
                : XnbArgumentsTemplate.Trim();
        });
        FeedbackMessage = "备份与高级选项已保存。";
        Refresh();
    }

    private async Task SelectDisplayThemeAsync(AppTheme theme)
    {
        if (CurrentDisplayTheme == theme)
        {
            Refresh();
            return;
        }

        // Selecting a different visual mode is an explicit manual override and disables follow-system.
        await SetThemeAsync(theme);
    }

    private async Task ToggleSystemThemeFollowAsync()
    {
        if (IsFollowingSystem)
        {
            // Switching follow-system off freezes whichever mode is currently displayed.
            await SetThemeAsync(CurrentDisplayTheme);
            return;
        }

        // Switching follow-system on makes the selected visual mode mirror Windows.
        await SetThemeAsync(AppTheme.System);
    }

    private async Task SetThemeAsync(AppTheme theme)
    {
        await _settings.UpdateAsync(value => value.Theme = theme);
        App.Current.ApplyAppearance(theme, _settings.Current.Palette);
        Refresh();
    }

    private async Task SetPaletteAsync(AppPalette palette)
    {
        await _settings.UpdateAsync(value => value.Palette = palette);
        App.Current.ApplyAppearance(_settings.Current.Theme, palette);
        Refresh();
    }

    private async Task SetTargetAsync(LaunchTarget target)
    {
        await _settings.UpdateAsync(value => value.DefaultLaunchTarget = target);
        Refresh();
        App.Current.Services.Home.Refresh();
    }

    private async Task SetModeAsync(LaunchMode mode)
    {
        await _settings.UpdateAsync(value => value.DefaultLaunchMode = mode);
        Refresh();
        App.Current.Services.Home.Refresh();
    }

    private void NotifyCommands()
    {
        ChooseDirectoryCommand.NotifyCanExecuteChanged();
        OpenGameDirectoryCommand.NotifyCanExecuteChanged();
        SaveNexusKeyCommand.NotifyCanExecuteChanged();
        ClearNexusKeyCommand.NotifyCanExecuteChanged();
        TestNexusConnectionCommand.NotifyCanExecuteChanged();
        SaveBackupSettingsCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
        BuildAssetCacheCommand.NotifyCanExecuteChanged();
        ClearAssetCacheCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private bool CanInteract() => !IsBusy;
    private bool CanSaveNexus() => !IsBusy && !string.IsNullOrWhiteSpace(NexusKeyInput);
}
