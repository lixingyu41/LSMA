namespace LSMA.Models;

public sealed class SaveInfo
{
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public string FarmerName { get; set; } = "未知角色";
    public string FarmName { get; set; } = "未知农场";
    public string GameVersion { get; set; } = "-";
    public string FarmType { get; set; } = "未知";
    public int Year { get; set; }
    public string Season { get; set; } = "-";
    public int Day { get; set; }
    public int TotalDays { get; set; }
    public int Money { get; set; }
    public long TotalMoneyEarned { get; set; }
    public int PlayTimeMilliseconds { get; set; }
    public string? Weather { get; set; }
    public string Spouse { get; set; } = "无";
    public string Pet { get; set; } = "未识别";
    public int MineLevel { get; set; }
    public int SkullCavernLevel { get; set; }
    public int QiGems { get; set; }
    public double CommunityCenterProgress { get; set; }
    public double CollectionProgress { get; set; }
    public string? ParseError { get; set; }
    public DateTime? LatestBackup { get; set; }
    public List<SaveSkillInfo> Skills { get; } = [];
    public List<SaveFriendshipInfo> Friendships { get; } = [];
    public string DateDisplay => Year > 0 ? $"{Year} 年 {Season} {Day} 日" : "日期未知";
    public string MoneyDisplay => $"{Money:N0}g";
    public string TotalIncomeDisplay => $"{TotalMoneyEarned:N0}g";
    public string ProgressDisplay => $"版本 {GameVersion} · 总天数 {TotalDays}";
    public string HouseholdDisplay => $"农场类型：{FarmType} · 配偶：{Spouse} · 宠物：{Pet}";
    public string ExplorationDisplay => $"矿洞 {MineLevel} 层 · 沙漠矿洞 {SkullCavernLevel} 层 · Qi 宝石 {QiGems}";
    public int RemainingDaysInSeason => Math.Max(0, 28 - Day);
    public double AverageMoneyPerDay => TotalDays > 0 ? TotalMoneyEarned / (double)TotalDays : 0;
    public double MoneyPerSeasonEstimate => AverageMoneyPerDay * 28;
    public double MoneyPerYearEstimate => AverageMoneyPerDay * 112;
    public string AverageMoneyDisplay => $"{AverageMoneyPerDay:N0}g / 天";
    public string SeasonEstimateDisplay => $"{MoneyPerSeasonEstimate:N0}g / 季";
    public string CommunityCenterDisplay => $"{CommunityCenterProgress:0}%";
    public string CollectionDisplay => $"{CollectionProgress:0}%";
    public string PlayTimeDisplay => TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalHours >= 1
        ? $"{TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalHours:0.0} 小时"
        : $"{TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalMinutes:0} 分钟";
}

public sealed class SaveSkillInfo
{
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public double Percent => Math.Clamp(Level / 10d * 100, 0, 100);
}

public sealed class SaveFriendshipInfo
{
    public string Name { get; init; } = string.Empty;
    public int Points { get; init; }
    public int Hearts => Math.Min(14, Points / 250);
    public string HeartsDisplay => $"{Hearts} 心";
    public double Percent => Math.Clamp(Points / 3500d * 100, 0, 100);
}
