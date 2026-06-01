namespace LSMA.Utilities;

public static class NexusCategoryNameMapper
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Audio"] = "音频",
        ["Buildings"] = "建筑",
        ["Characters"] = "角色",
        ["New Characters"] = "新角色",
        ["Cheats"] = "作弊",
        ["Clothing"] = "服装",
        ["Crafting"] = "制作",
        ["Crops"] = "作物",
        ["Dialogue"] = "对话",
        ["Events"] = "事件",
        ["Expansions"] = "扩展",
        ["Fishing"] = "钓鱼",
        ["Furniture"] = "家具",
        ["Gameplay Mechanics"] = "玩法机制",
        ["Interiors"] = "室内",
        ["Items"] = "物品",
        ["Livestock and Animals"] = "牲畜和动物",
        ["Locations"] = "地点",
        ["Maps"] = "地图",
        ["Miscellaneous"] = "杂项",
        ["Modding Tools"] = "模组工具",
        ["Pets / Horses"] = "宠物/马",
        ["Player"] = "玩家",
        ["Portraits"] = "肖像",
        ["User Interface"] = "用户界面",
        ["Visuals and Graphics"] = "视觉和图形",
    };

    private static readonly IReadOnlyDictionary<int, string> NamesById = new Dictionary<int, string>
    {
        [1] = "音频",
        [2] = "建筑",
        [3] = "角色",
        [4] = "新角色",
        [5] = "作弊",
        [6] = "服装",
        [7] = "制作",
        [8] = "作物",
        [9] = "对话",
        [10] = "事件",
        [11] = "扩展",
        [12] = "钓鱼",
        [13] = "家具",
        [14] = "玩法机制",
        [15] = "室内",
        [16] = "物品",
        [17] = "牲畜和动物",
        [18] = "地点",
        [19] = "地图",
        [20] = "杂项",
        [21] = "模组工具",
        [22] = "宠物/马",
        [23] = "玩家",
        [24] = "肖像",
        [25] = "用户界面",
        [26] = "视觉和图形",
    };

    public static string ToDisplayName(string? categoryName, int categoryId = 0, string fallback = "未分类")
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var trimmed = categoryName.Trim();
            return Names.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
        }

        return NamesById.TryGetValue(categoryId, out var name) ? name : fallback;
    }
}
