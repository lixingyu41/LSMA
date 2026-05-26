namespace LSMA.Models;

public sealed class LastKnownGoodSnapshot
{
    public DateTime CreatedAt { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public string? EnabledModsZip { get; set; }
    public string? DisabledModsZip { get; set; }
    public string? SaveBackupPath { get; set; }
    public string? SmapiVersion { get; set; }
    public string? GameVersion { get; set; }
    public List<SnapshotModEntry> EnabledMods { get; set; } = [];
    public List<SnapshotModEntry> DisabledMods { get; set; } = [];
    public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm} 的稳定状态";
}

public sealed class SnapshotModEntry
{
    public string Name { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
