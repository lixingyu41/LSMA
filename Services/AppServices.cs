using LSMA.ViewModels;

namespace LSMA.Services;

public sealed class AppServices
{
    private bool _settingsInitialized;

    public AppServices()
    {
        Logging = new LoggingService();
        Files = new FileSystemSafeService(Logging);
        UiDispatcher = new UiDispatcherService();
        Settings = new SettingsService(Logging);
        State = new AppStateService();
        Navigation = new NavigationService();
        Dialogs = new DialogService();
        Platform = new PlatformService(Logging);
        GameLocator = new GameLocatorService(State, Settings, Logging);
        GuideCatalog = new GameContentCatalogService(State, Logging);
        RunLock = new GameRunLockService(State, Logging);
        SmapiLogs = new SmapiLogService(Logging);
        Launcher = new GameLaunchService(State, RunLock, Logging);
        ModScanner = new ModScannerService(State, Logging);
        ModAnalyzer = new ModAnalyzerService(Settings);
        ModBackups = new ModBackupService(Settings, Files, Logging);
        ModTransactions = new ModTransactionService(State, RunLock, ModBackups, ModScanner, Files, Logging);
        ExternalArchives = new ExternalArchiveReader(Logging);
        ModPackages = new ModPackageService(State, RunLock, ModScanner, ModBackups, ExternalArchives, Files, Logging);
        SaveLocator = new SaveLocatorService(Logging);
        NpcNames = new NpcLocalizationService(Logging);
        XnbTextures = new XnbTextureService();
        GameIcons = new GameIconService(State, XnbTextures, NpcNames, Logging);
        SaveParser = new SaveParserService(Logging, NpcNames);
        SaveBackups = new SaveBackupService(State, RunLock, Settings, Files, Logging);
        GuideData = new GuideDataService();
        GuideRecommendations = new GuideRecommendationService(GuideData);
        NexusCredentials = new NexusCredentialService();
        Nexus = new NexusClient(Logging);
        NexusFavorites = new NexusFavoriteService(Logging);
        NexusDownloads = new NexusDownloadService(Nexus, Logging);
        Cache = new CacheService(Files, Logging);
        AssetCache = new AssetCacheService(State, Settings, Files, XnbTextures, Logging);
        LastKnownGood = new LastKnownGoodService(State, RunLock, ModScanner, SaveBackups, Files, Logging);

        Home = new HomeViewModel(State, Settings, GameLocator, GameIcons, Launcher, RunLock, SmapiLogs, LastKnownGood, Platform, Dialogs);
        Mods = new ModsViewModel(State, RunLock, ModScanner, ModAnalyzer, ModBackups, ModTransactions, ModPackages, NexusCredentials, Nexus, NexusFavorites, NexusDownloads, Platform, Dialogs, UiDispatcher);
        Guide = new GuideViewModel(State, GuideRecommendations, GuideData, GameIcons, GuideCatalog);
        Saves = new SavesViewModel(State, SaveLocator, SaveParser, GameIcons, SaveBackups, Platform, Dialogs, UiDispatcher);
        SettingsPage = new SettingsViewModel(State, Settings, GameLocator, Platform, Dialogs, NexusCredentials, Nexus, SmapiLogs, Cache, AssetCache, GameIcons);
        Downloads = new DownloadsViewModel(NexusCredentials, Nexus, NexusFavorites, NexusDownloads, Platform, Dialogs);
    }

    public LoggingService Logging { get; }
    public FileSystemSafeService Files { get; }
    public UiDispatcherService UiDispatcher { get; }
    public SettingsService Settings { get; }
    public AppStateService State { get; }
    public NavigationService Navigation { get; }
    public DialogService Dialogs { get; }
    public PlatformService Platform { get; }
    public GameLocatorService GameLocator { get; }
    public GameContentCatalogService GuideCatalog { get; }
    public GameRunLockService RunLock { get; }
    public SmapiLogService SmapiLogs { get; }
    public GameLaunchService Launcher { get; }
    public ModScannerService ModScanner { get; }
    public ModAnalyzerService ModAnalyzer { get; }
    public ModBackupService ModBackups { get; }
    public ModTransactionService ModTransactions { get; }
    public ModPackageService ModPackages { get; }
    public ExternalArchiveReader ExternalArchives { get; }
    public SaveLocatorService SaveLocator { get; }
    public NpcLocalizationService NpcNames { get; }
    public SaveParserService SaveParser { get; }
    public SaveBackupService SaveBackups { get; }
    public GuideRecommendationService GuideRecommendations { get; }
    public GuideDataService GuideData { get; }
    public NexusCredentialService NexusCredentials { get; }
    public NexusClient Nexus { get; }
    public NexusFavoriteService NexusFavorites { get; }
    public NexusDownloadService NexusDownloads { get; }
    public CacheService Cache { get; }
    public XnbTextureService XnbTextures { get; }
    public GameIconService GameIcons { get; }
    public AssetCacheService AssetCache { get; }
    public LastKnownGoodService LastKnownGood { get; }
    public HomeViewModel Home { get; }
    public ModsViewModel Mods { get; }
    public GuideViewModel Guide { get; }
    public SavesViewModel Saves { get; }
    public SettingsViewModel SettingsPage { get; }
    public DownloadsViewModel Downloads { get; }

    public async Task InitializeAppearanceAsync()
    {
        if (!_settingsInitialized)
        {
            await Settings.InitializeAsync();
            _settingsInitialized = true;
        }

        App.Current.ApplyAppearance(Settings.Current.Theme, Settings.Current.Palette);
    }

    public async Task InitializeAsync()
    {
        await InitializeAppearanceAsync();
        await GameLocator.DetectAsync();
        await NpcNames.PrepareAsync(State.GameDirectory?.Path);
        await GuideCatalog.PrepareAsync();
        await GameIcons.PrepareAsync();
        RunLock.Refresh();
        Home.Refresh();
        await Mods.StartAutomaticScanningAsync();
        await Guide.RefreshAsync();
        await Saves.StartAutomaticScanningAsync();
        SettingsPage.Refresh();
    }
}
