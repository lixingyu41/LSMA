namespace LSMA.Utilities;

public static class AppPaths
{
    private static readonly string RoamingRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LSMA");

    private static readonly string LocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LSMA");

    public static string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
    public static string FavoritesFile => Path.Combine(RoamingRoot, "favorites.json");
    public static string Downloads => Path.Combine(LocalRoot, "Downloads");
    public static string ModBackups => Path.Combine(LocalRoot, "Backups", "Mods");
    public static string ModPacks => Path.Combine(LocalRoot, "ModPacks");
    public static string ModPackCatalogFile => Path.Combine(ModPacks, "catalog.json");
    public static string SaveBackups => Path.Combine(LocalRoot, "Backups", "Saves");
    public static string FailedStates => Path.Combine(LocalRoot, "Backups", "FailedStates");
    public static string ArchivedMods => Path.Combine(LocalRoot, "Backups", "ArchivedMods");
    public static string LastKnownGood => Path.Combine(LocalRoot, "Backups", "LastKnownGood");
    public static string Logs => Path.Combine(LocalRoot, "Logs");
    public static string LogFile => Path.Combine(Logs, "lsma.log");
    public static string Cache => Path.Combine(LocalRoot, "Cache");
    public static string ModTranslationCacheFile => Path.Combine(Cache, "mod-translations.zh-CN.json");
    public static string NexusModNameTranslationCacheFile => Path.Combine(Cache, "nexus-mod-name-translations.zh-CN.json");
    public static string NexusCoverCache => Path.Combine(Cache, "NexusCovers");
    public static string NexusCoverCacheFile => Path.Combine(Cache, "nexus-cover-cache.json");
    public static string AssetCache => Path.Combine(LocalRoot, "AssetCache", "StardewValley");
    public static string Temp => Path.Combine(LocalRoot, "Temp");
    public static string SaveSource => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves");
    public static string SmapiLogs => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");
    public static string SmapiStoreLogs => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "ConcernedApe.StardewValleyPC_0c8vynj4cqe4e", "LocalCache", "Roaming", "StardewValley", "ErrorLogs");
    public static IEnumerable<string> SmapiLogSources => [SmapiLogs, SmapiStoreLogs];

    public static IEnumerable<string> RequiredDirectories =>
    [
        RoamingRoot,
        Downloads,
        ModBackups,
        ModPacks,
        SaveBackups,
        FailedStates,
        ArchivedMods,
        LastKnownGood,
        Logs,
        Cache,
        NexusCoverCache,
        AssetCache,
        Temp
    ];
}
