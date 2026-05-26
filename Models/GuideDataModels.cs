namespace LSMA.Models;

public abstract class GuideRecord
{
    public string Source { get; init; } = "Stardew Valley Wiki (CC BY-NC-SA 3.0)";
    public string GameVersion { get; init; } = "1.6";
}

public sealed class BirthdayRecord : GuideRecord
{
    public string Npc { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int Day { get; init; }
    public string LovedGiftHint { get; init; } = string.Empty;
    public string DateText => $"{Season} {Day} 日";
}

public sealed class FishRecord : GuideRecord
{
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string Weather { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public bool CommunityCenterNeeded { get; init; }
    public string Detail => $"{Season} · {Weather} · {Location} · {Time}";
}

public sealed class CropRecord : GuideRecord
{
    public string Name { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int SeedPrice { get; init; }
    public int GrowDays { get; init; }
    public int SalePrice { get; init; }
    public int? RegrowDays { get; init; }
    public string Detail => $"{Season} · 成熟 {GrowDays} 天 · 售价 {SalePrice}g";
}

public sealed class BundleRecord : GuideRecord
{
    public string Name { get; init; } = string.Empty;
    public string ItemHint { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string Detail => $"{Season} 可准备：{ItemHint}";
}

public sealed class RecipeRecord : GuideRecord
{
    public string Name { get; init; } = string.Empty;
    public string Materials { get; init; } = string.Empty;
    public string Acquisition { get; init; } = string.Empty;
    public string Detail => $"{Materials} · {Acquisition}";
}
