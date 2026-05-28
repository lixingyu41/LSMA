using LSMA.Utilities;

namespace LSMA.Models;

public enum ModState
{
    Normal,
    Warning,
    Error,
    Disabled,
    Archived
}

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed class ModIssue
{
    public IssueSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ModInfo
{
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsArchived { get; set; }
    public bool IsFavorite { get; set; }
    public string? SuggestedNestedDirectory { get; set; }
    public long? NexusModId { get; set; }
    public string? RemoteVersion { get; set; }
    public ModManifest? Manifest { get; set; }
    public List<ModIssue> Issues { get; } = [];
    public string Name => Manifest?.Name ?? FolderName;
    public string Author => Manifest?.Author ?? "未知作者";
    public string Version => Manifest?.Version ?? "-";
    public string UniqueId => Manifest?.UniqueID ?? "未识别";
    public string StatusText => IsArchived ? "已归档" : Manifest is null ? "无效" : !IsEnabled ? "已禁用" : Issues.Any(i => i.Severity == IssueSeverity.Error) ? "有问题" : Issues.Count > 0 ? "注意" : "正常";
    public bool CanRepairNestedDirectory => !string.IsNullOrWhiteSpace(SuggestedNestedDirectory);
    public string IssueSummary => Issues.Count == 0 ? "未发现问题" : Issues[0].Message;
    public bool HasRequiredDependencies => Manifest?.Dependencies?.Any(d => d.IsRequired) == true;
    public string DependencyText => Manifest?.Dependencies is { Count: > 0 } dependencies
        ? string.Join("、", dependencies.Select(item => item.UniqueID ?? "未指定"))
        : "无必需前置";
    public string UpdateSourceText => Manifest?.UpdateKeys is { Count: > 0 } sources
        ? string.Join("、", sources)
        : "未配置";
    public bool HasUpdate => RemoteVersion is not null && !VersionHelper.IsAtLeast(Version, RemoteVersion);
    public string UpdateStatus => NexusModId is null ? "未绑定更新来源" : HasUpdate ? $"可更新至 {RemoteVersion}" : "已是最新或尚未检查";
    public string DependencyStatus => Manifest is null
        ? "无法检查"
        : Issues.Any(issue => issue.Message.Contains("前置", StringComparison.Ordinal)
            || issue.Message.Contains("主模组", StringComparison.Ordinal))
            ? "存在缺失"
            : "前置完整";
    public int IssueCount => Issues.Count;
}
