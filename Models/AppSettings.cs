namespace LSMA.Models;

public enum AppTheme
{
    Dark,
    Light,
    System
}

public enum AppPalette
{
    Stardrop,
    Junimo,
    Moonlight,
    Cranberry
}

public enum LaunchTarget
{
    Smapi,
    Vanilla
}

public enum LaunchMode
{
    Quick,
    Safe,
    Diagnostic
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 10;

    public string? GameDirectory { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public AppPalette Palette { get; set; } = AppPalette.Stardrop;

    public LaunchTarget DefaultLaunchTarget { get; set; } = LaunchTarget.Smapi;

    public LaunchMode DefaultLaunchMode { get; set; } = LaunchMode.Safe;

    public bool BackupSaveBeforeLaunch { get; set; } = true;

    public bool BackupSaveBeforeUpdate { get; set; } = true;

    public int ModBackupRetention { get; set; } = 20;

    public int SaveBackupRetention { get; set; } = 20;

    public bool LocalAssetCacheEnabled { get; set; }

    public bool GpuPageAccelerationEnabled { get; set; } = true;

    public bool NexusDownloadDebugStepMode { get; set; }

    public bool NexusDownloadDebugShowWebViewMode { get; set; }

    public bool ModMetadataTranslationEnabled { get; set; } = true;

    public Dictionary<string, long> NexusBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> GuideCollapsedSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime? LastModsUpdateCheckAt { get; set; }

    public Dictionary<string, ModUpdateCacheEntry> ModUpdateCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double ModsPageListRatio { get; set; }

    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool LaunchViaSteam { get; set; }
}

public sealed class ModUpdateCacheEntry
{
    public long NexusModId { get; set; }

    public string? RemoteVersion { get; set; }

    public string? RemoteCategoryName { get; set; }

    public long? RemoteUpdatedTimestamp { get; set; }

    public long? RemoteDownloads { get; set; }

    public DateTime CheckedAt { get; set; }
}
