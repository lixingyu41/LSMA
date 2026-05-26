namespace LSMA.Models;

public enum HealthLevel
{
    Ready,
    Attention,
    Blocked
}

public sealed class LaunchStatus
{
    public HealthLevel Level { get; set; } = HealthLevel.Blocked;

    public string Title { get; set; } = "未连接游戏";

    public string Detail { get; set; } = "配置游戏目录后即可进行启动前检查。";
}

public sealed class SmapiLogSummary
{
    public bool HasLog { get; set; }

    public bool HasCrash { get; set; }

    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    public List<string> Highlights { get; set; } = [];

    public string? SourcePath { get; set; }

    public string? GameVersion { get; set; }

    public string? SmapiVersion { get; set; }

    public string MostLikelyCause { get; set; } = "未发现明确错误";

    public string RecommendedAction { get; set; } = "可以执行启动前检查后启动游戏。";

    public List<LogIssue> Issues { get; set; } = [];

    public string DisplaySummary => !HasLog
        ? "暂无可用日志"
        : HasCrash ? $"检测到崩溃，{ErrorCount} 个错误" : $"{ErrorCount} 个错误，{WarningCount} 个警告";
}

public sealed class LogIssue
{
    public int Priority { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public bool CanAutoFix { get; init; }
}

public sealed class LaunchCheckResult
{
    public bool CanLaunch { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool CanFallbackToVanilla { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
}
