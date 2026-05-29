using LSMA.Models;

namespace LSMA.Services;

public sealed class GuideDataService
{
    public IReadOnlyList<BirthdayRecord> Birthdays { get; } =
    [
        new() { NpcId = "Lewis", Npc = "刘易斯", Season = "春季", Day = 7, LovedGiftHint = "可准备容易取得的喜爱礼物" },
        new() { NpcId = "Haley", Npc = "海莉", Season = "春季", Day = 14, LovedGiftHint = "椰子或向日葵" },
        new() { NpcId = "Sam", Npc = "山姆", Season = "夏季", Day = 17, LovedGiftHint = "披萨" },
        new() { NpcId = "Abigail", Npc = "阿比盖尔", Season = "秋季", Day = 13, LovedGiftHint = "紫水晶" },
        new() { NpcId = "Sebastian", Npc = "塞巴斯蒂安", Season = "冬季", Day = 10, LovedGiftHint = "黑曜石" }
    ];

    public IReadOnlyList<FishRecord> Fish { get; } =
    [
        new() { ObjectId = 143, Name = "鲶鱼", Season = "春季 / 秋季", Weather = "雨天", Location = "河流", Time = "6:00 - 24:00", SortStartMinutes = 360, CommunityCenterNeeded = true },
        new() { ObjectId = 698, Name = "鲟鱼", Season = "夏季 / 冬季", Weather = "任意", Location = "山区湖泊", Time = "6:00 - 19:00", SortStartMinutes = 360, CommunityCenterNeeded = true },
        new() { ObjectId = 150, Name = "红鲷鱼", Season = "夏季 / 秋季", Weather = "雨天", Location = "海洋", Time = "6:00 - 19:00", SortStartMinutes = 360, CommunityCenterNeeded = true }
    ];

    public IReadOnlyList<CropRecord> Crops { get; } =
    [
        new() { ObjectId = 400, Name = "草莓", Season = "春季", SeedPrice = 100, GrowDays = 8, SalePrice = 120, RegrowDays = 4 },
        new() { ObjectId = 258, Name = "蓝莓", Season = "夏季", SeedPrice = 80, GrowDays = 13, SalePrice = 50, RegrowDays = 4 },
        new() { ObjectId = 282, Name = "蔓越莓", Season = "秋季", SeedPrice = 240, GrowDays = 7, SalePrice = 75, RegrowDays = 5 },
        new() { ObjectId = 498, Name = "冬季种子", Season = "冬季", SeedPrice = 0, GrowDays = 7, SalePrice = 0 }
    ];

    public IReadOnlyList<BundleRecord> Bundles { get; } =
    [
        new() { ObjectId = 24, Name = "春季作物收集包", Season = "春季", ItemHint = "防风草、青豆、花椰菜、土豆" },
        new() { ObjectId = 258, Name = "夏季作物收集包", Season = "夏季", ItemHint = "番茄、辣椒、蓝莓、甜瓜" },
        new() { ObjectId = 270, Name = "秋季作物收集包", Season = "秋季", ItemHint = "玉米、茄子、南瓜、山药" }
    ];

    public IReadOnlyList<NpcGiftRecord> NpcGifts => NpcGiftData.All;
}
