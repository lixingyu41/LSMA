using Microsoft.UI.Xaml;

namespace LSMA.Models;

public abstract class GuideRecord
{
    public string Source { get; init; } = "本机 Stardew Valley 内容数据";
    public string GameVersion { get; init; } = "1.6";
}

public sealed class FestivalRecord : GuideRecord
{
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int Day { get; init; }
    public bool IsPast { get; init; }
    public string? IconUri { get; set; }
    public string DateText => $"{Season} {Day} 日";
    public string CountdownText { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string StatusText => IsPast ? "刚过" : "将到";
    public string SearchQuery => Name;
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class BirthdayRecord : GuideRecord
{
    public string NpcId { get; init; } = string.Empty;
    public string Npc { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int Day { get; init; }
    public string LovedGiftHint { get; init; } = string.Empty;
    public string? IconUri { get; set; }
    public string SearchQuery => Npc;
}

public sealed class FishRecord : GuideRecord
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string Weather { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public int SortStartMinutes { get; init; }
    public int SalePrice { get; init; }
    public bool IsLegendary { get; init; }
    public bool CommunityCenterNeeded { get; init; }
    public string? IconUri { get; set; }
    public string SearchQuery => Name;
    public int FisherPrice => (int)(SalePrice * 1.25);
    public int AnglerPrice => (int)(SalePrice * 1.5);
    public int SmokedPrice => SalePrice * 2;
    public int SmokedArtisanPrice => (int)(SmokedPrice * 1.4);
    public string CategoryText => string.IsNullOrWhiteSpace(Location)
        ? Time
        : $"{Time} · {Location}";
    public string SeasonWeatherText => $"{Season} · {Weather}";
    public string PriceText => $"售价 {SalePrice}g · 渔夫 {FisherPrice}g · 垂钓者 {AnglerPrice}g";
    public string SmokedPriceText => $"熏鱼 {SmokedPrice}g · 工匠熏鱼 {SmokedArtisanPrice}g";
    public string BadgeText => IsLegendary ? "鱼王" : CommunityCenterNeeded ? "社区中心" : string.Empty;
    public Visibility BadgeVisibility => string.IsNullOrWhiteSpace(BadgeText) ? Visibility.Collapsed : Visibility.Visible;
    public string Detail => string.IsNullOrWhiteSpace(Location)
        ? $"{Weather} · {Time}"
        : $"{Weather} · {Location} · {Time}";
}

public sealed class CropRecord : GuideRecord
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int SeedPrice { get; init; }
    public int GrowDays { get; init; }
    public int SalePrice { get; init; }
    public int HarvestMinStack { get; init; } = 1;
    public int? RegrowDays { get; init; }
    public string? IconUri { get; set; }
    public string SearchQuery => Name;
    public int HarvestValue => SalePrice * HarvestMinStack;
    public int TillerPrice => (int)(HarvestValue * 1.1);
    public int WinePrice => SalePrice * 3 * HarvestMinStack;
    public int ArtisanWinePrice => (int)(WinePrice * 1.4);
    public int JellyPrice => (SalePrice * 2 + 50) * HarvestMinStack;
    public int ArtisanJellyPrice => (int)(JellyPrice * 1.4);
    public string GrowthText => RegrowDays is { } days
        ? $"{Season} · 成熟 {GrowDays} 天 · 再生 {days} 天"
        : $"{Season} · 成熟 {GrowDays} 天";
    public string HarvestText => HarvestMinStack > 1 ? $"每次收获 {HarvestMinStack} 个" : "每次收获 1 个";
    public int RawProfit => HarvestValue - SeedPrice;
    public string ProfitText => $"种子 {SeedPrice}g · 单株毛利 {RawProfit}g";
    public string RawPriceText => $"收获 {HarvestValue}g · 农耕人 {TillerPrice}g";
    public string ArtisanPriceText => $"果酒 {WinePrice}g / 工匠 {ArtisanWinePrice}g · 果酱 {JellyPrice}g / 工匠 {ArtisanJellyPrice}g";
}

public sealed class BundleRecord : GuideRecord
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ItemHint { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int CompletedCount { get; init; }
    public int RequiredCount { get; init; }
    public int MissingCount => Math.Max(0, RequiredCount - CompletedCount);
    public bool IsComplete => RequiredCount > 0 && CompletedCount >= RequiredCount;
    public string? IconUri { get; set; }
    public string SearchQuery => Name;
    public string Detail => IsComplete
        ? "已完成"
        : MissingCount > 0
            ? $"还差 {MissingCount} 项：{ItemHint}"
            : ItemHint;
    public string ProgressText => RequiredCount > 0 ? $"{CompletedCount}/{RequiredCount}" : Season;
}

public sealed class NpcGiftRecord : GuideRecord
{
    public string NpcId { get; init; } = string.Empty;
    public string Npc { get; init; } = string.Empty;
    public string Birthday { get; init; } = string.Empty;
    public string Loves { get; init; } = string.Empty;
    public string Likes { get; init; } = string.Empty;
    public string Neutral { get; init; } = string.Empty;
    public string Dislikes { get; init; } = string.Empty;
    public string Hates { get; init; } = string.Empty;
    public string? IconUri { get; set; }
    public string SearchQuery => Npc;
}

public sealed class GuideSearchResult
{
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int? ObjectId { get; init; }
    public string? NpcId { get; init; }
    public string? IconTexture { get; init; }
    public int? IconSpriteIndex { get; init; }
    public int IconWidth { get; init; } = 16;
    public int IconHeight { get; init; } = 16;
    public string? IconUri { get; set; }
    public List<GuideSearchSection> Sections { get; } = [];
    public string SearchQuery => Title;
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ImageVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SectionsVisibility => Sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string IconGlyph => Category switch
    {
        "人物" => "\uE77B",
        "节日" => "\uE787",
        "地点" => "\uE81C",
        "技能" => "\uE7BE",
        "机制" => "\uE9D9",
        "礼物" => "\uEB52",
        "鱼类" => "\uE7FC",
        "菜谱" => "\uE8D4",
        "配方" => "\uE9CE",
        _ => "\uE7C3"
    };
}

public sealed class GuideSearchSection
{
    public string Title { get; init; } = string.Empty;
    public List<string> Lines { get; } = [];
    public List<GuideSearchAction> Actions { get; } = [];
    public Visibility LinesVisibility => Lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActionsVisibility => Actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class GuideSearchAction
{
    public string Label { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int? ObjectId { get; init; }
    public string? IconTexture { get; init; }
    public int? IconSpriteIndex { get; init; }
    public int IconWidth { get; init; } = 16;
    public int IconHeight { get; init; } = 16;
    public string? IconUri { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(Detail) ? Label : $"{Label} · {Detail}";
    public Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ImageVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Visible : Visibility.Collapsed;
    public string IconGlyph => "\uE7C3";
}
