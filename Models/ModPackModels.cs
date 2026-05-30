using System.Text.Json.Serialization;

namespace LSMA.Models;

public sealed class ModPackCatalog
{
    public string? ActivePackId { get; set; }
    public bool InitialPackCreated { get; set; }
    public List<ModPackInfo> Packs { get; set; } = [];
}

public sealed class ModPackInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; }
    public List<ModPackEntry> Entries { get; set; } = [];
    [JsonIgnore]
    public int EntryCount => Entries.Count;
    [JsonIgnore]
    public int MissingCount => Entries.Count(entry => entry.IsMissing);
    [JsonIgnore]
    public string StatusText => IsActive
        ? $"当前加载 · {EntryCount} 个模组 · 缺失 {MissingCount}"
        : $"{EntryCount} 个模组 · 缺失 {MissingCount}";
    [JsonIgnore]
    public bool CanSwitch => !IsActive && MissingCount == 0;
}

public sealed class ModPackEntry
{
    public string UniqueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "-";
    public long? NexusModId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public bool IsMissing { get; set; }
    public string? MissingReason { get; set; }
    [JsonIgnore]
    public string NexusText => NexusModId is null ? "未绑定 Nexus ID" : $"Nexus ID：{NexusModId}";
    [JsonIgnore]
    public string FileStatusText => IsMissing ? $"缺文件：{MissingReason ?? "需要下载"}" : "文件就绪";
}

public sealed class ModPackImportPlan
{
    public string SourcePath { get; init; } = string.Empty;
    public string TargetPackName { get; init; } = string.Empty;
    public bool MergeIntoExisting { get; init; }
    public List<ModPackEntry> Entries { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Blockers { get; } = [];
    [JsonIgnore]
    public bool CanImport => Entries.Count > 0 && Blockers.Count == 0;
}

public sealed class ModPackExportOptions
{
    public bool IncludeModFiles { get; init; }
}
