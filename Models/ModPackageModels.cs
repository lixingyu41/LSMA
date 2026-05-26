namespace LSMA.Models;

public sealed class ModInstallPlan
{
    public string PackagePath { get; init; } = string.Empty;
    public string PreparedPackagePath { get; set; } = string.Empty;
    public string PackageName => Path.GetFileName(PackagePath);
    public List<ModInstallPlanItem> Items { get; } = [];
    public List<string> Blockers { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool CanInstall => Items.Count > 0 && Blockers.Count == 0;
    public string Summary => CanInstall
        ? $"准备安装 {Items.Count} 个模组"
        : Blockers.Count > 0 ? $"发现 {Blockers.Count} 个阻止安装的问题" : "没有识别到可安装模组";
}

public sealed class ModInstallPlanItem
{
    public string ArchiveRoot { get; init; } = string.Empty;
    public string DestinationFolderName { get; init; } = string.Empty;
    public ModManifest Manifest { get; init; } = new();
    public ModInfo? ExistingMod { get; init; }
    public bool PreserveConfiguration { get; init; }
    public List<string> Warnings { get; } = [];
    public List<string> Blockers { get; } = [];
    public string Name => Manifest.Name ?? DestinationFolderName;
    public string Version => Manifest.Version ?? "-";
    public string ActionText => ExistingMod is null ? "新安装" : $"更新 {ExistingMod.Version} → {Version}";
    public string SafetyText => PreserveConfiguration ? "保留原配置" : "无需迁移配置";
}

public sealed class ModInstallResult
{
    public int InstalledCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Messages { get; } = [];
}
