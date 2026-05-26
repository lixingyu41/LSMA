using System.Xml.Linq;
using LSMA.Models;

namespace LSMA.Services;

public sealed class SaveParserService(LoggingService logging)
{
    public async Task<SaveInfo?> ParseAsync(SaveSource source)
    {
        try
        {
            return await Task.Run(() => Parse(source));
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"解析存档失败：{source.FolderName}", exception);
            return new SaveInfo
            {
                FolderPath = source.DirectoryPath,
                FolderName = source.FolderName,
                ParseError = "此存档无法读取，请使用备份恢复或在游戏中检查存档。"
            };
        }
    }

    private static SaveInfo Parse(SaveSource source)
    {
        var document = XDocument.Load(source.FilePath, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("存档 XML 为空。");
        var player = First(root, "player") ?? root;
        var save = new SaveInfo
        {
            FolderPath = source.DirectoryPath,
            FolderName = source.FolderName,
            FarmerName = Value(player, "name") ?? "未知角色",
            FarmName = Value(player, "farmName") ?? "未知农场",
            FarmType = FarmTypeName(Integer(root, "whichFarm")),
            GameVersion = Value(root, "gameVersion") ?? Value(root, "version") ?? "-",
            Year = Integer(root, "year"),
            Season = SeasonName(Value(root, "currentSeason")),
            Day = Integer(root, "dayOfMonth"),
            TotalDays = Integer(root, "daysPlayed", "stats"),
            Money = Integer(player, "money"),
            TotalMoneyEarned = Long(player, "totalMoneyEarned"),
            PlayTimeMilliseconds = Integer(player, "millisecondsPlayed"),
            Weather = WeatherText(root),
            Spouse = Value(player, "spouse") is { Length: > 0 } spouse ? spouse : "无",
            Pet = bool.TryParse(Value(player, "catPerson"), out var hasCat) ? (hasCat ? "猫" : "狗") : "未识别",
            MineLevel = Integer(player, "deepestMineLevel"),
            SkullCavernLevel = Integer(root, "skullCavesDifficulty"),
            QiGems = CountInventoryItem(player, "(O)858"),
            CommunityCenterProgress = CalculateBundleProgress(root),
            CollectionProgress = CalculateCollectionProgress(player)
        };

        save.Skills.AddRange(
        [
            new SaveSkillInfo { Name = "耕种", Level = Integer(player, "farmingLevel") },
            new SaveSkillInfo { Name = "采矿", Level = Integer(player, "miningLevel") },
            new SaveSkillInfo { Name = "采集", Level = Integer(player, "foragingLevel") },
            new SaveSkillInfo { Name = "钓鱼", Level = Integer(player, "fishingLevel") },
            new SaveSkillInfo { Name = "战斗", Level = Integer(player, "combatLevel") }
        ]);

        var friendshipData = First(player, "friendshipData");
        if (friendshipData is not null)
        {
            foreach (var item in friendshipData.Elements().Where(element => element.Name.LocalName == "item"))
            {
                var name = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "string")?.Value;
                var pointsElement = item.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("Points", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(name) && int.TryParse(pointsElement?.Value, out var points))
                {
                    save.Friendships.Add(new SaveFriendshipInfo { Name = name, Points = points });
                }
            }
        }

        var ordered = save.Friendships.OrderByDescending(value => value.Points).Take(10).ToList();
        save.Friendships.Clear();
        save.Friendships.AddRange(ordered);
        return save;
    }

    private static XElement? First(XElement root, string name)
    {
        return root.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Value(XElement root, string name)
    {
        return First(root, name)?.Value;
    }

    private static int Integer(XElement root, string name, string? nested = null)
    {
        var scope = nested is null ? root : First(root, nested) ?? root;
        return int.TryParse(Value(scope, name), out var value) ? value : 0;
    }

    private static long Long(XElement root, string name)
    {
        return long.TryParse(Value(root, name), out var value) ? value : 0;
    }

    private static string SeasonName(string? season)
    {
        return season?.ToLowerInvariant() switch
        {
            "spring" => "春季",
            "summer" => "夏季",
            "fall" => "秋季",
            "winter" => "冬季",
            _ => "未知季节"
        };
    }

    private static string WeatherText(XElement root)
    {
        if (bool.TryParse(Value(root, "isLightning"), out var lightning) && lightning)
        {
            return "雷雨";
        }

        if (bool.TryParse(Value(root, "isRaining"), out var raining) && raining)
        {
            return "雨天";
        }

        if (bool.TryParse(Value(root, "isSnowing"), out var snowing) && snowing)
        {
            return "降雪";
        }

        return "晴朗";
    }

    private static string FarmTypeName(int type)
    {
        return type switch
        {
            0 => "标准农场",
            1 => "河流农场",
            2 => "森林农场",
            3 => "山顶农场",
            4 => "荒野农场",
            5 => "四角农场",
            6 => "沙滩农场",
            7 => "草原农场",
            _ => "未知"
        };
    }

    private static int CountInventoryItem(XElement player, string id)
    {
        var item = player.Descendants()
            .FirstOrDefault(element => element.Value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item?.Parent is not { } parent)
        {
            return 0;
        }

        var stack = parent.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("Stack", StringComparison.OrdinalIgnoreCase));
        return int.TryParse(stack?.Value, out var value) ? value : 0;
    }

    private static double CalculateBundleProgress(XElement root)
    {
        var bundles = First(root, "bundles");
        if (bundles is null)
        {
            return 0;
        }

        var flags = bundles.Descendants()
            .Where(element => bool.TryParse(element.Value, out _))
            .Select(element => bool.Parse(element.Value))
            .ToList();
        return flags.Count == 0 ? 0 : flags.Count(value => value) / (double)flags.Count * 100;
    }

    private static double CalculateCollectionProgress(XElement player)
    {
        var shipped = First(player, "basicShipped");
        var count = shipped?.Elements().Count(element => element.Name.LocalName == "item") ?? 0;
        return Math.Clamp(count / 145d * 100, 0, 100);
    }
}
