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

    private static readonly PerfectionGoal[] ObeliskGoals =
    [
        new("Earth Obelisk", "地之图腾柱", "传送到山脉区域的后期建筑", "地之图腾柱"),
        new("Water Obelisk", "水之图腾柱", "传送到海滩区域的后期建筑", "水之图腾柱"),
        new("Desert Obelisk", "沙漠图腾柱", "传送到卡利科沙漠的后期建筑", "沙漠图腾柱"),
        new("Island Obelisk", "姜岛图腾柱", "传送到姜岛的后期建筑", "姜岛图腾柱")
    ];

    private static readonly PerfectionGoal[] StardropGoals =
    [
        new("Stardrop.Fair", "星露谷展览会星之果实", "在秋季星露谷展览会兑换获得", "星之果实"),
        new("Stardrop.Mines", "矿洞 100 层星之果实", "到达矿洞 100 层宝箱获得", "星之果实"),
        new("Stardrop.Spouse", "配偶或室友星之果实", "结婚或室友关系达到条件后获得", "星之果实"),
        new("Stardrop.Krobus", "下水道星之果实", "在科罗布斯商店购买获得", "星之果实"),
        new("Stardrop.SecretNote", "秘密纸条星之果实", "根据秘密纸条线索获得", "星之果实"),
        new("Stardrop.Museum", "博物馆星之果实", "博物馆捐赠达到目标后获得", "星之果实"),
        new("Stardrop.Fishing", "钓鱼大师星之果实", "钓到全部鱼类后从威利处获得", "星之果实")
    ];

    private static readonly MonsterEradicationGoal[] MonsterEradicationGoals =
    [
        new("Gil_Slimes", "史莱姆除害目标", "史莱姆"),
        new("Gil_VoidSpirits", "虚空怪除害目标", "暗影狂徒"),
        new("Gil_Bats", "蝙蝠除害目标", "蝙蝠"),
        new("Gil_Skeletons", "骷髅除害目标", "骷髅"),
        new("Gil_Insects", "昆虫除害目标", "臭虫"),
        new("Gil_Duggies", "掘地虫除害目标", "掘地虫"),
        new("Gil_DustSprites", "灰尘精灵除害目标", "灰尘精灵"),
        new("Gil_RockCrabs", "岩石蟹除害目标", "岩石蟹"),
        new("Gil_Mummies", "木乃伊除害目标", "木乃伊"),
        new("Gil_Serpents", "飞蛇除害目标", "飞蛇"),
        new("Gil_MagmaSprites", "岩浆精灵除害目标", "岩浆精灵"),
        new("Gil_PepperRex", "霸王喷火龙除害目标", "霸王喷火龙")
    ];

    private static readonly string[] MonsterEradicationFlags = MonsterEradicationGoals
        .Select(goal => goal.Flag)
        .ToArray();

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
        AddCollectionDetails(save, player);
        AddPerfectionStats(save);
        AddPerfectionDetails(save, root);
        AddMonsterKillStats(save, player);
        AddFishCatchStats(save, player);
        save.PerfectionProgress = CalculatePerfectionProgress(save);
        return save;
    }

    private static void AddCollectionStats(SaveInfo save)
    {
        save.CollectionStats.AddRange(
        [
            Progress("出货与采集", save.ItemsShipped, TotalShippableItems, "出货收藏记录农作物、采集物、动物产品和加工品，是完美度中产品与采集品项目的来源", "Shipped"),
            Progress("矿物", save.MineralsFound, TotalMinerals, "矿物收藏来自采矿、晶球和博物馆登记，用来判断矿物图鉴是否接近完整", "Minerals"),
            Progress("古物", save.ArtifactsFound, TotalArtifacts, "古物收藏来自蚯蚓点、钓鱼宝箱、怪物掉落和矿洞发现，主要用于博物馆补全", "Artifacts"),
            Progress("烹饪", save.CookedRecipes, TotalCookingRecipes, "烹饪进度统计已经做过的菜谱，完美度要求制作全部烹饪食谱", "Cooking"),
            Progress("鱼类", save.FishSpeciesCaught, TotalFishSpecies, "鱼类收藏记录捕获过的鱼和水产，完美度要求捕获全部鱼类", "Fish")
        ]);
    }

    private void AddCollectionDetails(SaveInfo save, XElement player)
    {
        AddCollectionDetails(save, "Shipped", player, "basicShipped");
        AddCollectionDetails(save, "Minerals", player, "mineralsFound");
        AddCollectionDetails(save, "Artifacts", player, "archaeologyFound");
        AddCollectionDetails(save, "Cooking", player, "cookingRecipes");
        AddCollectionDetails(save, "Crafting", player, "craftingRecipes");
        AddCollectionDetails(save, "Fish", player, "fishCaught");
    }

    private void AddCollectionDetails(SaveInfo save, string collectionKey, XElement player, string dictionaryName)
    {
        var collected = ReadCollectedKeys(player, dictionaryName);
        var items = catalog.GetCollectionItems(collectionKey, collected);
        if (items.Count > 0)
        {
            save.CollectionItems[collectionKey] = items.ToList();
        }
    }

    private static void AddPerfectionStats(SaveInfo save)
    {
        var friendshipTotal = save.Friendships.Count > 0 ? save.Friendships.Count : TotalGreatFriends;
        save.PerfectionStats.AddRange(
        [
            Progress("农场上的图腾柱", save.ObelisksBuilt, TotalObelisks, "图腾柱是后期传送建筑，地、水、沙漠、姜岛四座都属于完美度目标", detailKey: "Perfection.Obelisks"),
            Toggle("农场上有黄金时钟", save.HasGoldClock, "黄金时钟是后期昂贵建筑，可以阻止农场杂草和栅栏腐坏，也是完美度目标之一", "Perfection.GoldClock"),
            Progress("好朋友", save.GoodFriends, friendshipTotal, "好朋友统计达到当前关系上限的村民，完美度要求主要村民关系达到上限", detailKey: "Perfection.Friends"),
            Progress("找到所有星之果实", save.StardropsFound, 7, "星之果实会永久增加最大体力，全部找到是完美度目标之一", detailKey: "Perfection.Stardrops"),
            Progress("制作的制造设计图", save.CraftedRecipes, TotalCraftingRecipes, "制造设计图统计已经制作过的配方，完美度要求每种配方至少制作一次", detailKey: "Crafting"),
            Progress("找到的金色核桃", save.GoldenWalnutsFound, TotalGoldenWalnuts, "金色核桃是姜岛探索货币，可解锁道路、建筑、传送点和齐先生核桃房", detailKey: "Perfection.Walnuts"),
            Progress("已售出的产品和采集品", save.ItemsShipped, TotalShippableItems, "出货项目要求把主要产品和采集品至少出货一次，是完美度的重要组成", detailKey: "Shipped"),
            Toggle("杀怪英雄", save.IsMonsterHero, "杀怪英雄要求完成冒险者公会主要除害目标，会影响完美度中的战斗项目", "Perfection.MonsterHero"),
            Progress("农场主等级", save.FarmerLevelScore, TotalFarmerLevelScore, "农场主等级由五项技能综合折算，代表角色基础成长是否接近满档", detailKey: "Perfection.FarmerLevel"),
            Progress("制作的烹饪食谱", save.CookedRecipes, TotalCookingRecipes, "烹饪食谱要求每道菜至少制作一次，通常需要补齐配方和食材来源", detailKey: "Cooking"),
            Progress("捕获的鱼", save.FishSpeciesCaught, TotalFishSpecies, "捕获的鱼统计鱼类图鉴完成情况，完美度要求所有鱼类都至少钓到一次", detailKey: "Fish"),
            new SaveProgressInfo
            {
                Name = "完美豁免书",
                Value = save.PerfectionWaivers.ToString("N0"),
                Percent = 0,
                Detail = "完美豁免书可在齐先生处购买，用来直接补完美度分数，适合跳过不想完成的目标",
                DetailKey = "Perfection.Waivers"
            }
        ]);
    }

    private static void AddPerfectionDetails(SaveInfo save, XElement root)
    {
        var buildingTypes = ReadBuildingTypes(root);
        save.ProgressDetailItems["Perfection.Obelisks"] = ObeliskGoals
            .Select(goal => DetailItem(
                goal.Id,
                goal.Name,
                buildingTypes.Contains(goal.Id),
                goal.Detail,
                goal.GuideQuery,
                "\uE80F"))
            .ToList();

        save.ProgressDetailItems["Perfection.GoldClock"] =
        [
            DetailItem(
                "Gold Clock",
                "黄金时钟",
                save.HasGoldClock,
                "农场后期建筑，可阻止农场杂草和栅栏腐坏",
                "黄金时钟",
                "\uE80F")
        ];

        save.ProgressDetailItems["Perfection.Friends"] = save.Friendships.Count > 0
            ? save.Friendships
                .OrderBy(friendship => friendship.Hearts < friendship.MaximumHearts)
                .ThenByDescending(friendship => friendship.Points)
                .ThenBy(friendship => friendship.Name)
                .Select(friendship => DetailItem(
                    friendship.NpcId,
                    friendship.Name,
                    friendship.Hearts >= friendship.MaximumHearts,
                    string.Join(" · ", new[]
                    {
                        $"{friendship.Hearts}/{friendship.MaximumHearts} 心",
                        friendship.RelationshipText
                    }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    friendship.Name,
                    "\uE77B"))
                .ToList()
            : CountSummaryItems("好朋友", save.GoodFriends, TotalGreatFriends, "主要村民关系达到上限", "好感", "\uE77B");

        save.ProgressDetailItems["Perfection.Stardrops"] = StardropGoals
            .Select((goal, index) => DetailItem(
                goal.Id,
                goal.Name,
                index < save.StardropsFound,
                $"{goal.Detail}；存档只记录星之果实数量，此处按常见来源顺序标记",
                goal.GuideQuery,
                "\uE735"))
            .ToList();

        save.ProgressDetailItems["Perfection.Walnuts"] = CountSummaryItems(
            "金色核桃",
            save.GoldenWalnutsFound,
            TotalGoldenWalnuts,
            "姜岛探索货币，可解锁道路、建筑、传送点和齐先生核桃房",
            "金色核桃",
            "\uE8D1");

        save.ProgressDetailItems["Perfection.MonsterHero"] = MonsterEradicationGoals
            .Select(goal => DetailItem(
                goal.Flag,
                goal.Name,
                save.MailFlags.Contains(goal.Flag),
                "冒险者公会除害目标，完成后计入杀怪英雄",
                goal.GuideQuery,
                "\uE7FC"))
            .ToList();

        save.ProgressDetailItems["Perfection.FarmerLevel"] = save.Skills
            .Where(skill => !skill.Key.Equals("Mastery", StringComparison.OrdinalIgnoreCase))
            .OrderBy(skill => skill.IsMaxLevel)
            .ThenByDescending(skill => skill.Level)
            .ThenBy(skill => skill.Name)
            .Select(skill => DetailItem(
                skill.Key,
                skill.Name,
                skill.IsMaxLevel,
                $"{skill.LevelText} · {skill.ExperienceText}",
                skill.Name,
                "\uE735"))
            .ToList();

        save.ProgressDetailItems["Perfection.Waivers"] =
        [
            DetailItem(
                "PerfectionWaivers",
                "完美豁免书",
                save.PerfectionWaivers > 0,
                save.PerfectionWaivers > 0
                    ? $"已购买 {save.PerfectionWaivers:N0} 本，用于补足完美度分数"
                    : "未购买；可在齐先生处购买来补足完美度分数",
                "完美豁免书",
                "\uE8A5")
        ];
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
            var displayName = MonsterName(name);
            save.MonsterKillStats.Add(new SaveMetricInfo
            {
                Name = displayName,
                Value = $"x{count:N0}",
                Detail = $"{displayName} 的累计击杀次数。击杀记录来自冒险者公会统计，可用于判断除害目标和杀怪英雄进度",
                Glyph = "\uE7FC",
                IconKey = name
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
            var displayName = item?.Title ?? (objectId is { } value ? catalog.ObjectName(value) : KnownQualifiedObjectName(id));
            save.FishCatchStats.Add(new SaveMetricInfo
            {
                Name = displayName,
                Value = $"x{count:N0}",
                Detail = $"{displayName} 的累计捕获次数。捕获记录会影响鱼类收藏和完美度中的捕获全部鱼类目标",
                Glyph = "\uE7C5",
                ObjectId = objectId,
                IconTexture = item?.IconTexture,
                IconSpriteIndex = item?.IconSpriteIndex,
                IconWidth = item?.IconWidth ?? 16,
                IconHeight = item?.IconHeight ?? 16
            });
        }
    }

    private static SaveProgressInfo Progress(
        string name,
        int current,
        int total,
        string detail,
        string? collectionKey = null,
        string? detailKey = null)
    {
        var safeTotal = Math.Max(1, total);
        var percent = Math.Clamp(current / (double)safeTotal * 100, 0, 100);
        return new SaveProgressInfo
        {
            Name = name,
            Value = $"{current:N0}/{total:N0}",
            Percent = percent,
            Detail = $"{detail}。当前 {current:N0}/{total:N0}，完成度 {percent:0}%",
            CollectionKey = collectionKey,
            DetailKey = detailKey
        };
    }

    private static SaveProgressInfo Toggle(string name, bool value, string detail, string? detailKey = null)
        => new()
        {
            Name = name,
            Value = value ? "是" : "否",
            Percent = value ? 100 : 0,
            Detail = $"{detail}。当前状态：{(value ? "已完成" : "未完成")}",
            DetailKey = detailKey
        };

    private static List<SaveCollectionItemInfo> CountSummaryItems(
        string name,
        int current,
        int total,
        string detail,
        string guideQuery,
        string glyph)
    {
        var safeTotal = Math.Max(1, total);
        var completed = Math.Clamp(current, 0, safeTotal);
        var missing = Math.Max(0, safeTotal - completed);
        return
        [
            DetailItem(
                $"{name}.Done",
                $"已完成{name}",
                completed > 0 || missing == 0,
                $"{detail}；当前已完成 {completed:N0}/{safeTotal:N0}",
                guideQuery,
                glyph,
                completed > 0 || missing == 0 ? "已完成" : "未开始"),
            DetailItem(
                $"{name}.Missing",
                $"未完成{name}",
                missing == 0,
                missing == 0 ? "没有未完成项目" : $"{detail}；还差 {missing:N0} 项",
                guideQuery,
                glyph,
                missing == 0 ? "已完成" : "未完成")
        ];
    }

    private static SaveCollectionItemInfo DetailItem(
        string itemId,
        string name,
        bool isComplete,
        string detail,
        string guideQuery,
        string glyph,
        string? statusText = null)
        => new()
        {
            ItemId = itemId,
            Name = name,
            Detail = detail,
            IsCollected = isComplete,
            GuideQuery = guideQuery,
            Glyph = glyph,
            StatusTextOverride = statusText ?? (isComplete ? "已完成" : "未完成")
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
        if (normalized.Equals("Sludge", StringComparison.OrdinalIgnoreCase))
        {
            return "污泥怪";
        }

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

    private static IReadOnlySet<string> ReadCollectedKeys(XElement root, string name)
        => First(root, name) is { } dictionary
            ? ReadStringIntDictionary(dictionary)
                .Where(item => item.Value > 0)
                .Select(item => item.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

    private static HashSet<string> ReadBuildingTypes(XElement root)
        => root.Descendants()
            .Where(element => !element.HasElements
                && element.Name.LocalName.Equals("buildingType", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(element.Value))
            .Select(element => element.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

    private sealed record PerfectionGoal(string Id, string Name, string Detail, string GuideQuery);

    private sealed record MonsterEradicationGoal(string Flag, string Name, string GuideQuery);
}
