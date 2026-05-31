using LSMA.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

public sealed class ModInfo : ObservableObject
{
    private bool _isSelected;
    private bool _isPointerOver;

    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsArchived { get; set; }
    public bool IsFavorite { get; set; }
    public string? SuggestedNestedDirectory { get; set; }
    public long? NexusModId { get; set; }
    public string? RemoteVersion { get; set; }
    public ModManifest? Manifest { get; set; }
    public string? TranslatedName { get; set; }
    public string? TranslatedDescription { get; set; }
    public List<ModIssue> Issues { get; } = [];
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                NotifyListVisualStateChanged();
            }
        }
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set
        {
            if (SetProperty(ref _isPointerOver, value))
            {
                NotifyListVisualStateChanged();
            }
        }
    }

    public double HoverLayerOpacity => IsPointerOver && !IsSelected ? 1 : 0;
    public double SelectedLayerOpacity => IsSelected ? 1 : 0;
    public double HoverIndicatorOpacity => IsPointerOver && !IsSelected ? 1 : 0;
    public double SelectedIndicatorOpacity => IsSelected ? 1 : 0;
    public Brush ListTextBrush => (Brush)Application.Current.Resources["PrimaryTextBrush"];
    public string OriginalName => Manifest?.Name ?? FolderName;
    public string? OriginalDescription => Manifest?.Description;
    public string OriginalDescriptionText => OriginalDescription ?? string.Empty;
    public Visibility OriginalDescriptionVisibility => string.IsNullOrWhiteSpace(OriginalDescription)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string TranslatedDescriptionText => TranslatedDescription?.Trim() ?? string.Empty;
    public Visibility TranslatedDescriptionVisibility => string.IsNullOrWhiteSpace(TranslatedDescription)
        || string.Equals(OriginalDescription?.Trim(), TranslatedDescription.Trim(), StringComparison.CurrentCultureIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
    public string DisplayName
    {
        get
        {
            var original = OriginalName;
            var translated = TranslatedName?.Trim();
            return string.IsNullOrWhiteSpace(translated)
                || string.Equals(original.Trim(), translated, StringComparison.CurrentCultureIgnoreCase)
                ? original
                : $"{original}/{translated}";
        }
    }

    public string DisplayDescription => string.IsNullOrWhiteSpace(TranslatedDescription)
        ? OriginalDescription ?? string.Empty
        : TranslatedDescription.Trim();
    public string Name => OriginalName;
    public string Author => Manifest?.Author ?? "未知作者";
    public string Version => Manifest?.Version ?? "-";
    public string UniqueId => Manifest?.UniqueID ?? "未识别";
    public string StatusText => IsArchived ? "已归档" : Manifest is null ? "无效" : !IsEnabled ? "已禁用" : Issues.Any(i => i.Severity == IssueSeverity.Error) ? "有问题" : Issues.Count > 0 ? "注意" : "正常";
    public bool CanRepairNestedDirectory => !string.IsNullOrWhiteSpace(SuggestedNestedDirectory);
    public string IssueSummary => Issues.Count == 0 ? string.Empty : Issues[0].Message;
    public Visibility IssueSummaryVisibility => Issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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

    private void NotifyListVisualStateChanged()
    {
        OnPropertyChanged(nameof(HoverLayerOpacity));
        OnPropertyChanged(nameof(SelectedLayerOpacity));
        OnPropertyChanged(nameof(HoverIndicatorOpacity));
        OnPropertyChanged(nameof(SelectedIndicatorOpacity));
        OnPropertyChanged(nameof(ListTextBrush));
    }
}
