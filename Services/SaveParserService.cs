using System.Xml.Linq;
using LSMA.Models;

namespace LSMA.Services;

public sealed class SaveParserService(
    LoggingService logging,
    NpcLocalizationService npcNames,
    GameContentCatalogService catalog)
{
    private const int TotalShippableItems = 154;
    private const int TotalMinerals = 42;
    private const int TotalArtifacts = 53;
    private const int TotalCookingRecipes = 81;
    private const int TotalCraftingRecipes = 130;
    private const int TotalFishSpecies = 77;
    private const int TotalGoldenWalnuts = 130;
    private const int TotalObelisks = 4;
    private const int TotalFarmerLevelScore = 25;
    private const int TotalGreatFriends = 34;

    private static readonly IReadOnlyDictionary<int, int> BundleRequiredItemCounts = new Dictionary<int, int>
    {
        [0] = 4,
        [1] = 3,
        [2] = 4,
        [3] = 4,
        [4] = 4,
        [5] = 5,
        [6] = 4,
        [7] = 4,
        [8] = 4,
        [9] = 3,
        [10] = 5,
        [11] = 6,
        [12] = 4,
        [13] = 4,
        [14] = 4,
        [15] = 3,
        [16] = 5,
        [17] = 4,
        [18] = 3,
        [19] = 4,
        [20] = 2,
        [21] = 6,
        [22] = 6,
        [23] = 4,
        [24] = 3,
        [25] = 4,
        [26] = 1,
        [27] = 1,
        [28] = 1,
        [29] = 1
    };

    private static readonly string[] CommunityCenterRoomFlags =
    [
        "ccCraftsRoom",
        "ccPantry",
        "ccFishTank",
        "ccBoilerRoom",
        "ccBulletin",
        "ccVault"
    ];

    private static readonly string[] MonsterEradicationFlags =
    [
        "Gil_Slimes",
        "Gil_VoidSpirits",
        "Gil_Bats",
        "Gil_Skeletons",
        "Gil_Insects",
        "Gil_Duggies",
        "Gil_DustSprites",
        "Gil_RockCrabs",
        "Gil_Mummies",
        "Gil_Serpents",
        "Gil_MagmaSprites",
        "Gil_PepperRex"
    ];

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
        var mailFlags = GetMailFlags(root);
        var bundleStates = ReadBundleStates(root);
        var skillLevelTotal = Integer(player, "farmingLevel")
            + Integer(player, "miningLevel")
            + Integer(player, "foragingLevel")
            + Integer(player, "fishingLevel")
            + Integer(player, "combatLevel");
        var save = new SaveInfo
        {
            FolderPath = source.DirectoryPath,
            FolderName = source.FolderName,
            FarmerName = Value(player, "name") ?? "未知角色",
            FarmName = Value(player, "farmName") ?? "未知农场",
            FarmType = FarmTypeName(Integer(root, "whichFarm")),
            Gender = Value(player, "Gender") ?? Value(player, "gender") ?? "Male",
            FacingDirection = Integer(player, "FacingDirection"),
            Hair = Integer(player, "hair"),
            HairColorR = ColorComponent(player, "hairstyleColor", "R"),
            HairColorG = ColorComponent(player, "hairstyleColor", "G"),
            HairColorB = ColorComponent(player, "hairstyleColor", "B"),
            ShirtIndex = ClothingIndex(player, "shirtItem", "shirt"),
            GameVersion = Value(root, "gameVersion") ?? Value(root, "version") ?? "-",
            Year = year,
            Season = season,
            Day = day,
            TotalDays = StatisticsInteger(player, root, "daysPlayed", CalculateElapsedDays(year, season, day)),
            Money = Integer(player, "money"),
            TotalMoneyEarned = Long(player, "totalMoneyEarned"),
            PlayTimeMilliseconds = Integer(player, "millisecondsPlayed"),
            Weather = WeatherText(root),
            Spouse = Value(player, "spouse") is { Length: > 0 } spouse ? npcNames.Translate(spouse) : "无",
            Pet = DetectPet(player, root),
            MineLevel = Math.Min(deepest, 120),
            SkullCavernLevel = Math.Max(0, deepest - 120),
            QiGems = CountInventoryItem(player, "(O)858"),
            CommunityCenterProgress = CalculateBundleProgress(
                mailFlags,
                bundleStates,
                catalog.GetBundleRequiredItemCounts()),
            CollectionProgress = CalculateCollectionProgress(player),
            UniqueGameId = Long(root, "uniqueIDForThisGame"),
            StepsTaken = StatisticsLong(player, root, "stepsTaken"),
            FishCaught = StatisticsInteger(player, root, "fishCaught", 0),
            TimesFished = StatisticsInteger(player, root, "timesFished", 0),
            SeedsSown = StatisticsInteger(player, root, "seedsSown", 0),
            TrashCansChecked = StatisticsInteger(player, root, "trashCansChecked", 0),
            TrashRecycled = StatisticsInteger(player, root, "piecesOfTrashRecycled", 0),
            ItemsShipped = CountDictionaryItems(player, "basicShipped"),
            MineralsFound = CountDictionaryItems(player, "mineralsFound"),
            ArtifactsFound = CountDictionaryItems(player, "archaeologyFound"),
            CookedRecipes = CountDictionaryPositiveValues(player, "cookingRecipes"),
            CraftedRecipes = CountDictionaryPositiveValues(player, "craftingRecipes"),
            FishSpeciesCaught = CountDictionaryItems(player, "fishCaught"),
            TotalMonsterKills = StatisticsInteger(player, root, "monstersKilled", 0),
            ObelisksBuilt = CountBuildings(root, "Obelisk"),
            HasGoldClock = HasBuilding(root, "Gold Clock"),
            GoldenWalnutsFound = Integer(root, "goldenWalnutsFound"),
            PerfectionWaivers = Integer(root, "perfectionWaivers"),
            GoodFriends = StatisticsInteger(player, root, "goodFriends", 0),
            SkillLevelTotal = skillLevelTotal,
            FarmerLevelScore = CalculateFarmerLevelScore(skillLevelTotal),
            MasteryLevel = CountMasteries(player, root),
            MasteryExp = StatisticsInteger(player, root, "MasteryExp", 0),
            IsMonsterHero = MonsterEradicationFlags.All(mailFlags.Contains),
            HouseUpgradeLevel = Integer(player, "houseUpgradeLevel"),
            StardropsFound = EstimateStardrops(Integer(player, "maxStamina"))
        };

        foreach (var flag in mailFlags)
        {
            save.MailFlags.Add(flag);
        }

        foreach (var (id, states) in bundleStates)
        {
            save.CommunityBundleStates[id] = states;
        }

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
            new SaveSkillInfo { Key = "Combat", Name = "战斗", Level = Integer(player, "combatLevel"), Experience = ExperienceAt(experience, 4) },
            new SaveSkillInfo { Key = "Mastery", Name = "精通", Level = save.MasteryLevel, Experience = save.MasteryExp, MaxLevel = 5, UseExperienceProgress = false }
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
        if (save.GoodFriends == 0)
        {
            save.GoodFriends = save.Friendships.Count(friendship => friendship.Hearts >= friendship.MaximumHearts);
        }

        AddCollectionStats(save);
        AddPerfectionStats(save);
        AddMonsterKillStats(save, player);
        AddFishCatchStats(save, player);
        save.PerfectionProgress = CalculatePerfectionProgress(save);
        return save;
    }

    private static void AddCollectionStats(SaveInfo save)
    {
        save.CollectionStats.AddRange(
        [
            Progress("出货与采集", save.ItemsShipped, TotalShippableItems, "已登记到收藏的出货物品"),
            Progress("矿物", save.MineralsFound, TotalMinerals, "博物馆矿物收藏进度"),
            Progress("古物", save.ArtifactsFound, TotalArtifacts, "博物馆古物收藏进度"),
            Progress("烹饪", save.CookedRecipes, TotalCookingRecipes, "已制作过的食谱"),
            Progress("鱼类", save.FishSpeciesCaught, TotalFishSpecies, "已捕获过的鱼类")
        ]);
    }

    private static void AddPerfectionStats(SaveInfo save)
    {
        var friendshipTotal = save.Friendships.Count > 0 ? save.Friendships.Count : TotalGreatFriends;
        save.PerfectionStats.AddRange(
        [
            Progress("农场上的图腾柱", save.ObelisksBuilt, TotalObelisks, "地、水、沙漠、姜岛图腾柱"),
            Toggle("农场上有黄金时钟", save.HasGoldClock),
            Progress("好朋友", save.GoodFriends, friendshipTotal, "达到当前关系上限的村民"),
            Progress("找到所有星之果实", save.StardropsFound, 7, "永久体力星之果实"),
            Progress("制作的制造设计图", save.CraftedRecipes, TotalCraftingRecipes, "至少制作过一次的配方"),
            Progress("找到的金色核桃", save.GoldenWalnutsFound, TotalGoldenWalnuts, "姜岛金色核桃"),
            Progress("已售出的产品和采集品", save.ItemsShipped, TotalShippableItems, "出货收藏进度"),
            Toggle("杀怪英雄", save.IsMonsterHero),
            Progress("农场主等级", save.FarmerLevelScore, TotalFarmerLevelScore, "五项技能折算的完美度等级"),
            Progress("制作的烹饪食谱", save.CookedRecipes, TotalCookingRecipes, "至少烹饪过一次的食谱"),
            Progress("捕获的鱼", save.FishSpeciesCaught, TotalFishSpecies, "鱼类收藏进度"),
            new SaveProgressInfo
            {
                Name = "完美豁免书",
                Value = save.PerfectionWaivers.ToString("N0"),
                Percent = 0,
                Detail = "齐先生完美豁免书数量"
            }
        ]);
    }

    private static void AddMonsterKillStats(SaveInfo save, XElement player)
    {
        var stats = First(player, "stats");
        var monsters = First(stats ?? player, "specificMonstersKilled");
        if (monsters is null)
        {
            return;
        }

        foreach (var (name, count) in ReadStringIntDictionary(monsters)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key))
        {
            save.MonsterKillStats.Add(new SaveMetricInfo
            {
                Name = MonsterName(name),
                Value = $"x{count:N0}",
                Detail = name,
                Glyph = "\uE7FC"
            });
        }
    }

    private void AddFishCatchStats(SaveInfo save, XElement player)
    {
        var fishCaught = First(player, "fishCaught");
        if (fishCaught is null)
        {
            return;
        }

        foreach (var (id, count) in ReadStringIntDictionary(fishCaught)
            .Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key))
        {
            var item = catalog.FindItemById(id);
            var objectId = ObjectIdFromQualifiedId(id) ?? item?.ObjectId;
            save.FishCatchStats.Add(new SaveMetricInfo
            {
                Name = item?.Title ?? (objectId is { } value ? catalog.ObjectName(value) : KnownQualifiedObjectName(id)),
                Value = $"x{count:N0}",
                Detail = id,
                Glyph = "\uE7C5",
                ObjectId = objectId,
                IconTexture = item?.IconTexture,
                IconSpriteIndex = item?.IconSpriteIndex,
                IconWidth = item?.IconWidth ?? 16,
                IconHeight = item?.IconHeight ?? 16
            });
        }
    }

    private static SaveProgressInfo Progress(string name, int current, int total, string detail)
    {
        var safeTotal = Math.Max(1, total);
        return new SaveProgressInfo
        {
            Name = name,
            Value = $"{current:N0}/{total:N0}",
            Percent = Math.Clamp(current / (double)safeTotal * 100, 0, 100),
            Detail = $"{detail} · {Math.Clamp(current / (double)safeTotal * 100, 0, 100):0}%"
        };
    }

    private static SaveProgressInfo Toggle(string name, bool value)
        => new()
        {
            Name = name,
            Value = value ? "是" : "否",
            Percent = value ? 100 : 0,
            Detail = value ? "已完成" : "未完成"
        };

    private static double CalculatePerfectionProgress(SaveInfo save)
    {
        var score = 0d;
        score += Ratio(save.ItemsShipped, TotalShippableItems) * 15;
        score += Ratio(save.ObelisksBuilt, TotalObelisks) * 4;
        score += save.HasGoldClock ? 10 : 0;
        score += save.IsMonsterHero ? 10 : 0;
        score += Ratio(save.GoodFriends, TotalGreatFriends) * 11;
        score += Ratio(save.FarmerLevelScore, TotalFarmerLevelScore) * 5;
        score += Ratio(save.StardropsFound, 7) * 10;
        score += Ratio(save.CookedRecipes, TotalCookingRecipes) * 10;
        score += Ratio(save.CraftedRecipes, TotalCraftingRecipes) * 10;
        score += Ratio(save.FishSpeciesCaught, TotalFishSpecies) * 10;
        score += Ratio(save.GoldenWalnutsFound, TotalGoldenWalnuts) * 5;
        score += Math.Max(0, save.PerfectionWaivers);
        return Math.Clamp(score, 0, 100);
    }

    private static double Ratio(int current, int total)
        => Math.Clamp(current / (double)Math.Max(1, total), 0, 1);

    private static int CalculateFarmerLevelScore(int skillLevelTotal)
        => Math.Clamp(skillLevelTotal / 2, 0, TotalFarmerLevelScore);

    private static int CountMasteries(XElement player, XElement root)
    {
        var unlocked = Enumerable.Range(0, 5)
            .Count(index => StatisticsInteger(player, root, $"Mastery_{index}", 0) > 0);
        return Math.Clamp(
            Math.Max(unlocked, StatisticsInteger(player, root, "MasteryLevelsSpent", 0)),
            0,
            5);
    }

    private static int? ObjectIdFromQualifiedId(string id)
    {
        var normalized = id.Trim();
        if (normalized.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        return int.TryParse(normalized, out var value) ? value : null;
    }

    private static string KnownQualifiedObjectName(string id)
    {
        var normalized = id.Trim();
        if (normalized.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        return normalized switch
        {
            "RiverJelly" => "河凝胶",
            "SeaJelly" => "海凝胶",
            "CaveJelly" => "洞穴凝胶",
            _ => id
        };
    }

    private static string MonsterName(string id)
    {
        var normalized = id.Replace("_dangerous", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized switch
        {
            "Armored Bug" => "装甲虫",
            "Assassin Bug" => "刺客虫",
            "Bat" => "蝙蝠",
            "Big Slime" => "大型史莱姆",
            "Blue Squid" => "蓝鱿鱼",
            "Bug" => "臭虫",
            "Carbon Ghost" => "碳幽灵",
            "Duggy" => "掘地虫",
            "Dust Spirit" => "灰尘精灵",
            "Dwarvish Sentry" => "矮人哨兵",
            "Fly" => "苍蝇",
            "Frost Bat" => "冰霜蝙蝠",
            "Frost Jelly" => "冰霜史莱姆",
            "Ghost" => "幽灵",
            "Green Slime" => "大史莱姆",
            "Grub" => "蛆",
            "Haunted Skull" => "闹鬼骷髅",
            "Hot Head" => "热头怪",
            "Iridium Bat" => "铱蝙蝠",
            "Iridium Crab" => "铱蟹",
            "Iridium Golem" => "铱魔像",
            "Lava Bat" => "熔岩蝙蝠",
            "Lava Crab" => "熔岩蟹",
            "Lava Lurk" => "熔岩潜伏怪",
            "Magma Duggy" => "岩浆掘地虫",
            "Magma Sprite" => "岩浆精灵",
            "Magma Sparker" => "岩浆火花",
            "Metal Head" => "金属大头",
            "Mummy" => "木乃伊",
            "Pepper Rex" => "霸王喷火龙",
            "Putrid Ghost" => "腐臭幽灵",
            "Rock Crab" => "岩石蟹",
            "Royal Serpent" => "皇家飞蛇",
            "Serpent" => "飞蛇",
            "Shadow Brute" => "暗影狂徒",
            "Shadow Shaman" => "暗影萨满",
            "Skeleton" => "骷髅",
            "Skeleton Mage" => "骷髅法师",
            "Spider" => "蜘蛛",
            "Spiker" => "尖刺怪",
            "Squid Kid" => "鱿鱼娃",
            "Stick Bug" => "竹节虫",
            "Stone Golem" => "石魔像",
            "Tiger Slime" => "老虎史莱姆",
            "Truffle Crab" => "松露蟹",
            "Wilderness Golem" => "荒野石魔",
            _ => normalized
        };
    }

    private static string DetectPet(XElement player, XElement root)
    {
        var directTypes = new[] { player, root }
            .SelectMany(scope => new[] { Value(scope, "whichPetType"), Value(scope, "petType") })
            .Select(MapPetType)
            .Where(type => type is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directTypes.Count > 0)
        {
            return string.Join("+", directTypes);
        }

        var nestedTypes = root.Descendants()
            .Where(element => !element.HasElements
                && (element.Name.LocalName.Equals("whichPetType", StringComparison.OrdinalIgnoreCase)
                    || element.Name.LocalName.Equals("petType", StringComparison.OrdinalIgnoreCase)))
            .Select(element => MapPetType(element.Value))
            .Where(type => type is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (nestedTypes.Count > 0)
        {
            return string.Join("+", nestedTypes);
        }

        var catPerson = new[] { player, root }
            .Select(scope => Value(scope, "catPerson"))
            .Concat(root.Descendants()
                .Where(element => !element.HasElements && element.Name.LocalName.Equals("catPerson", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return bool.TryParse(catPerson, out var isCat) ? (isCat ? "猫" : "狗") : "未识别";
    }

    private static string? MapPetType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            var type when type.Contains("Cat", StringComparison.OrdinalIgnoreCase) => "猫",
            var type when type.Contains("Dog", StringComparison.OrdinalIgnoreCase) => "狗",
            var type when type.Contains("Turtle", StringComparison.OrdinalIgnoreCase) => "乌龟",
            var type => type
        };
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

    private static int ColorComponent(XElement root, string colorName, string component)
        => First(root, colorName) is { } color ? Integer(color, component) : 0;

    private static int ClothingIndex(XElement player, string itemName, string legacyName)
    {
        var item = First(player, itemName);
        if (item is not null)
        {
            var index = Integer(item, "indexInTileSheet");
            if (index >= 0)
            {
                return index;
            }
        }

        return Integer(player, legacyName);
    }

    private static int StatisticsInteger(XElement player, XElement root, string name, int fallback)
        => (int)Math.Clamp(StatisticsLong(player, root, name, fallback), int.MinValue, int.MaxValue);

    private static long StatisticsLong(XElement player, XElement root, string name, long fallback = 0)
    {
        foreach (var stats in CandidateStats(player, root))
        {
            if (TryReadStatistic(stats, name, out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static IEnumerable<XElement> CandidateStats(XElement player, XElement root)
    {
        var seen = new HashSet<XElement>();
        foreach (var stats in new[] { First(player, "stats"), First(root, "stats") })
        {
            if (stats is not null && seen.Add(stats))
            {
                yield return stats;
            }
        }

        foreach (var stats in player.Descendants()
            .Concat(root.Descendants())
            .Where(element => element.Name.LocalName.Equals("stats", StringComparison.OrdinalIgnoreCase)))
        {
            if (seen.Add(stats))
            {
                yield return stats;
            }
        }
    }

    private static bool TryReadStatistic(XElement stats, string name, out long value)
    {
        var values = First(stats, "Values");
        if (values is not null && TryReadDictionaryNumber(values, name, out value))
        {
            return true;
        }

        if (TryReadDictionaryNumber(stats, name, out value))
        {
            return true;
        }

        var scalar = First(stats, name);
        if (scalar is not null && TryReadElementNumber(scalar, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadDictionaryNumber(XElement dictionary, string name, out long value)
    {
        foreach (var item in dictionary.Elements().Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)))
        {
            var key = FirstLeafText(First(item, "key"));
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryReadElementNumber(First(item, "value"), out value);
        }

        value = 0;
        return false;
    }

    private static bool TryReadElementNumber(XElement? element, out long value)
    {
        var text = FirstLeafText(element);
        return long.TryParse(text, out value);
    }

    private static string? FirstLeafText(XElement? element)
    {
        return element?.DescendantsAndSelf()
            .Where(candidate => !candidate.HasElements)
            .Select(candidate => candidate.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private static double CalculateBundleProgress(
        IReadOnlySet<string> mailFlags,
        IReadOnlyDictionary<int, List<bool>> bundleStates,
        IReadOnlyDictionary<int, int> requiredCounts)
    {
        if (mailFlags.Contains("ccIsComplete"))
        {
            return 100;
        }

        var bundleProgress = CalculateSavedBundleProgress(bundleStates, requiredCounts);
        var roomProgress = CalculateRoomMailProgress(mailFlags);
        return Math.Max(bundleProgress, roomProgress);
    }

    private static HashSet<string> GetMailFlags(XElement root)
    {
        return root.Descendants()
            .Where(element => element.Name.LocalName.Equals("mailReceived", StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => element.Descendants()
                .Where(child => child.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase)))
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static double CalculateRoomMailProgress(IReadOnlySet<string> mailFlags)
    {
        var completedRooms = CommunityCenterRoomFlags.Count(mailFlags.Contains);
        return completedRooms == 0 ? 0 : completedRooms / (double)CommunityCenterRoomFlags.Length * 100;
    }

    private static Dictionary<int, List<bool>> ReadBundleStates(XElement root)
    {
        var result = new Dictionary<int, List<bool>>();
        var bundles = root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("bundles", StringComparison.OrdinalIgnoreCase));
        if (bundles is null)
        {
            return result;
        }

        foreach (var item in bundles.Elements()
            .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)))
        {
            var bundleId = ReadBundleId(item);
            if (bundleId is null)
            {
                continue;
            }

            var states = item.Descendants()
                .Where(element => element.Name.LocalName.Equals("boolean", StringComparison.OrdinalIgnoreCase))
                .Select(element => bool.TryParse(element.Value, out var isDone) && isDone)
                .ToList();
            if (states.Count > 0)
            {
                result[bundleId.Value] = states;
            }
        }

        return result;
    }

    private static double CalculateSavedBundleProgress(
        IReadOnlyDictionary<int, List<bool>> bundleStates,
        IReadOnlyDictionary<int, int> requiredCounts)
    {
        var totalBundles = 0;
        var completedBundles = 0;
        foreach (var (bundleId, states) in bundleStates)
        {
            if (states.Count == 0)
            {
                continue;
            }

            totalBundles++;
            var required = requiredCounts.TryGetValue(bundleId, out var currentCount)
                ? currentCount
                : BundleRequiredItemCounts.TryGetValue(bundleId, out var defaultCount)
                    ? defaultCount
                    : states.Count;
            if (states.Count(state => state) >= required)
            {
                completedBundles++;
            }
        }

        return totalBundles == 0 ? 0 : completedBundles / (double)totalBundles * 100;
    }

    private static int? ReadBundleId(XElement item)
    {
        var key = item.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("key", StringComparison.OrdinalIgnoreCase));
        var value = key?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("int", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }

    private static int CountDictionaryItems(XElement root, string name)
        => First(root, name)?.Elements().Count(element => element.Name.LocalName == "item") ?? 0;

    private static int CountDictionaryPositiveValues(XElement root, string name)
        => First(root, name) is { } dictionary
            ? ReadStringIntDictionary(dictionary).Count(item => item.Value > 0)
            : 0;

    private static Dictionary<string, int> ReadStringIntDictionary(XElement dictionary)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dictionary.Elements()
            .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)))
        {
            var key = FirstLeafText(First(item, "key"));
            var valueText = FirstLeafText(First(item, "value"));
            if (!string.IsNullOrWhiteSpace(key) && int.TryParse(valueText, out var value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static int CountBuildings(XElement root, string namePart)
        => root.Descendants()
            .Count(element => !element.HasElements
                && element.Name.LocalName.Equals("buildingType", StringComparison.OrdinalIgnoreCase)
                && element.Value.Contains(namePart, StringComparison.OrdinalIgnoreCase));

    private static bool HasBuilding(XElement root, string buildingType)
        => root.Descendants()
            .Any(element => !element.HasElements
                && element.Name.LocalName.Equals("buildingType", StringComparison.OrdinalIgnoreCase)
                && element.Value.Equals(buildingType, StringComparison.OrdinalIgnoreCase));

    private static int EstimateStardrops(int maxStamina)
        => maxStamina <= 270 ? 0 : Math.Clamp((int)Math.Round((maxStamina - 270) / 34d), 0, 7);

    private static double CalculateCollectionProgress(XElement player)
    {
        var count = CountDictionaryItems(player, "basicShipped");
        return Math.Clamp(count / 145d * 100, 0, 100);
    }
}
