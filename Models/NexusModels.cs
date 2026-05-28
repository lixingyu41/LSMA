using System.Text.Json.Serialization;

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

public sealed class NexusModInfo
{
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
    public int CategoryId { get; set; }
    [JsonPropertyName("updated_timestamp")]
    public long UpdatedTimestamp { get; set; }
    [JsonPropertyName("endorsement_count")]
    public int Endorsements { get; set; }
    [JsonPropertyName("mod_downloads")]
    public long Downloads { get; set; }
    public DateTime UpdatedAt => DateTimeOffset.FromUnixTimeSeconds(UpdatedTimestamp).LocalDateTime;
    public bool IsFavorite { get; set; }
    public string StatusText => $"版本 {Version} · {Downloads:N0} 次下载";
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
        DownloadState.Installed => "已安装",
        DownloadState.Failed => "下载失败",
        DownloadState.Canceled => "已取消",
        _ => "待下载"
    };
}
