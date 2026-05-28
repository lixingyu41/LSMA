using System.Xml.Linq;
using LSMA.Models;

namespace LSMA.Services;

public sealed class SaveParserService(LoggingService logging, NpcLocalizationService npcNames)
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

    private SaveInfo Parse(SaveSource source)
    {
        var document = XDocument.Load(source.FilePath, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("存档 XML 为空。");
        var player = First(root, "player") ?? root;
        var year = Integer(root, "year");
        var season = SeasonName(Value(root, "currentSeason"));
        var day = Integer(root, "dayOfMonth");
        var deepest = Integer(player, "deepestMineLevel");
        var save = new SaveInfo
        {
            FolderPath = source.DirectoryPath,
            FolderName = source.FolderName,
            FarmerName = Value(player, "name") ?? "未知角色",
            FarmName = Value(player, "farmName") ?? "未知农场",
            FarmType = FarmTypeName(Integer(root, "whichFarm")),
            GameVersion = Value(root, "gameVersion") ?? Value(root, "version") ?? "-",
            Year = year,
            Season = season,
            Day = day,
            TotalDays = StatisticsInteger(root, "daysPlayed", CalculateElapsedDays(year, season, day)),
            Money = Integer(player, "money"),
            TotalMoneyEarned = Long(player, "totalMoneyEarned"),
            PlayTimeMilliseconds = Integer(player, "millisecondsPlayed"),
            Weather = WeatherText(root),
            Spouse = Value(player, "spouse") is { Length: > 0 } spouse ? npcNames.Translate(spouse) : "无",
            Pet = DetectPet(player, root),
            MineLevel = Math.Min(deepest, 120),
            SkullCavernLevel = Math.Max(0, deepest - 120),
            QiGems = CountInventoryItem(player, "(O)858"),
            CommunityCenterProgress = CalculateBundleProgress(root),
            CollectionProgress = CalculateCollectionProgress(player)
        };

        var experience = First(player, "experiencePoints")?.Elements()
            .Where(element => element.Name.LocalName.Equals("int", StringComparison.OrdinalIgnoreCase))
            .Select(element => int.TryParse(element.Value, out var value) ? value : 0)
            .ToList() ?? [];
        save.Skills.AddRange(
        [
            new SaveSkillInfo { Key = "Farming", Name = "耕种", Level = Integer(player, "farmingLevel"), Experience = ExperienceAt(experience, 0) },
            new SaveSkillInfo { Key = "Mining", Name = "采矿", Level = Integer(player, "miningLevel"), Experience = ExperienceAt(experience, 3) },
            new SaveSkillInfo { Key = "Foraging", Name = "采集", Level = Integer(player, "foragingLevel"), Experience = ExperienceAt(experience, 2) },
            new SaveSkillInfo { Key = "Fishing", Name = "钓鱼", Level = Integer(player, "fishingLevel"), Experience = ExperienceAt(experience, 1) },
            new SaveSkillInfo { Key = "Combat", Name = "战斗", Level = Integer(player, "combatLevel"), Experience = ExperienceAt(experience, 4) }
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
                    save.Friendships.Add(new SaveFriendshipInfo
                    {
                        NpcId = name,
                        Name = npcNames.Translate(name),
                        Points = points,
                        Status = item.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("Status", StringComparison.OrdinalIgnoreCase))?.Value ?? "Friendly",
                        IsDatable = npcNames.IsRomanceable(name)
                    });
                }
            }
        }

        var ordered = save.Friendships.OrderByDescending(value => value.Points).ToList();
        save.Friendships.Clear();
        save.Friendships.AddRange(ordered);
        return save;
    }

    private static string DetectPet(XElement player, XElement root)
    {
        // Try catPerson from player and root (direct children)
        foreach (var scope in new[] { player, root })
        {
            var catPerson = scope.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("catPerson", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (bool.TryParse(catPerson, out var isCat))
            {
                return isCat ? "猫" : "狗";
            }
        }

        // Fallback: search descendants (1.6+ possibly nested)
        var catPersonDesc = player.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("catPerson", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?? root.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("catPerson", StringComparison.OrdinalIgnoreCase))
                ?.Value;
        if (bool.TryParse(catPersonDesc, out var isCatDesc))
        {
            return isCatDesc ? "猫" : "狗";
        }

        // Try pets list (1.6+ may have multiple pets)
        var petsElement = player.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("pets", StringComparison.OrdinalIgnoreCase))
            ?? root.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("pets", StringComparison.OrdinalIgnoreCase));
        if (petsElement is not null)
        {
            var types = petsElement.Elements()
                .Select(pet => pet.Element("petType")?.Value ?? pet.Element("type")?.Value ?? string.Empty)
                .Where(t => t.Length > 0)
                .Select(t => t.Contains("Cat", StringComparison.OrdinalIgnoreCase) ? "猫"
                    : t.Contains("Dog", StringComparison.OrdinalIgnoreCase) ? "狗" : null)
                .Where(t => t is not null)
                .Distinct()
                .ToList();
            if (types.Count > 0)
            {
                return string.Join("+", types);
            }
        }

        return "未识别";
    }

    private static XElement? First(XElement root, string name)
    {
        return root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
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

    private static int StatisticsInteger(XElement root, string name, int fallback)
    {
        var stats = First(root, "stats");
        var item = stats?.Descendants()
            .FirstOrDefault(element =>
            {
                var key = element.Elements().FirstOrDefault(child => child.Name.LocalName == "key");
                return element.Name.LocalName == "item"
                    && key?.Descendants().Any(value => value.Value.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
            });
        var value = item?.Elements().FirstOrDefault(element => element.Name.LocalName == "value")?
            .Descendants().FirstOrDefault()?.Value;
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int CalculateElapsedDays(int year, string season, int day)
    {
        var seasonOffset = season switch
        {
            "夏季" => 28,
            "秋季" => 56,
            "冬季" => 84,
            _ => 0
        };
        return Math.Max(0, (year - 1) * 112 + seasonOffset + day - 1);
    }

    private static int ExperienceAt(IReadOnlyList<int> values, int index)
        => index >= 0 && index < values.Count ? values[index] : 0;

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
        var mailReceived = First(root, "mailReceived");

        // Community center fully complete
        if (HasMailFlag(mailReceived, "ccIsComplete"))
        {
            return 100;
        }

        // Joja route completed
        if (HasMailFlag(mailReceived, "jojaMember"))
        {
            return 100;
        }

        // Bundles may be nested inside locations in 1.6+, use Descendants
        var bundles = root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("bundles", StringComparison.OrdinalIgnoreCase));
        if (bundles is null)
        {
            return 0;
        }

        var totalSlots = 0;
        var completedSlots = 0;
        foreach (var array in bundles.Descendants()
            .Where(element => element.Name.LocalName.Equals("ArrayOfBoolean", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var boolean in array.Elements()
                .Where(element => element.Name.LocalName.Equals("boolean", StringComparison.OrdinalIgnoreCase)))
            {
                totalSlots++;
                if (bool.TryParse(boolean.Value, out var isDone) && isDone)
                {
                    completedSlots++;
                }
            }
        }

        return totalSlots == 0 ? 0 : completedSlots / (double)totalSlots * 100;
    }

    private static bool HasMailFlag(XElement? mailReceived, string flag)
    {
        return mailReceived?.Descendants()
            .Any(element => element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase)
                && element.Value.Equals(flag, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static double CalculateCollectionProgress(XElement player)
    {
        var shipped = First(player, "basicShipped");
        var count = shipped?.Elements().Count(element => element.Name.LocalName == "item") ?? 0;
        return Math.Clamp(count / 145d * 100, 0, 100);
    }
}
