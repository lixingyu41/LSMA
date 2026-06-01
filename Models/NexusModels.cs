using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using LSMA.Utilities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LSMA.Models;

public sealed class NexusConnectionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public bool IsPremium { get; init; }
}

public sealed class NexusUserInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; set; }
}

public sealed class NexusModInfo : ObservableObject
{
    private bool _isSelected;
    private bool _isPointerOver;
    private bool _isFavorite;
    private bool _isInstalled;
    private string? _translatedName;
    private int _categoryId;
    private long _updatedTimestamp;
    private long _downloads;
    private string? _coverSourceUrl;
    private string? _coverImageUri;
    private string? _categoryName;

    [JsonPropertyName("mod_id")]
    public long ModId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("version")]
    public string Version { get; set; } = "-";
    [JsonPropertyName("author")]
    public string Author { get; set; } = "未知作者";
    [JsonPropertyName("category_id")]
    public int CategoryId
    {
        get => _categoryId;
        set
        {
            if (SetProperty(ref _categoryId, value))
            {
                OnPropertyChanged(nameof(CategoryText));
            }
        }
    }
    [JsonPropertyName("updated_timestamp")]
    public long UpdatedTimestamp
    {
        get => _updatedTimestamp;
        set
        {
            if (SetProperty(ref _updatedTimestamp, value))
            {
                OnPropertyChanged(nameof(UpdatedAt));
                OnPropertyChanged(nameof(UpdatedText));
            }
        }
    }
    [JsonPropertyName("endorsement_count")]
    public int Endorsements { get; set; }
    [JsonPropertyName("mod_downloads")]
    public long Downloads
    {
        get => _downloads;
        set
        {
            if (SetProperty(ref _downloads, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DownloadStatusText));
                OnPropertyChanged(nameof(DownloadsText));
            }
        }
    }
    [JsonPropertyName("picture_url")]
    public string? CoverSourceUrl
    {
        get => _coverSourceUrl;
        set => SetProperty(ref _coverSourceUrl, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }
    [JsonPropertyName("category_name")]
    public string? CategoryName
    {
        get => _categoryName;
        set
        {
            if (SetProperty(ref _categoryName, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
            {
                OnPropertyChanged(nameof(CategoryText));
            }
        }
    }
    public string? CoverImageUri
    {
        get => _coverImageUri;
        set
        {
            if (SetProperty(ref _coverImageUri, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                OnPropertyChanged(nameof(CoverImageVisibility));
                OnPropertyChanged(nameof(CoverPlaceholderVisibility));
            }
        }
    }
    public DateTime UpdatedAt => DateTimeOffset.FromUnixTimeSeconds(UpdatedTimestamp).LocalDateTime;
    public string CategoryText => NexusCategoryNameMapper.ToDisplayName(CategoryName, CategoryId);
    public string UpdatedText => UpdatedTimestamp > 0 ? $"更新 {UpdatedAt:yyyy-MM-dd}" : "更新 -";
    public string DownloadsText => $"下载 {Downloads:N0}";
    public Visibility CoverImageVisibility => string.IsNullOrWhiteSpace(CoverImageUri)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility CoverPlaceholderVisibility => string.IsNullOrWhiteSpace(CoverImageUri)
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                NotifyResultStateChanged();
            }
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                NotifyResultStateChanged();
            }
        }
    }

    public string ResultStateText
    {
        get
        {
            var states = new List<string>(2);
            if (IsInstalled)
            {
                states.Add("已安装");
            }

            if (IsFavorite)
            {
                states.Add("已收藏");
            }

            return string.Join(" · ", states);
        }
    }

    public Visibility ResultStateVisibility => string.IsNullOrWhiteSpace(ResultStateText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string? TranslatedName
    {
        get => _translatedName;
        set
        {
            if (SetProperty(ref _translatedName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName
    {
        get
        {
            var translated = TranslatedName?.Trim();
            return string.IsNullOrWhiteSpace(translated)
                || string.Equals(Name.Trim(), translated, StringComparison.CurrentCultureIgnoreCase)
                    ? Name
                    : $"{Name}/{translated}";
        }
    }

    public string ModIdText => $"ID {ModId}";
    public string StatusText => $"版本 {Version} · {Downloads:N0} 次下载";
    public string DownloadStatusText => $"{Downloads:N0} 次下载";
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

    private void NotifyListVisualStateChanged()
    {
        OnPropertyChanged(nameof(HoverLayerOpacity));
        OnPropertyChanged(nameof(SelectedLayerOpacity));
        OnPropertyChanged(nameof(HoverIndicatorOpacity));
        OnPropertyChanged(nameof(SelectedIndicatorOpacity));
        OnPropertyChanged(nameof(ListTextBrush));
    }

    private void NotifyResultStateChanged()
    {
        OnPropertyChanged(nameof(ResultStateText));
        OnPropertyChanged(nameof(ResultStateVisibility));
    }
}

public sealed class NexusModSearchResult
{
    public List<NexusModInfo> Mods { get; init; } = [];
    public int TotalCount { get; init; }
}

public sealed class NexusFilesResponse
{
    [JsonPropertyName("files")]
    public List<NexusFileInfo?>? Files { get; set; } = [];
}

public sealed class NexusFileInfo
{
    [JsonPropertyName("file_id")]
    public long FileId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }
    [JsonPropertyName("uploaded_timestamp")]
    public long UploadedTimestamp { get; set; }
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }
    public string DisplayText => $"{(string.IsNullOrWhiteSpace(Name) ? "未命名文件" : Name)} · {(string.IsNullOrWhiteSpace(Version) ? "-" : Version)}";
}

public sealed class NexusCategory
{
    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    public string? SearchName { get; set; }
    public string FilterName => string.IsNullOrWhiteSpace(SearchName) ? Name : SearchName;
}

public sealed class NexusSortOption
{
    public string Name { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public bool IsRandom => string.Equals(FieldName, "random", StringComparison.OrdinalIgnoreCase);
}

public sealed class NexusSortDirectionOption
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = "DESC";
}

public sealed class NexusDownloadLink
{
    [JsonPropertyName("URI")]
    public string Uri { get; set; } = string.Empty;
}

public sealed class NexusFavorite
{
    public long ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

public enum DownloadState
{
    Pending,
    Downloading,
    Downloaded,
    Failed,
    Canceled,
    AwaitingInstall,
    InstallFailed,
    Installed
}

public sealed class DownloadQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ModId { get; set; }
    public long FileId { get; set; }
    public string ModName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? LocalPath { get; set; }
    public DownloadState State { get; set; } = DownloadState.Pending;
    public string? Error { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public string StatusText => State switch
    {
        DownloadState.Downloading => "下载中",
        DownloadState.Downloaded => "已下载",
        DownloadState.AwaitingInstall => "待安装",
        DownloadState.InstallFailed => "安装失败",
        DownloadState.Installed => "已安装",
        DownloadState.Failed => "下载失败",
        DownloadState.Canceled => "已取消",
        _ => "待下载"
    };
}
