using System.Text.RegularExpressions;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed partial class SmapiLogService(LoggingService logging)
{
    public async Task<SmapiLogSummary> AnalyzeLatestAsync()
    {
        var source = GetLatestLogPath();
        if (source is null)
        {
            return new SmapiLogSummary();
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(source);
            var issues = IdentifyIssues(lines);
            var primary = issues.FirstOrDefault();
            return new SmapiLogSummary
            {
                HasLog = true,
                SourcePath = source,
                HasCrash = Path.GetFileName(source).Equals("SMAPI-crash.txt", StringComparison.OrdinalIgnoreCase)
                    || lines.Any(line => line.Contains("game has ended unexpectedly", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("fatal", StringComparison.OrdinalIgnoreCase)),
                ErrorCount = lines.Count(IsErrorLine),
                WarningCount = lines.Count(IsWarningLine),
                GameVersion = FindVersion(lines, GameVersionRegex()),
                SmapiVersion = FindVersion(lines, SmapiVersionRegex()),
                Issues = issues,
                Highlights = issues.Take(4).Select(issue => issue.Summary).ToList(),
                MostLikelyCause = primary?.Summary ?? "未发现明确错误",
                RecommendedAction = primary?.Recommendation ?? "可以执行启动前检查后启动游戏。"
            };
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取 SMAPI 日志失败", exception);
            return new SmapiLogSummary();
        }
    }

    public string CreateReport(SmapiLogSummary summary, GameDirectoryState? directory)
    {
        var lines = new List<string>
        {
            "# LSMA 启动前诊断报告",
            string.Empty,
            $"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}",
            $"- 连接状态：{(directory is null ? "未连接游戏目录" : "游戏目录已连接")}",
            $"- 游戏版本：{summary.GameVersion ?? "未知"}",
            $"- SMAPI 版本：{summary.SmapiVersion ?? "未知"}",
            $"- 日志摘要：{summary.DisplaySummary}",
            string.Empty,
            "## 结论",
            summary.MostLikelyCause,
            string.Empty,
            "## 建议",
            summary.RecommendedAction
        };
        if (summary.Issues.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## 发现的问题");
            foreach (var issue in summary.Issues.Take(10))
            {
                lines.Add($"- **{issue.Category}**：{issue.Summary}");
                lines.Add($"  - 建议：{issue.Recommendation}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<LogIssue> IdentifyIssues(string[] lines)
    {
        var rules = new[]
        {
            new Rule(1, "崩溃", ["fatal", "game has ended unexpectedly", "unhandled exception"], "上次启动发生崩溃。", "先禁用出错模组或恢复上次稳定状态，再启动。", false),
            new Rule(2, "缺少前置", ["required mod", "missing dependencies", "needs the '", "requires"], "检测到缺少必要前置模组。", "在模组页查看缺失前置并确认安装计划。", false),
            new Rule(3, "加载失败", ["failed to load"], "有模组未能加载。", "在模组页检查该模组程序文件与依赖。", true),
            new Rule(4, "程序文件错误", ["could not load file or assembly", "dll"], "模组程序文件或依赖库加载失败。", "更新或禁用关联模组后再次检查。", true),
            new Rule(5, "兼容补丁错误", ["harmony"], "模组补丁冲突或加载失败。", "优先更新相关模组，仍失败时暂时禁用。", true),
            new Rule(6, "重复模组", ["duplicate", "already registered"], "检测到重复模组识别名。", "保留一个版本并归档重复项。", true),
            new Rule(7, "不兼容", ["incompatible"], "存在与当前版本不兼容的模组。", "检查更新或禁用不兼容模组。", true),
            new Rule(8, "SMAPI 版本", ["newer version of smapi", "requires smapi"], "某些模组需要更高版本 SMAPI。", "更新 SMAPI 后重新检查。", false),
            new Rule(9, "游戏版本", ["newer version of stardew", "requires game"], "某些模组需要更高游戏版本。", "更新游戏或禁用相关模组。", false),
            new Rule(10, "配置错误", ["json", "parse"], "模组配置或内容数据无法读取。", "备份后重置相关配置或更新模组。", false)
        };
        var issues = new List<LogIssue>();
        foreach (var rule in rules)
        {
            var line = lines.FirstOrDefault(value => rule.Terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)));
            if (line is not null)
            {
                issues.Add(new LogIssue
                {
                    Priority = rule.Priority,
                    Category = rule.Category,
                    Summary = rule.Summary,
                    Excerpt = TrimSummary(line),
                    Recommendation = rule.Recommendation,
                    CanAutoFix = rule.CanAutoFix
                });
            }
        }

        return issues.OrderBy(issue => issue.Priority).ToList();
    }

    private static string? GetLatestLogPath()
    {
        foreach (var directory in AppPaths.SmapiLogSources.Where(Directory.Exists))
        {
            var crash = Path.Combine(directory, "SMAPI-crash.txt");
            if (File.Exists(crash))
            {
                return crash;
            }
        }

        foreach (var directory in AppPaths.SmapiLogSources.Where(Directory.Exists))
        {
            var latest = Path.Combine(directory, "SMAPI-latest.txt");
            if (File.Exists(latest))
            {
                return latest;
            }
        }

        return AppPaths.SmapiLogSources
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.txt"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsErrorLine(string line)
        => line.Contains("[Error", StringComparison.OrdinalIgnoreCase)
            || line.Contains(" fatal ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Error", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarningLine(string line)
        => line.Contains("[Warn", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Warning", StringComparison.OrdinalIgnoreCase);

    private static string? FindVersion(IEnumerable<string> lines, Regex pattern)
    {
        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string TrimSummary(string source)
    {
        var compact = source.Trim();
        return compact.Length <= 180 ? compact : compact[..177] + "...";
    }

    private sealed record Rule(
        int Priority,
        string Category,
        string[] Terms,
        string Summary,
        string Recommendation,
        bool CanAutoFix);

    [GeneratedRegex(@"Stardew Valley\s+([0-9][^\s,;]*)", RegexOptions.IgnoreCase)]
    private static partial Regex GameVersionRegex();

    [GeneratedRegex(@"SMAPI\s+([0-9][^\s,;]*)", RegexOptions.IgnoreCase)]
    private static partial Regex SmapiVersionRegex();
}
