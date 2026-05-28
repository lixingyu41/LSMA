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
    public string? SeasonIconUri { get; set; }
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
    private static readonly int[] ExperienceThresholds = [0, 100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000];

    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Experience { get; init; }
    public string? IconUri { get; set; }
    public bool IsMaxLevel => Level >= 10;
    public int CurrentThreshold => ExperienceThresholds[Math.Clamp(Level, 0, 10)];
    public int NextThreshold => ExperienceThresholds[Math.Clamp(Level + 1, 0, 10)];
    public int ExperienceIntoLevel => Math.Max(0, Experience - CurrentThreshold);
    public int ExperienceForLevel => Math.Max(0, NextThreshold - CurrentThreshold);
    public int ExperienceRemaining => IsMaxLevel ? 0 : Math.Max(0, NextThreshold - Experience);
    public double Percent => IsMaxLevel || ExperienceForLevel == 0
        ? 100
        : Math.Clamp(ExperienceIntoLevel / (double)ExperienceForLevel * 100, 0, 100);
    public string LevelText => $"{Level} 级";
    public string ExperienceText => IsMaxLevel
        ? $"{Experience:N0} 经验 · 已满级"
        : $"{ExperienceIntoLevel:N0} / {ExperienceForLevel:N0} 经验 · 距下级 {ExperienceRemaining:N0}";
}

public sealed class SaveFriendshipInfo
{
    public string NpcId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Points { get; init; }
    public string Status { get; init; } = "Friendly";
    public bool IsDatable { get; init; }
    public string? IconUri { get; set; }
    public List<SaveFriendshipHeart> HeartSlots { get; } = [];
    public bool IsPartner => Status is "Dating" or "Engaged" or "Married" or "Roommate";
    public bool IsSpouse => Status is "Married" or "Roommate";
    public int MaximumHearts => IsSpouse ? 14 : IsDatable && !IsPartner ? 8 : 10;
    public int Hearts => Math.Min(MaximumHearts, Points / 250);
    public string RelationshipText => Status switch
    {
        "Dating" => "恋爱中",
        "Engaged" => "订婚",
        "Married" => "配偶",
        "Roommate" => "室友",
        _ when IsDatable => "单身",
        _ => string.Empty
    };
    public string HeartsDisplay => $"{Hearts} / {MaximumHearts}";
}

public sealed class SaveFriendshipHeart
{
    public string? IconUri { get; init; }
    public bool IsLocked { get; init; }
}
