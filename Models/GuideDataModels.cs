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
    public string? IconUri { get; set; }
    public string DateText => $"{Season} {Day} 日";
    public string CountdownText { get; init; } = string.Empty;
}

public sealed class BirthdayRecord : GuideRecord
{
    public string NpcId { get; init; } = string.Empty;
    public string Npc { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int Day { get; init; }
    public string LovedGiftHint { get; init; } = string.Empty;
    public string? IconUri { get; set; }
}

public sealed class FishRecord : GuideRecord
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string Weather { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public bool CommunityCenterNeeded { get; init; }
    public string? IconUri { get; set; }
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
    public int? RegrowDays { get; init; }
    public string? IconUri { get; set; }
    public int TillerPrice => (int)(SalePrice * 1.1);
    public int WinePrice => SalePrice * 3;
    public int ArtisanWinePrice => (int)(WinePrice * 1.4);
    public int JellyPrice => SalePrice * 2 + 50;
    public int ArtisanJellyPrice => (int)(JellyPrice * 1.4);
    public string GrowthText => RegrowDays is { } days
        ? $"{Season} · 成熟 {GrowDays} 天 · 再生 {days} 天"
        : $"{Season} · 成熟 {GrowDays} 天";
    public string RawPriceText => $"原料 {SalePrice}g · 农耕人 {TillerPrice}g";
    public string ArtisanPriceText => $"果酒 {WinePrice}g / 工匠 {ArtisanWinePrice}g · 果酱 {JellyPrice}g / 工匠 {ArtisanJellyPrice}g";
}

public sealed class BundleRecord : GuideRecord
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ItemHint { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string? IconUri { get; set; }
    public string Detail => $"{Season} 可准备：{ItemHint}";
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
    public string? IconUri { get; set; }
    public Visibility ImageVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => string.IsNullOrWhiteSpace(IconUri) ? Visibility.Visible : Visibility.Collapsed;
    public string IconGlyph => Category switch
    {
        "人物" => "\uE77B",
        "节日" => "\uE787",
        "鱼类" => "\uE7FC",
        _ => "\uE7C3"
    };
}
