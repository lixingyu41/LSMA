namespace LSMA.Models;

public sealed class TodaySuggestion
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
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
