using Microsoft.UI.Xaml;

namespace LSMA.Models;

public sealed class SaveInfo
{
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public string FarmerName { get; set; } = "未知角色";
    public string FarmName { get; set; } = "未知农场";
    public string GameVersion { get; set; } = "-";
    public string FarmType { get; set; } = "未知";
    public string Gender { get; set; } = "Male";
    public int FacingDirection { get; set; }
    public int Hair { get; set; }
    public int HairColorR { get; set; }
    public int HairColorG { get; set; }
    public int HairColorB { get; set; }
    public int ShirtIndex { get; set; } = -1;
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
    public long UniqueGameId { get; set; }
    public long StepsTaken { get; set; }
    public int FishCaught { get; set; }
    public int TimesFished { get; set; }
    public int SeedsSown { get; set; }
    public int TrashCansChecked { get; set; }
    public int TrashRecycled { get; set; }
    public int ItemsShipped { get; set; }
    public int MineralsFound { get; set; }
    public int ArtifactsFound { get; set; }
    public int CookedRecipes { get; set; }
    public int CraftedRecipes { get; set; }
    public int FishSpeciesCaught { get; set; }
    public int TotalMonsterKills { get; set; }
    public int ObelisksBuilt { get; set; }
    public bool HasGoldClock { get; set; }
    public int GoldenWalnutsFound { get; set; }
    public int PerfectionWaivers { get; set; }
    public int GoodFriends { get; set; }
    public int SkillLevelTotal { get; set; }
    public int FarmerLevelScore { get; set; }
    public int MasteryLevel { get; set; }
    public int MasteryExp { get; set; }
    public double PerfectionProgress { get; set; }
    public bool IsMonsterHero { get; set; }
    public int HouseUpgradeLevel { get; set; }
    public int StardropsFound { get; set; }
    public string? ParseError { get; set; }
    public DateTime? LatestBackup { get; set; }
    public string? SeasonIconUri { get; set; }
    public string? PortraitImageUri { get; set; }
    public string? BackgroundImageUri { get; set; }
    public List<SaveSkillInfo> Skills { get; } = [];
    public List<SaveFriendshipInfo> Friendships { get; } = [];
    public List<SaveProgressInfo> CollectionStats { get; } = [];
    public List<SaveProgressInfo> PerfectionStats { get; } = [];
    public List<SaveMetricInfo> MonsterKillStats { get; } = [];
    public List<SaveMetricInfo> FishCatchStats { get; } = [];
    public Dictionary<int, List<bool>> CommunityBundleStates { get; } = [];
    public HashSet<string> MailFlags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string DateDisplay => Year > 0 ? $"{Year} 年 {Season} {Day} 日" : "日期未知";
    public string ListDisplay => $"{FarmName}-{FarmerName}-{Year}年{Season}{Day}日";
    public string MoneyDisplay => $"{Money:N0}g";
    public string TotalIncomeDisplay => $"{TotalMoneyEarned:N0}g";
    public double AverageMoneyPerDay => TotalDays > 0 ? TotalMoneyEarned / (double)TotalDays : 0;
    public double MoneyPerSeasonEstimate => AverageMoneyPerDay * 28;
    public string AverageMoneyDisplay => $"{AverageMoneyPerDay:N0}g / 天";
    public string SeasonEstimateDisplay => $"{MoneyPerSeasonEstimate:N0}g / 季";
    public string CommunityCenterDisplay => $"{CommunityCenterProgress:0}%";
    public string CollectionDisplay => $"{CollectionProgress:0}%";
    public string UniqueGameIdDisplay => UniqueGameId > 0 ? UniqueGameId.ToString() : "-";
    public string TotalDaysDisplay => $"{TotalDays:N0} 天";
    public string StepsTakenDisplay => $"{StepsTaken:N0}";
    public string FishCaughtDisplay => $"{FishCaught:N0}";
    public string TimesFishedDisplay => $"{TimesFished:N0}";
    public string SeedsSownDisplay => $"{SeedsSown:N0}";
    public string TrashCansCheckedDisplay => $"{TrashCansChecked:N0}";
    public string TrashRecycledDisplay => $"{TrashRecycled:N0}";
    public string ItemsShippedDisplay => $"{ItemsShipped:N0}";
    public string HouseLevelDisplay => $"{HouseUpgradeLevel}/3";
    public string StardropsFoundDisplay => $"{StardropsFound}/7";
    public string GoldenWalnutsDisplay => $"{GoldenWalnutsFound}/130";
    public string FarmerLevelScoreDisplay => $"{FarmerLevelScore}/25";
    public string MasteryLevelDisplay => $"{MasteryLevel}/5";
    public string MasteryExpDisplay => $"{MasteryExp:N0}";
    public string PerfectionProgressDisplay => $"{Math.Floor(Math.Clamp(PerfectionProgress, 0, 100)):0}%";
    public string MineLevelDisplay => $"{MineLevel:N0} 层";
    public string SkullCavernLevelDisplay => $"{SkullCavernLevel:N0} 层";
    public string QiGemsDisplay => $"{QiGems:N0}";
    public string PlayTimeDisplay => TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalHours >= 1
        ? $"{TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalHours:0.0} 小时"
        : $"{TimeSpan.FromMilliseconds(PlayTimeMilliseconds).TotalMinutes:0} 分钟";
    public IReadOnlyList<SaveMetricInfo> SummaryStats =>
    [
        Metric("完美度", PerfectionProgressDisplay, "齐先生完美度总进度", "\uE8FB"),
        Metric("游戏时长", PlayTimeDisplay, "实际游玩时长", "\uE823"),
        Metric("总收入", TotalIncomeDisplay, "游戏开始以来累计获得的金币", "\uE9D2"),
        Metric("居住天数", TotalDaysDisplay, "从开局开始累计的居住天数", "\uE787")
    ];
    public IReadOnlyList<SaveMetricInfo> EconomyStats =>
    [
        Metric("当前金币", MoneyDisplay, "角色当前持有金币", "\uE8C7"),
        Metric("日均收入", AverageMoneyDisplay, "总收入 ÷ 居住天数", "\uE9D2"),
        Metric("季度估算", SeasonEstimateDisplay, "按当前日均收入估算 28 天收益", "\uE9D2"),
        Metric("齐宝石", QiGemsDisplay, "姜岛齐先生商店货币", "\uE970")
    ];
    public IReadOnlyList<SaveMetricInfo> ProgressStats =>
    [
        Metric("社区中心", CommunityCenterDisplay, "社区中心献祭包裹完成比例", "\uE73E"),
        Metric("收集", CollectionDisplay, "出货与收藏综合进度", "\uE8FD"),
        Metric("精通", MasteryLevelDisplay, $"精通经验 {MasteryExpDisplay}", "\uE735"),
        Metric("星之果实", StardropsFoundDisplay, "已找到的永久体力星之果实", "\uE735")
    ];
    public IReadOnlyList<SaveMetricInfo> FarmLifeStats =>
    [
        Metric("农场类型", FarmType, "当前存档选择的农场地图", "\uE81E"),
        Metric("配偶", Spouse, "当前结婚对象或室友", "\uE716"),
        Metric("宠物", Pet, "农场宠物类型", "\uE76E"),
        Metric("房屋等级", HouseLevelDisplay, "农舍升级进度", "\uE80F"),
        Metric("矿洞", MineLevelDisplay, "普通矿洞最深层数，最高 120 层", "\uE81C"),
        Metric("沙漠矿洞", SkullCavernLevelDisplay, "骷髅矿洞最深层数", "\uE7AC")
    ];
    public IReadOnlyList<SaveMetricInfo> ActivityStats =>
    [
        Metric("迈出步数", StepsTakenDisplay, "角色移动步数累计", "\uE805"),
        Metric("播种", SeedsSownDisplay, "累计种下的作物种子", "\uE8F1"),
        Metric("钓鱼", FishCaughtDisplay, $"抛竿 {TimesFishedDisplay} 次", "\uE7C5"),
        Metric("垃圾桶", TrashCansCheckedDisplay, $"回收垃圾 {TrashRecycledDisplay} 件", "\uE74D"),
        Metric("已出货", ItemsShippedDisplay, "已登记到收藏的出货物品数量", "\uE7BF")
    ];

    private static SaveMetricInfo Metric(string name, string value, string detail, string glyph)
        => new()
        {
            Name = name,
            Value = value,
            Detail = detail,
            Glyph = glyph
        };
}

public sealed class SaveProgressInfo
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class SaveMetricInfo
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE946";
    public int? ObjectId { get; init; }
    public string? IconTexture { get; init; }
    public int? IconSpriteIndex { get; init; }
    public int IconWidth { get; init; } = 16;
    public int IconHeight { get; init; } = 16;
    public string? IconUri { get; set; }
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IconVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => string.IsNullOrWhiteSpace(Glyph) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackGlyphVisibility => string.IsNullOrWhiteSpace(IconUri) && !string.IsNullOrWhiteSpace(Glyph)
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class SaveSkillInfo
{
    private static readonly int[] ExperienceThresholds = [0, 100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000];

    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Experience { get; init; }
    public string? IconUri { get; set; }
    public int MaxLevel { get; init; } = 10;
    public bool UseExperienceProgress { get; init; } = true;
    public bool IsMaxLevel => Level >= MaxLevel;
    public int CurrentThreshold => ExperienceThresholds[Math.Clamp(Level, 0, 10)];
    public int NextThreshold => ExperienceThresholds[Math.Clamp(Level + 1, 0, 10)];
    public int ExperienceIntoLevel => Math.Max(0, Experience - CurrentThreshold);
    public int ExperienceForLevel => Math.Max(0, NextThreshold - CurrentThreshold);
    public int ExperienceRemaining => IsMaxLevel ? 0 : Math.Max(0, NextThreshold - Experience);
    public double Percent => !UseExperienceProgress
        ? Math.Clamp(Level / (double)Math.Max(1, MaxLevel) * 100, 0, 100)
        : IsMaxLevel || ExperienceForLevel == 0
        ? 100
        : Math.Clamp(ExperienceIntoLevel / (double)ExperienceForLevel * 100, 0, 100);
    public string LevelText => MaxLevel == 5 ? $"{Level}/5" : $"{Level} 级";
    public string ExperienceText => !UseExperienceProgress
        ? $"{Experience:N0} 精通经验"
        : IsMaxLevel
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
}

public sealed class SaveFriendshipHeart
{
    public string? IconUri { get; init; }
    public string? FullIconUri { get; init; }
    public bool IsLocked { get; init; }
    public bool IsPartial { get; init; }
    public double FillPercent { get; init; }
    public double HeartOpacity => IsLocked ? 0.3 : 1.0;
    public Visibility PartialVisibility => IsPartial ? Visibility.Visible : Visibility.Collapsed;
}
