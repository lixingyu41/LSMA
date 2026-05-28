namespace LSMA.Models;

public sealed class TodaySuggestion
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? IconUri { get; init; }
    public string IconGlyph => Category switch
    {
        "日历" => "\uE787",
        "生日" => "\uEB52",
        "天气" => "\uE706",
        "钓鱼" => "\uE7FC",
        "种植" => "\uE8BE",
        "技能" => "\uE735",
        "好感" => "\uEB52",
        "安全" => "\uE72E",
        _ => "\uE946"
    };
}

public sealed class BackupRecord
{
    public string ItemName { get; init; } = string.Empty;
    public string ZipPath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string Operation { get; init; } = string.Empty;
    public bool Succeeded { get; set; }
}

public sealed class SaveBackupEntry
{
    public string Path { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm} · {System.IO.Path.GetFileName(Path)}";
}
