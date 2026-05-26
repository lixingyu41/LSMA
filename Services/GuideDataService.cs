using LSMA.Models;

namespace LSMA.Services;

public sealed class GuideDataService
{
    public IReadOnlyList<BirthdayRecord> Birthdays { get; } =
    [
        new() { Npc = "Lewis", Season = "春季", Day = 7, LovedGiftHint = "可准备容易取得的喜爱礼物" },
        new() { Npc = "Haley", Season = "春季", Day = 14, LovedGiftHint = "椰子或向日葵" },
        new() { Npc = "Sam", Season = "夏季", Day = 17, LovedGiftHint = "披萨" },
        new() { Npc = "Abigail", Season = "秋季", Day = 13, LovedGiftHint = "紫水晶" },
        new() { Npc = "Sebastian", Season = "冬季", Day = 10, LovedGiftHint = "黑曜石" }
    ];

    public IReadOnlyList<FishRecord> Fish { get; } =
    [
        new() { Name = "鲶鱼", Season = "春季 / 秋季", Weather = "雨天", Location = "河流", Time = "6:00 - 24:00", CommunityCenterNeeded = true },
        new() { Name = "鲟鱼", Season = "夏季 / 冬季", Weather = "任意", Location = "山区湖泊", Time = "6:00 - 19:00", CommunityCenterNeeded = true },
        new() { Name = "红鲷鱼", Season = "夏季 / 秋季", Weather = "雨天", Location = "海洋", Time = "6:00 - 19:00", CommunityCenterNeeded = true }
    ];

    public IReadOnlyList<CropRecord> Crops { get; } =
    [
        new() { Name = "草莓", Season = "春季", SeedPrice = 100, GrowDays = 8, SalePrice = 120, RegrowDays = 4 },
        new() { Name = "蓝莓", Season = "夏季", SeedPrice = 80, GrowDays = 13, SalePrice = 50, RegrowDays = 4 },
        new() { Name = "蔓越莓", Season = "秋季", SeedPrice = 240, GrowDays = 7, SalePrice = 75, RegrowDays = 5 },
        new() { Name = "冬季种子", Season = "冬季", SeedPrice = 0, GrowDays = 7, SalePrice = 0 }
    ];

    public IReadOnlyList<BundleRecord> Bundles { get; } =
    [
        new() { Name = "春季作物收集包", Season = "春季", ItemHint = "防风草、青豆、花椰菜、土豆" },
        new() { Name = "夏季作物收集包", Season = "夏季", ItemHint = "番茄、辣椒、蓝莓、甜瓜" },
        new() { Name = "秋季作物收集包", Season = "秋季", ItemHint = "玉米、茄子、南瓜、山药" }
    ];

    public IReadOnlyList<RecipeRecord> Recipes { get; } =
    [
        new() { Name = "田野零食", Materials = "橡树种子、枫树种子、松果", Acquisition = "采集等级提升后制作" },
        new() { Name = "生命药水", Materials = "蘑菇材料", Acquisition = "战斗探索准备" },
        new() { Name = "洒水器", Materials = "金属锭", Acquisition = "耕种等级提升后制作" }
    ];
}
