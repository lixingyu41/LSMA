using System.Reflection;
using System.Text.RegularExpressions;
using LSMA.Models;

namespace LSMA.Services;

public sealed partial class GameContentCatalogService(AppStateService state, LoggingService logging)
{
    private static readonly HashSet<int> LegendaryFishIds =
    [
        159, 160, 163, 682, 775, 898, 899, 900, 901, 902
    ];

    private static readonly IReadOnlyDictionary<string, string[]> CollectionKeyAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Cheese Cauli."] = ["Cheese Cauliflower"],
        ["Cheese Cauliflower"] = ["Cheese Cauli."],
        ["Cookies"] = ["Cookie"],
        ["Cookie"] = ["Cookies"],
        ["Cran. Sauce"] = ["Cranberry Sauce"],
        ["Cranberry Sauce"] = ["Cran. Sauce"],
        ["Dish o' The Sea"] = ["Dish O' The Sea"],
        ["Dish O' The Sea"] = ["Dish o' The Sea"],
        ["Eggplant Parm."] = ["Eggplant Parmesan"],
        ["Eggplant Parmesan"] = ["Eggplant Parm."],
        ["Vegetable Stew"] = ["Vegetable Medley"],
        ["Vegetable Medley"] = ["Vegetable Stew"]
    };

    private readonly List<GuideSearchResult> _searchIndex = [];
    private readonly List<FishRecord> _fish = [];
    private readonly List<CropRecord> _crops = [];
    private readonly List<FestivalDefinition> _festivals = [];
    private readonly List<CommunityBundleDefinition> _bundleDefinitions = [];
    private readonly Dictionary<string, int> _objectIdsByName = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<int, string> _objectNamesById = [];
    private readonly Dictionary<string, GuideSearchResult> _itemResultsByQualifiedId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CollectionCatalogItem>> _collectionItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GuideSearchResult, SearchTextCacheEntry> _searchTextCache = [];

    public async Task PrepareAsync()
    {
        _searchIndex.Clear();
        _fish.Clear();
        _crops.Clear();
        _festivals.Clear();
        _bundleDefinitions.Clear();
        _objectIdsByName.Clear();
        _objectNamesById.Clear();
        _itemResultsByQualifiedId.Clear();
        _collectionItems.Clear();
        _searchTextCache.Clear();
        AddWikiGuideResults();
        if (state.GameDirectory is not { } game)
        {
            return;
        }

        try
        {
            await Task.Run(() => Load(game.Path));
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取游戏攻略数据失败", exception);
        }
    }

    public IReadOnlyList<GuideSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        query = query.Trim();
        var looseQuery = LooseSearchText(query);
        return _searchIndex
            .Where(item => SearchMatches(item, query, looseQuery))
            .OrderBy(item => MatchRank(item.Title, query, looseQuery))
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Title)
            .Take(60)
            .Select(item => CloneSearchResult(item, FindMatchReason(item, query)))
            .ToList();
    }

    public int? FindObjectIdByName(string name)
    {
        var normalized = NormalizeSearchName(name);
        return _objectIdsByName.TryGetValue(normalized, out var objectId) ? objectId : null;
    }

    public string ObjectName(int objectId)
        => _objectNamesById.TryGetValue(objectId, out var name) ? name : $"物品 {objectId}";

    public GuideSearchResult? FindItemById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var normalized = itemId.Trim();
        if (_itemResultsByQualifiedId.TryGetValue(normalized, out var result))
        {
            return CloneSearchResult(result, null);
        }

        var match = QualifiedItemType().Match(normalized);
        if (match.Success && _itemResultsByQualifiedId.TryGetValue(match.Groups[2].Value, out result))
        {
            return CloneSearchResult(result, null);
        }

        if (!normalized.StartsWith("(O)", StringComparison.OrdinalIgnoreCase)
            && _itemResultsByQualifiedId.TryGetValue($"(O){normalized}", out result))
        {
            return CloneSearchResult(result, null);
        }

        return null;
    }

    public IReadOnlyList<SaveCollectionItemInfo> GetCollectionItems(
        string collectionKey,
        IReadOnlySet<string> collectedIds)
    {
        if (!_collectionItems.TryGetValue(collectionKey, out var items) || items.Count == 0)
        {
            return [];
        }

        var collected = collectedIds
            .SelectMany(CollectionKeysFor)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return items
            .GroupBy(item => item.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => !IsCollectionItemCollected(item, collected))
            .ThenBy(item => item.SortKey)
            .ThenBy(item => item.Name)
            .Select(item => new SaveCollectionItemInfo
            {
                ItemId = item.ItemId,
                Name = item.Name,
                Detail = item.Detail,
                IsCollected = IsCollectionItemCollected(item, collected),
                ObjectId = item.ObjectId,
                IconTexture = item.IconTexture,
                IconSpriteIndex = item.IconSpriteIndex,
                IconWidth = item.IconWidth,
                IconHeight = item.IconHeight,
                GuideQuery = item.Name
            })
            .ToList();
    }

    private static string SearchText(GuideSearchResult item)
    {
        var values = new List<string> { item.Category, item.Title, item.Detail };
        foreach (var section in item.Sections)
        {
            values.Add(section.Title);
            values.AddRange(section.Lines);
            values.AddRange(section.Actions.Select(action => $"{action.Label} {action.Detail} {action.Query}"));
        }

        return string.Join(" ", values);
    }

    private bool SearchMatches(GuideSearchResult item, string query, string looseQuery)
    {
        var cache = SearchTextFor(item);
        return cache.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (!string.IsNullOrWhiteSpace(looseQuery)
                && cache.LooseText.Contains(looseQuery, StringComparison.CurrentCultureIgnoreCase));
    }

    private SearchTextCacheEntry SearchTextFor(GuideSearchResult item)
    {
        if (_searchTextCache.TryGetValue(item, out var cache))
        {
            return cache;
        }

        var text = SearchText(item);
        cache = new SearchTextCacheEntry(text, LooseSearchText(text));
        _searchTextCache[item] = cache;
        return cache;
    }

    private static int MatchRank(string title, string query, string looseQuery)
    {
        if (title.Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 0;
        }

        if (title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(looseQuery))
        {
            var looseTitle = LooseSearchText(title);
            if (looseTitle.Equals(looseQuery, StringComparison.CurrentCultureIgnoreCase))
            {
                return 0;
            }

            if (looseTitle.StartsWith(looseQuery, StringComparison.CurrentCultureIgnoreCase))
            {
                return 1;
            }
        }

        return 2;
    }

    private static string LooseSearchText(string value)
        => NormalizeSearchName(value).Replace("之", string.Empty);

    private static string? FindMatchReason(GuideSearchResult item, string query)
    {
        var looseQuery = LooseSearchText(query);
        if (item.Title.Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return null;
        }

        if (item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"名称包含：{item.Title}";
        }

        if (!string.IsNullOrWhiteSpace(looseQuery)
            && LooseSearchText(item.Title).Contains(looseQuery, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"名称关联：{item.Title}";
        }

        if (item.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return $"详情包含：{item.Detail}";
        }

        foreach (var section in item.Sections)
        {
            var line = section.Lines.FirstOrDefault(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            if (line is not null)
            {
                return FormatMatchReason(section.Title, line, query);
            }

            var action = section.Actions.FirstOrDefault(value =>
                value.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || value.Query.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || value.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            if (action is not null)
            {
                return $"{section.Title}：{action.DisplayText}";
            }
        }

        return $"分类包含：{item.Category}";
    }

    private static string FormatMatchReason(string sectionTitle, string line, string query)
    {
        if (sectionTitle == "特殊剧情与对话")
        {
            return $"特殊剧情关联：{ShortMatchLine(line, query)}";
        }

        if (sectionTitle == "亲属与关系")
        {
            return $"亲属关系关联：{ShortMatchLine(line, query)}";
        }

        return $"{sectionTitle}：{ShortMatchLine(line, query)}";
    }

    private static string ShortMatchLine(string line, string query)
    {
        var clean = line.Replace("/", " ").Trim();
        if (clean.Length <= 52)
        {
            return clean;
        }

        var index = clean.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
        {
            return $"{clean[..52]}...";
        }

        var start = Math.Max(0, index - 18);
        var length = Math.Min(clean.Length - start, 52);
        return $"{(start > 0 ? "..." : string.Empty)}{clean.Substring(start, length)}...";
    }

    private static GuideSearchResult CloneSearchResult(GuideSearchResult source, string? matchReason)
    {
        var result = new GuideSearchResult
        {
            Category = source.Category,
            Title = source.Title,
            Detail = source.Detail,
            ObjectId = source.ObjectId,
            NpcId = source.NpcId,
            IconTexture = source.IconTexture,
            IconSpriteIndex = source.IconSpriteIndex,
            IconWidth = source.IconWidth,
            IconHeight = source.IconHeight,
            IconUri = source.IconUri
        };

        if (!string.IsNullOrWhiteSpace(matchReason))
        {
            var reasonSection = new GuideSearchSection { Title = "匹配原因" };
            reasonSection.Lines.Add(matchReason);
            result.Sections.Add(reasonSection);
        }

        foreach (var section in source.Sections)
        {
            result.Sections.Add(CloneSection(section));
        }

        return result;
    }

    private static GuideSearchSection CloneSection(GuideSearchSection source)
    {
        var section = new GuideSearchSection { Title = source.Title };
        section.Lines.AddRange(source.Lines);
        section.Actions.AddRange(source.Actions.Select(action => new GuideSearchAction
        {
            Label = action.Label,
            Query = action.Query,
            Detail = action.Detail,
            ObjectId = action.ObjectId,
            IconTexture = action.IconTexture,
            IconSpriteIndex = action.IconSpriteIndex,
            IconWidth = action.IconWidth,
            IconHeight = action.IconHeight,
            IconUri = action.IconUri
        }));
        return section;
    }

    private static string NormalizeSearchName(string name)
    {
        var normalized = name.Replace('（', '(');
        var index = normalized.IndexOf('(', StringComparison.Ordinal);
        return (index >= 0 ? normalized[..index] : normalized)
            .Replace("黏", "粘")
            .Trim();
    }

    public IReadOnlyList<FestivalRecord> GetUpcomingFestivals(SaveInfo? save)
    {
        var currentDay = save is null ? 0 : ToYearDay(save.Season, save.Day);
        var upcoming = _festivals
            .Select(festival => (Festival: festival, Remaining: (festival.YearDay - currentDay + 112) % 112))
            .OrderBy(value => value.Remaining)
            .Take(3)
            .Select(value => new FestivalRecord
            {
                Name = value.Festival.Name,
                Season = value.Festival.Season,
                Day = value.Festival.Day,
                Detail = value.Festival.CardDetail,
                CountdownText = save is null
                    ? value.Festival.DateText
                    : value.Remaining == 0 ? "今天" : $"{value.Remaining} 天后"
            })
            .ToList();

        if (save is null || _festivals.Count == 0)
        {
            return upcoming;
        }

        var previous = _festivals
            .Select(festival => (Festival: festival, Elapsed: (currentDay - festival.YearDay + 112) % 112))
            .Where(value => value.Elapsed > 0)
            .OrderBy(value => value.Elapsed)
            .FirstOrDefault();
        if (previous.Festival is not null)
        {
            upcoming.Insert(0, new FestivalRecord
            {
                Name = previous.Festival.Name,
                Season = previous.Festival.Season,
                Day = previous.Festival.Day,
                IsPast = true,
                Detail = previous.Festival.CardDetail,
                CountdownText = $"{previous.Elapsed} 天前"
            });
        }

        return upcoming;
    }

    public IReadOnlyList<FishRecord> GetFishToday(SaveInfo? save)
    {
        if (save is null)
        {
            return _fish.Take(8).ToList();
        }

        return _fish
            .Where(fish => fish.Season.Contains(save.Season, StringComparison.Ordinal)
                && IsWeatherMatch(fish, save.Weather))
            .OrderByDescending(fish => fish.SalePrice)
            .ThenByDescending(fish => fish.IsLegendary)
            .ThenBy(fish => fish.SortStartMinutes)
            .ThenBy(fish => fish.Location)
            .ThenBy(fish => fish.Name)
            .ToList();
    }

    public IReadOnlyList<CropRecord> GetCropsForCurrentSeason(SaveInfo? save)
    {
        IEnumerable<CropRecord> source = _crops;
        if (save is null)
        {
            return source
                .OrderByDescending(crop => crop.HarvestValue)
                .ThenBy(crop => crop.GrowDays)
                .Take(12)
                .ToList();
        }

        var remainingDays = Math.Max(0, 28 - save.Day);
        return source
            .Where(crop => crop.Season.Contains(save.Season, StringComparison.Ordinal)
                && crop.GrowDays <= remainingDays)
            .OrderByDescending(crop => crop.HarvestValue)
            .ThenBy(crop => crop.GrowDays)
            .ThenBy(crop => crop.Name)
            .ToList();
    }

    public IReadOnlyList<BundleRecord> GetCommunityBundles(SaveInfo? save)
    {
        return GetRelevantBundleDefinitions(save)
            .Select(bundle => CreateBundleRecord(bundle, save))
            .Where(record => save is null || !record.IsComplete)
            .OrderBy(record => record.IsComplete)
            .ThenByDescending(record => record.MissingCount)
            .ThenBy(record => record.Name)
            .ToList();
    }

    public IReadOnlySet<int> GetMissingCommunityItemIds(SaveInfo? save)
    {
        if (save is null)
        {
            return new HashSet<int>();
        }

        return GetRelevantBundleDefinitions(save)
            .SelectMany(bundle => GetMissingItems(bundle, save).Select(item => item.ObjectId))
            .ToHashSet();
    }

    public IReadOnlyDictionary<int, int> GetBundleRequiredItemCounts()
    {
        return _bundleDefinitions.Count == 0
            ? new Dictionary<int, int>()
            : _bundleDefinitions.ToDictionary(bundle => bundle.Id, bundle => bundle.RequiredCount);
    }

    private IEnumerable<CommunityBundleDefinition> GetRelevantBundleDefinitions(SaveInfo? save)
    {
        return save is null || save.CommunityBundleStates.Count == 0
            ? _bundleDefinitions
            : _bundleDefinitions.Where(bundle => save.CommunityBundleStates.ContainsKey(bundle.Id));
    }

    private void AddCollectionCatalogItem(string collectionKey, CollectionCatalogItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        if (!_collectionItems.TryGetValue(collectionKey, out var items))
        {
            items = [];
            _collectionItems[collectionKey] = items;
        }

        if (!items.Any(existing => existing.ItemId.Equals(item.ItemId, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(item);
        }
    }

    private static bool IsCollectionItemCollected(
        CollectionCatalogItem item,
        IReadOnlySet<string> collectedIds)
    {
        return CollectionKeysFor(item.ItemId).Any(collectedIds.Contains)
            || item.ObjectId is { } objectId && CollectionKeysFor(objectId.ToString()).Any(collectedIds.Contains);
    }

    private static IEnumerable<string> CollectionKeysFor(string itemId)
    {
        var normalized = itemId.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;
        if (CollectionKeyAliases.TryGetValue(normalized, out var aliases))
        {
            foreach (var alias in aliases)
            {
                yield return alias;
            }
        }

        if (normalized.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
        {
            yield return normalized[3..];
        }
        else
        {
            yield return $"(O){normalized}";
        }
    }

    private static bool IsMineralCollectionItem(string type, int category)
        => category is -12 or -2 || type.Contains("Mineral", StringComparison.OrdinalIgnoreCase);

    private static bool IsArtifactCollectionItem(string type, int category)
        => type.Contains("Arch", StringComparison.OrdinalIgnoreCase);

    private static bool IsFishCollectionItem(string type, int category)
        => category == -4 || type.Contains("Fish", StringComparison.OrdinalIgnoreCase);

    private void Load(string gamePath)
    {
        var monoGame = Assembly.LoadFrom(Path.Combine(gamePath, "MonoGame.Framework.dll"));
        var gameData = Assembly.LoadFrom(Path.Combine(gamePath, "StardewValley.GameData.dll"));
        var contentType = monoGame.GetType("Microsoft.Xna.Framework.Content.ContentManager")
            ?? throw new InvalidOperationException("游戏运行库中缺少内容读取组件。");
        using var content = Activator.CreateInstance(
            contentType,
            new EmptyServiceProvider(),
            Path.Combine(gamePath, "Content")) as IDisposable
            ?? throw new InvalidOperationException("无法打开游戏内容目录。");

        var objectsStrings = LoadStringDictionary(contentType, content, "Strings\\Objects.zh-CN");
        var npcStrings = LoadStringDictionary(contentType, content, "Strings\\NPCNames.zh-CN");
        var characterStrings = LoadStringDictionary(contentType, content, "Strings\\Characters.zh-CN");
        var stringsFromCsFiles = LoadStringDictionary(contentType, content, "Strings\\StringsFromCSFiles.zh-CN");
        var bigCraftableStrings = TryLoadStringDictionary(contentType, content, "Strings\\BigCraftables.zh-CN")
            ?? new Dictionary<string, string>();
        var furnitureStrings = TryLoadStringDictionary(contentType, content, "Strings\\Furniture.zh-CN")
            ?? new Dictionary<string, string>();
        var toolStrings = TryLoadStringDictionary(contentType, content, "Strings\\Tools.zh-CN")
            ?? new Dictionary<string, string>();
        var weaponStrings = TryLoadStringDictionary(contentType, content, "Strings\\Weapons.zh-CN")
            ?? new Dictionary<string, string>();
        var shirtStrings = TryLoadStringDictionary(contentType, content, "Strings\\Shirts.zh-CN")
            ?? new Dictionary<string, string>();
        var pantsStrings = TryLoadStringDictionary(contentType, content, "Strings\\Pants.zh-CN")
            ?? new Dictionary<string, string>();
        var strings16 = TryLoadStringDictionary(contentType, content, "Strings\\1_6_Strings.zh-CN")
            ?? new Dictionary<string, string>();
        var locationStrings = TryLoadStringDictionary(contentType, content, "Strings\\Locations.zh-CN")
            ?? new Dictionary<string, string>();
        var localizedText = MergeLocalizedText(
            ("Strings\\Objects", objectsStrings),
            ("Strings\\NPCNames", npcStrings),
            ("Strings\\Characters", characterStrings),
            ("Strings\\StringsFromCSFiles", stringsFromCsFiles),
            ("Strings\\BigCraftables", bigCraftableStrings),
            ("Strings\\Furniture", furnitureStrings),
            ("Strings\\Tools", toolStrings),
            ("Strings\\Weapons", weaponStrings),
            ("Strings\\Shirts", shirtStrings),
            ("Strings\\Pants", pantsStrings),
            ("Strings\\1_6_Strings", strings16),
            ("Strings\\Locations", locationStrings));
        var festivalStrings = LoadStringDictionary(contentType, content, "Data\\Festivals\\FestivalDates.zh-CN");
        var fishData = LoadStringDictionary(contentType, content, "Data\\Fish");
        var bundleData = TryLoadStringDictionary(contentType, content, "Data\\Bundles.zh-CN")
            ?? LoadStringDictionary(contentType, content, "Data\\Bundles");
        var cookingRecipes = LoadStringDictionary(contentType, content, "Data\\CookingRecipes");
        var craftingRecipes = LoadStringDictionary(contentType, content, "Data\\CraftingRecipes");
        var locationNames = LoadLocationNames(contentType, content, gameData, localizedText);
        var characterEvents = LoadCharacterEventSummaries(contentType, content, gamePath, npcStrings, localizedText, locationNames);

        var objectType = gameData.GetType("StardewValley.GameData.Objects.ObjectData")
            ?? throw new InvalidOperationException("游戏对象数据类型不可用。");
        var objects = LoadTypedDictionary(contentType, content, "Data\\Objects", objectType);
        var localizedObjects = new Dictionary<int, string>();
        var objectNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var objectPrices = new Dictionary<int, int>();
        var objectResultsById = new Dictionary<int, GuideSearchResult>();
        var catalogItemsByQualifiedId = new Dictionary<string, CatalogItemReference>(StringComparer.OrdinalIgnoreCase);
        var itemResultsByQualifiedId = new Dictionary<string, GuideSearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, value) in objects)
        {
            var name = ResolveText(GetString(value, "DisplayName"), localizedText);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var detail = ResolveText(GetString(value, "Description"), localizedText);
            var price = GetInt(value, "Price");
            var texture = GetString(value, "Texture");
            var spriteIndex = GetInt(value, "SpriteIndex");
            var hasAlternateTexture = !string.IsNullOrWhiteSpace(texture);
            if (int.TryParse(id, out var objectId))
            {
                localizedObjects[objectId] = name;
                _objectNamesById[objectId] = name;
                objectPrices[objectId] = price;
                _objectIdsByName[NormalizeSearchName(name)] = objectId;
            }
            objectNamesById[id] = name;

            var objectResult = new GuideSearchResult
            {
                Category = "物品",
                Title = name,
                Detail = price > 0 ? $"{detail} · 售价 {price}g" : detail,
                ObjectId = !hasAlternateTexture && int.TryParse(id, out objectId) ? objectId : null,
                IconTexture = hasAlternateTexture ? texture : null,
                IconSpriteIndex = hasAlternateTexture ? spriteIndex : null
            };
            if (int.TryParse(id, out objectId))
            {
                var infoSection = new GuideSearchSection { Title = "物品信息" };
                if (price > 0)
                {
                    infoSection.Lines.Add($"售价：{price}g");
                }

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    infoSection.Lines.Add(detail);
                }

                if (infoSection.Lines.Count > 0)
                {
                    objectResult.Sections.Add(infoSection);
                }

                objectResultsById[objectId] = objectResult;
            }

            var reference = new CatalogItemReference(
                $"(O){id}",
                name,
                "物品",
                objectResult.Detail,
                objectResult.ObjectId,
                objectResult.IconTexture,
                objectResult.IconSpriteIndex,
                objectResult.IconWidth,
                objectResult.IconHeight);
            catalogItemsByQualifiedId[reference.ItemId] = reference;
            catalogItemsByQualifiedId[id] = reference;
            itemResultsByQualifiedId[reference.ItemId] = objectResult;
            itemResultsByQualifiedId[id] = objectResult;
            var itemType = GetString(value, "Type");
            var itemCategory = GetInt(value, "Category");
            var collectionItem = new CollectionCatalogItem(
                reference.ItemId,
                name,
                objectResult.Detail,
                reference.ObjectId,
                reference.IconTexture,
                reference.IconSpriteIndex,
                reference.IconWidth,
                reference.IconHeight,
                reference.ObjectId ?? int.MaxValue);
            if (!GetBool(value, "ExcludeFromShippingCollection"))
            {
                AddCollectionCatalogItem("Shipped", collectionItem);
            }

            if (IsMineralCollectionItem(itemType, itemCategory))
            {
                AddCollectionCatalogItem("Minerals", collectionItem);
            }

            if (IsArtifactCollectionItem(itemType, itemCategory))
            {
                AddCollectionCatalogItem("Artifacts", collectionItem);
            }

            if (IsFishCollectionItem(itemType, itemCategory))
            {
                AddCollectionCatalogItem("Fish", collectionItem);
            }

            _searchIndex.Add(objectResult);
        }
        AddObjectAliases(objectResultsById);
        foreach (var (reference, result) in LoadAdditionalItemResults(contentType, content, gameData, localizedText))
        {
            catalogItemsByQualifiedId[reference.ItemId] = reference;
            itemResultsByQualifiedId[reference.ItemId] = result;
            _searchIndex.Add(result);
        }

        foreach (var (reference, result) in LoadDelimitedItemResults(contentType, content, localizedText))
        {
            catalogItemsByQualifiedId[reference.ItemId] = reference;
            itemResultsByQualifiedId[reference.ItemId] = result;
            _searchIndex.Add(result);
        }

        foreach (var (id, result) in itemResultsByQualifiedId)
        {
            _itemResultsByQualifiedId[id] = result;
        }

        var festivalShopItems = LoadFestivalShopItems(contentType, content, gameData, localizedText, catalogItemsByQualifiedId);

        var characterType = gameData.GetType("StardewValley.GameData.Characters.CharacterData")
            ?? throw new InvalidOperationException("游戏角色数据类型不可用。");
        foreach (var (id, value) in LoadTypedDictionary(contentType, content, "Data\\Characters", characterType))
        {
            var name = ResolveText(GetString(value, "DisplayName"), localizedText);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            var season = TranslateSeason(GetString(value, "BirthSeason"));
            var birthday = GetInt(value, "BirthDay");
            var datable = GetBool(value, "CanBeRomanced");
            var result = new GuideSearchResult
            {
                Category = "人物",
                Title = name,
                NpcId = id,
                Detail = birthday > 0
                    ? $"{season} {birthday} 日生日{(datable ? " · 可恋爱" : string.Empty)}"
                    : datable ? "可恋爱角色" : "角色"
            };

            var basicSection = new GuideSearchSection { Title = "人物信息" };
            if (birthday > 0)
            {
                basicSection.Lines.Add($"生日：{season} {birthday} 日");
            }

            AddSectionLine(basicSection, "住址", FormatHomes(value, locationNames));
            AddSectionLine(basicSection, "区域", TranslateHomeRegion(GetString(value, "HomeRegion")));
            AddSectionLine(basicSection, "年龄", TranslateAge(GetString(value, "Age")));
            AddSectionLine(basicSection, "性别", TranslateGender(GetString(value, "Gender")));
            basicSection.Lines.Add(datable ? "关系：可恋爱" : "关系：不可恋爱");
            AddSectionLine(basicSection, "描述", ResolveText(GetString(value, "Description"), localizedText));
            if (basicSection.Lines.Count > 0)
            {
                result.Sections.Add(basicSection);
            }

            var familySection = new GuideSearchSection { Title = "亲属与关系" };
            foreach (var line in FormatRelationships(GetStringDictionary(value, "FriendsAndFamily"), npcStrings, localizedText))
            {
                familySection.Lines.Add(line);
            }

            AddSectionLine(familySection, "心仪对象", ResolveNpcName(GetString(value, "LoveInterest"), npcStrings));
            if (familySection.Lines.Count > 0)
            {
                result.Sections.Add(familySection);
            }

            var scheduleSection = CreateScheduleSection(TryLoadStringDictionary(contentType, content, $"Characters\\schedules\\{id}"), locationNames);
            if (scheduleSection is not null)
            {
                result.Sections.Add(scheduleSection);
            }

            var eventSection = new GuideSearchSection { Title = "特殊剧情与对话" };
            if (characterEvents.TryGetValue(id, out var eventLines))
            {
                eventSection.Lines.AddRange(eventLines.Take(6));
            }
            else
            {
                eventSection.Lines.Add("心事件：触发由好感心数、地点、时间、季节、天气共同决定。");
            }

            eventSection.Lines.Add("每日首次交谈：+20 好感；生日送礼：礼物基础好感 x8。");
            eventSection.Lines.Add("礼物基础值：喜爱 +80，喜欢 +45，中立 +20，不喜欢 -20，讨厌 -40。");
            result.Sections.Add(eventSection);

            _searchIndex.Add(result);
        }

        foreach (var (key, name) in festivalStrings)
        {
            var match = FestivalKey().Match(key);
            if (!match.Success)
            {
                continue;
            }

            var season = TranslateSeason(match.Groups[1].Value);
            var day = int.Parse(match.Groups[2].Value);
            var assetKey = $"{match.Groups[1].Value.ToLowerInvariant()}{day}";
            var festivalData = TryLoadStringDictionary(contentType, content, $"Data\\Festivals\\{assetKey}.zh-CN")
                ?? TryLoadStringDictionary(contentType, content, $"Data\\Festivals\\{assetKey}")
                ?? new Dictionary<string, string>();
            var schedule = ParseFestivalSchedule(festivalData, locationNames);
            var highlights = BuildFestivalHighlights(assetKey, festivalData);
            var shopItems = festivalShopItems.GetValueOrDefault(assetKey) ?? [];
            var definition = new FestivalDefinition(name, season, day, schedule.Location, schedule.TimeText, highlights, shopItems);
            _festivals.Add(definition);
            var festivalResult = new GuideSearchResult
            {
                Category = "节日",
                Title = name,
                Detail = definition.SearchDetail,
                ObjectId = SeasonIconObjectId(season)
            };
            var section = new GuideSearchSection { Title = "节日信息" };
            section.Lines.Add($"日期：{definition.DateText}");
            if (!string.IsNullOrWhiteSpace(definition.Location))
            {
                section.Lines.Add($"地点：{definition.Location}");
            }

            if (!string.IsNullOrWhiteSpace(definition.TimeText))
            {
                section.Lines.Add($"时间：{definition.TimeText}");
            }

            foreach (var highlight in definition.Highlights)
            {
                section.Lines.Add(highlight);
            }
            festivalResult.Sections.Add(section);
            if (definition.ShopItems.Count > 0)
            {
                var shopSection = new GuideSearchSection { Title = "节日商店" };
                shopSection.Actions.AddRange(definition.ShopItems.Take(24).Select(item => new GuideSearchAction
                {
                    Label = item.Name,
                    Query = item.SearchQuery,
                    Detail = item.Detail,
                    ObjectId = item.ObjectId,
                    IconTexture = item.IconTexture,
                    IconSpriteIndex = item.IconSpriteIndex,
                    IconWidth = item.IconWidth,
                    IconHeight = item.IconHeight
                }));
                festivalResult.Sections.Add(shopSection);
            }

            _searchIndex.Add(festivalResult);
            foreach (var shopItem in definition.ShopItems)
            {
                if (itemResultsByQualifiedId.TryGetValue(shopItem.ItemId, out var itemResult))
                {
                    AddSectionAction(itemResult, "可购买", new GuideSearchAction
                    {
                        Label = definition.Name,
                        Query = definition.Name,
                        Detail = string.Join(" · ", new[] { definition.DateText, shopItem.Detail }
                            .Where(value => !string.IsNullOrWhiteSpace(value)))
                    });
                }
            }
        }

        foreach (var definition in LoadBundleDefinitions(bundleData, localizedObjects))
        {
            _bundleDefinitions.Add(definition);
            var bundleResult = new GuideSearchResult
            {
                Category = "任务",
                Title = $"{definition.RoomName} · {definition.Name}",
                Detail = $"需要：{string.Join("、", definition.Items.Take(6).Select(item => item.DisplayText))}",
                ObjectId = definition.Items.First().ObjectId
            };
            var section = new GuideSearchSection { Title = "所需物品" };
            section.Actions.AddRange(definition.Items.Select(item => new GuideSearchAction
            {
                Label = item.Name,
                Query = item.Name,
                Detail = item.Stack > 1 ? $"x{item.Stack}" : string.Empty,
                ObjectId = item.ObjectId
            }));
            bundleResult.Sections.Add(section);
            _searchIndex.Add(bundleResult);
            foreach (var item in definition.Items)
            {
                if (objectResultsById.TryGetValue(item.ObjectId, out var objectResult))
                {
                    AddSectionAction(objectResult, "社区中心", new GuideSearchAction
                    {
                        Label = $"{definition.RoomName} · {definition.Name}",
                        Query = bundleResult.Title,
                        Detail = item.DisplayText,
                        ObjectId = definition.Items.First().ObjectId
                    });
                }
            }
        }

        foreach (var item in LoadRecipeCollectionItems(
            cookingRecipes,
            objectNamesById,
            catalogItemsByQualifiedId,
            "食材",
            "烹饪食谱",
            useBigCraftableOutputFlag: false))
        {
            AddCollectionCatalogItem("Cooking", item);
        }

        foreach (var item in LoadRecipeCollectionItems(
            craftingRecipes,
            objectNamesById,
            catalogItemsByQualifiedId,
            "材料",
            "制造配方",
            useBigCraftableOutputFlag: true))
        {
            AddCollectionCatalogItem("Crafting", item);
        }

        foreach (var recipe in LoadRecipeSearchResults(
            cookingRecipes,
            objectNamesById,
            catalogItemsByQualifiedId,
            "菜谱",
            useBigCraftableOutputFlag: false).ToList())
        {
            var result = recipe.Result;
            if (recipe.Output.ObjectId is { } objectId && objectResultsById.TryGetValue(objectId, out var objectResult))
            {
                AddClonedSections(objectResult, result.Sections);
            }
            else if (itemResultsByQualifiedId.TryGetValue(recipe.Output.ItemId, out var itemResult))
            {
                AddClonedSections(itemResult, result.Sections);
            }

            AddRecipeReverseAssociations(result, objectResultsById, "用于菜谱");
            _searchIndex.Add(result);
        }

        foreach (var recipe in LoadRecipeSearchResults(
            craftingRecipes,
            objectNamesById,
            catalogItemsByQualifiedId,
            "配方",
            useBigCraftableOutputFlag: true).ToList())
        {
            var result = recipe.Result;
            if (recipe.Output.ObjectId is { } objectId && objectResultsById.TryGetValue(objectId, out var objectResult))
            {
                AddClonedSections(objectResult, result.Sections);
            }
            else if (itemResultsByQualifiedId.TryGetValue(recipe.Output.ItemId, out var itemResult))
            {
                AddClonedSections(itemResult, result.Sections);
            }

            AddRecipeReverseAssociations(result, objectResultsById, "用于配方");
            _searchIndex.Add(result);
        }

        var cropType = gameData.GetType("StardewValley.GameData.Crops.CropData")
            ?? throw new InvalidOperationException("游戏作物数据类型不可用。");
        foreach (var (seedIdText, value) in LoadTypedDictionary(contentType, content, "Data\\Crops", cropType))
        {
            if (!int.TryParse(seedIdText, out var seedId)
                || !int.TryParse(GetString(value, "HarvestItemId"), out var harvestId)
                || !localizedObjects.TryGetValue(harvestId, out var name))
            {
                continue;
            }

            var growDays = GetIntList(value, "DaysInPhase").Sum();
            var regrowDays = GetInt(value, "RegrowDays");
            var record = new CropRecord
            {
                ObjectId = harvestId,
                Name = name,
                Season = string.Join(" / ", GetStringList(value, "Seasons").Select(TranslateSeason)),
                SeedPrice = objectPrices.GetValueOrDefault(seedId),
                GrowDays = growDays,
                SalePrice = objectPrices.GetValueOrDefault(harvestId),
                HarvestMinStack = Math.Max(1, GetInt(value, "HarvestMinStack")),
                RegrowDays = regrowDays > 0 ? regrowDays : null
            };
            _crops.Add(record);
            var cropResult = new GuideSearchResult
            {
                Category = "作物",
                Title = record.Name,
                Detail = $"{record.GrowthText} · {record.ProfitText}",
                ObjectId = harvestId
            };
            var cropSection = new GuideSearchSection { Title = "收益信息" };
            cropSection.Lines.Add(record.GrowthText);
            cropSection.Lines.Add(record.HarvestText);
            cropSection.Lines.Add(record.ProfitText);
            cropSection.Lines.Add(record.RawPriceText);
            cropSection.Lines.Add(record.ArtisanPriceText);
            cropResult.Sections.Add(cropSection);
            if (objectResultsById.TryGetValue(harvestId, out var harvestResult))
            {
                AddSectionLineUnique(harvestResult, "种植", $"{record.GrowthText}；{record.ProfitText}；{record.ArtisanPriceText}");
            }

            if (objectResultsById.TryGetValue(seedId, out var seedResult))
            {
                AddSectionAction(seedResult, "种植结果", new GuideSearchAction
                {
                    Label = record.Name,
                    Query = record.Name,
                    Detail = record.GrowthText,
                    ObjectId = harvestId
                });
            }

            _searchIndex.Add(cropResult);
        }

        foreach (var (idText, raw) in fishData)
        {
            if (!int.TryParse(idText, out var id) || !localizedObjects.TryGetValue(id, out var name))
            {
                continue;
            }

            var fields = raw.Split('/');
            if (fields.Length < 8 || fields[1].Equals("trap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var record = new FishRecord
            {
                ObjectId = id,
                Name = name,
                Season = TranslateSeasons(fields[6]),
                Weather = TranslateWeather(fields[7]),
                Location = fields.Length > 8 ? TranslateLocations(fields[8]) : string.Empty,
                Time = TranslateTime(fields[5]),
                SortStartMinutes = ParseStartMinutes(fields[5]),
                SalePrice = objectPrices.GetValueOrDefault(id),
                IsLegendary = LegendaryFishIds.Contains(id),
                CommunityCenterNeeded = false
            };
            _fish.Add(record);
            AddCollectionCatalogItem(
                "Fish",
                new CollectionCatalogItem(
                    $"(O){id}",
                    name,
                    record.Detail,
                    id,
                    null,
                    null,
                    16,
                    16,
                    id));
            var fishResult = new GuideSearchResult
            {
                Category = "鱼类",
                Title = record.Name,
                Detail = $"{record.Season} · {record.Detail}",
                ObjectId = id
            };
            var fishSection = new GuideSearchSection { Title = "钓鱼信息" };
            fishSection.Lines.Add($"时间：{record.Time}");
            fishSection.Lines.Add($"地点：{record.Location}");
            fishSection.Lines.Add($"季节/天气：{record.SeasonWeatherText}");
            fishSection.Lines.Add(record.PriceText);
            fishSection.Lines.Add(record.SmokedPriceText);
            if (record.IsLegendary)
            {
                fishSection.Lines.Add("类型：鱼王");
            }

            fishResult.Sections.Add(fishSection);
            if (objectResultsById.TryGetValue(id, out var objectResult))
            {
                AddClonedSections(objectResult, [fishSection]);
            }

            _searchIndex.Add(fishResult);
        }
    }

    private static IEnumerable<CommunityBundleDefinition> LoadBundleDefinitions(
        IReadOnlyDictionary<string, string> bundleData,
        IReadOnlyDictionary<int, string> localizedObjects)
    {
        foreach (var (key, raw) in bundleData)
        {
            var keyParts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (keyParts.Length < 2 || !int.TryParse(keyParts[1], out var bundleId))
            {
                continue;
            }

            var fields = raw.Split('/');
            if (fields.Length < 3)
            {
                continue;
            }

            var items = ParseBundleItems(fields[2], localizedObjects).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var requiredCount = fields.Length > 4 && int.TryParse(fields[4], out var required) && required > 0
                ? required
                : items.Count;
            var localizedName = fields.LastOrDefault(field => !string.IsNullOrWhiteSpace(field)) ?? fields[0];
            yield return new CommunityBundleDefinition(
                bundleId,
                TranslateBundleRoom(keyParts[0]),
                localizedName,
                Math.Min(requiredCount, items.Count),
                items);
        }
    }

    private static IEnumerable<CommunityBundleItemDefinition> ParseBundleItems(
        string raw,
        IReadOnlyDictionary<int, string> localizedObjects)
    {
        var values = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < values.Length; index += 3)
        {
            if (!int.TryParse(values[index], out var objectId)
                || !int.TryParse(values[index + 1], out var stack)
                || !int.TryParse(values[index + 2], out var quality)
                || !localizedObjects.TryGetValue(objectId, out var name))
            {
                continue;
            }

            yield return new CommunityBundleItemDefinition(
                objectId,
                name,
                Math.Max(1, stack),
                quality);
        }
    }

    private static BundleRecord CreateBundleRecord(CommunityBundleDefinition bundle, SaveInfo? save)
    {
        var states = save?.CommunityBundleStates.GetValueOrDefault(bundle.Id);
        var completed = states is null
            ? 0
            : states.Take(bundle.Items.Count).Count(state => state);
        var missingItems = save is null ? bundle.Items : GetMissingItems(bundle, save).ToList();
        var missingText = FormatMissingItems(missingItems, Math.Max(0, bundle.RequiredCount - completed));

        return new BundleRecord
        {
            ObjectId = missingItems.FirstOrDefault()?.ObjectId ?? bundle.Items.First().ObjectId,
            Name = $"{bundle.RoomName} · {bundle.Name}",
            Season = bundle.RoomName,
            ItemHint = missingText,
            CompletedCount = completed,
            RequiredCount = bundle.RequiredCount
        };
    }

    private static IReadOnlyList<CommunityBundleItemDefinition> GetMissingItems(
        CommunityBundleDefinition bundle,
        SaveInfo save)
    {
        if (!save.CommunityBundleStates.TryGetValue(bundle.Id, out var states))
        {
            return bundle.Items;
        }

        var completed = states.Take(bundle.Items.Count).Count(state => state);
        if (completed >= bundle.RequiredCount)
        {
            return [];
        }

        return bundle.Items
            .Where((_, index) => index >= states.Count || !states[index])
            .ToList();
    }

    private static string FormatMissingItems(
        IReadOnlyList<CommunityBundleItemDefinition> items,
        int missingCount)
    {
        if (items.Count == 0)
        {
            return "无缺项";
        }

        var text = string.Join("、", items.Take(6).Select(item => item.DisplayText));
        if (items.Count > 6)
        {
            text += $" 等 {items.Count} 项";
        }

        return missingCount > 0 && items.Count > missingCount
            ? $"任选 {missingCount} 项：{text}"
            : text;
    }

    private Dictionary<string, IReadOnlyList<FestivalShopItemDefinition>> LoadFestivalShopItems(
        Type contentType,
        IDisposable content,
        Assembly gameData,
        IReadOnlyDictionary<string, string> localizedText,
        IReadOnlyDictionary<string, CatalogItemReference> catalogItemsByQualifiedId)
    {
        var shopType = gameData.GetType("StardewValley.GameData.Shops.ShopData");
        if (shopType is null)
        {
            return [];
        }

        var result = new Dictionary<string, List<FestivalShopItemDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (shopId, shop) in LoadTypedDictionary(contentType, content, "Data\\Shops", shopType))
        {
            var festivalKey = FestivalAssetKeyForShop(shopId);
            if (festivalKey is null)
            {
                continue;
            }

            if (!result.TryGetValue(festivalKey, out var items))
            {
                items = [];
                result[festivalKey] = items;
            }

            foreach (var item in GetEnumerableObjects(shop, "Items"))
            {
                var itemId = GetMemberString(item, "ItemId");
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                var price = GetMemberInt(item, "Price");
                var stock = GetMemberInt(item, "AvailableStock");
                var reference = ResolveShopItem(itemId, catalogItemsByQualifiedId);
                if (string.IsNullOrWhiteSpace(reference.Name))
                {
                    continue;
                }

                var detail = price > 0
                    ? $"{price}g{(stock > 0 ? $" · 限购 {stock}" : string.Empty)}"
                    : stock > 0 ? $"限购 {stock}" : string.Empty;
                items.Add(new FestivalShopItemDefinition(
                    reference.ItemId,
                    reference.Name,
                    reference.Name,
                    reference.ObjectId,
                    reference.IconTexture,
                    reference.IconSpriteIndex,
                    reference.IconWidth,
                    reference.IconHeight,
                    detail));
            }
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FestivalShopItemDefinition>)pair.Value);
    }

    private CatalogItemReference ResolveShopItem(
        string itemId,
        IReadOnlyDictionary<string, CatalogItemReference> catalogItemsByQualifiedId)
    {
        if (catalogItemsByQualifiedId.TryGetValue(itemId, out var reference))
        {
            return reference;
        }

        var match = QualifiedItemType().Match(itemId);
        if (match.Success)
        {
            var type = match.Groups[1].Value;
            var idText = match.Groups[2].Value;
            if (type == "O" && int.TryParse(idText, out var objectId) && _objectNamesById.TryGetValue(objectId, out var objectName))
            {
                return new CatalogItemReference($"(O){objectId}", objectName, "物品", string.Empty, objectId, null, null, 16, 16);
            }
        }

        if (int.TryParse(itemId, out var rawObjectId) && _objectNamesById.TryGetValue(rawObjectId, out var rawObjectName))
        {
            return new CatalogItemReference($"(O){rawObjectId}", rawObjectName, "物品", string.Empty, rawObjectId, null, null, 16, 16);
        }

        var name = TranslateKnownQualifiedItem(itemId);
        return new CatalogItemReference(itemId, name, "物品", string.Empty, null, null, null, 16, 16);
    }

    private static IEnumerable<(CatalogItemReference Reference, GuideSearchResult Result)> LoadAdditionalItemResults(
        Type contentType,
        IDisposable content,
        Assembly gameData,
        IReadOnlyDictionary<string, string> localizedText)
    {
        foreach (var item in LoadTypedItemResults(
            contentType,
            content,
            gameData,
            localizedText,
            "Data\\BigCraftables",
            "StardewValley.GameData.BigCraftables.BigCraftableData",
            "BC",
            "大型物品",
            @"TileSheets\Craftables",
            16,
            32,
            "SpriteIndex",
            "Price"))
        {
            yield return item;
        }

        foreach (var item in LoadTypedItemResults(
            contentType,
            content,
            gameData,
            localizedText,
            "Data\\Weapons",
            "StardewValley.GameData.Weapons.WeaponData",
            "W",
            "武器",
            @"TileSheets\weapons",
            16,
            16,
            "SpriteIndex",
            string.Empty))
        {
            yield return item;
        }

        foreach (var item in LoadTypedItemResults(
            contentType,
            content,
            gameData,
            localizedText,
            "Data\\Tools",
            "StardewValley.GameData.Tools.ToolData",
            "T",
            "工具",
            @"TileSheets\tools",
            16,
            16,
            "SpriteIndex",
            "SalePrice"))
        {
            yield return item;
        }

        foreach (var item in LoadTypedItemResults(
            contentType,
            content,
            gameData,
            localizedText,
            "Data\\Trinkets",
            "StardewValley.GameData.TrinketData",
            "TR",
            "饰品",
            @"TileSheets\Objects_2",
            16,
            16,
            "SheetIndex",
            string.Empty))
        {
            yield return item;
        }
    }

    private static IEnumerable<(CatalogItemReference Reference, GuideSearchResult Result)> LoadTypedItemResults(
        Type contentType,
        IDisposable content,
        Assembly gameData,
        IReadOnlyDictionary<string, string> localizedText,
        string asset,
        string typeName,
        string qualifiedType,
        string category,
        string defaultTexture,
        int iconWidth,
        int iconHeight,
        string spriteField,
        string priceField)
    {
        var type = gameData.GetType(typeName);
        if (type is null)
        {
            yield break;
        }

        foreach (var (id, value) in LoadTypedDictionary(contentType, content, asset, type))
        {
            var name = ResolveText(GetString(value, "DisplayName"), localizedText);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ResolveText(GetString(value, "Name"), localizedText);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = ResolveText(GetString(value, "Description"), localizedText);
            var price = string.IsNullOrWhiteSpace(priceField) ? 0 : GetInt(value, priceField);
            var texture = GetString(value, "Texture");
            if (string.IsNullOrWhiteSpace(texture))
            {
                texture = defaultTexture;
            }

            var spriteIndex = GetInt(value, spriteField);
            var detail = BuildItemDetail(description, price);
            var result = new GuideSearchResult
            {
                Category = category,
                Title = name,
                Detail = detail,
                IconTexture = texture,
                IconSpriteIndex = spriteIndex,
                IconWidth = iconWidth,
                IconHeight = iconHeight
            };
            var section = new GuideSearchSection { Title = $"{category}信息" };
            AddSectionLine(section, "售价", price > 0 ? $"{price}g" : string.Empty);
            AddSectionLine(section, "描述", description);
            AddTypedItemExtraLines(section, category, value);
            if (section.Lines.Count > 0)
            {
                result.Sections.Add(section);
            }

            var reference = new CatalogItemReference(
                $"({qualifiedType}){id}",
                name,
                category,
                detail,
                null,
                texture,
                spriteIndex,
                iconWidth,
                iconHeight);
            yield return (reference, result);
        }
    }

    private static IEnumerable<(CatalogItemReference Reference, GuideSearchResult Result)> LoadDelimitedItemResults(
        Type contentType,
        IDisposable content,
        IReadOnlyDictionary<string, string> localizedText)
    {
        foreach (var item in LoadDelimitedItemResults(
            contentType,
            content,
            localizedText,
            "Data\\Furniture",
            "F",
            "家具",
            @"TileSheets\furniture",
            16,
            16,
            nameIndex: 7,
            descriptionIndex: -1,
            priceIndex: 5))
        {
            yield return item;
        }

        foreach (var item in LoadDelimitedItemResults(
            contentType,
            content,
            localizedText,
            "Data\\hats.zh-CN",
            "H",
            "帽子",
            @"Characters\Farmer\hats",
            20,
            20,
            nameIndex: 5,
            descriptionIndex: 1,
            priceIndex: -1))
        {
            yield return item;
        }

        foreach (var item in LoadDelimitedItemResults(
            contentType,
            content,
            localizedText,
            "Data\\Boots.zh-CN",
            "B",
            "鞋子",
            @"Characters\Farmer\shoeColors",
            4,
            4,
            nameIndex: 6,
            descriptionIndex: 1,
            priceIndex: 2))
        {
            yield return item;
        }
    }

    private static IEnumerable<(CatalogItemReference Reference, GuideSearchResult Result)> LoadDelimitedItemResults(
        Type contentType,
        IDisposable content,
        IReadOnlyDictionary<string, string> localizedText,
        string asset,
        string qualifiedType,
        string category,
        string texture,
        int iconWidth,
        int iconHeight,
        int nameIndex,
        int descriptionIndex,
        int priceIndex)
    {
        var data = TryLoadStringDictionary(contentType, content, asset);
        if (data is null)
        {
            yield break;
        }

        foreach (var (id, raw) in data)
        {
            var fields = raw.Split('/');
            if (fields.Length <= nameIndex)
            {
                continue;
            }

            var name = ResolveText(fields[nameIndex], localizedText);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = fields[0];
            }

            var description = descriptionIndex >= 0 && fields.Length > descriptionIndex
                ? ResolveText(fields[descriptionIndex], localizedText)
                : string.Empty;
            var price = priceIndex >= 0 && fields.Length > priceIndex && int.TryParse(fields[priceIndex], out var parsedPrice)
                ? parsedPrice
                : 0;
            var spriteIndex = int.TryParse(id, out var parsedId) ? parsedId : 0;
            var detail = BuildItemDetail(description, price);
            var result = new GuideSearchResult
            {
                Category = category,
                Title = name,
                Detail = detail,
                IconTexture = texture,
                IconSpriteIndex = spriteIndex,
                IconWidth = iconWidth,
                IconHeight = iconHeight
            };
            var section = new GuideSearchSection { Title = $"{category}信息" };
            AddSectionLine(section, "售价", price > 0 ? $"{price}g" : string.Empty);
            AddSectionLine(section, "描述", description);
            if (section.Lines.Count > 0)
            {
                result.Sections.Add(section);
            }

            var reference = new CatalogItemReference(
                $"({qualifiedType}){id}",
                name,
                category,
                detail,
                null,
                texture,
                spriteIndex,
                iconWidth,
                iconHeight);
            yield return (reference, result);
        }
    }

    private static string BuildItemDetail(string description, int price)
    {
        if (price > 0 && !string.IsNullOrWhiteSpace(description))
        {
            return $"{description} · 售价 {price}g";
        }

        return price > 0 ? $"售价 {price}g" : description;
    }

    private static void AddTypedItemExtraLines(GuideSearchSection section, string category, object value)
    {
        if (category == "武器")
        {
            var min = GetInt(value, "MinDamage");
            var max = GetInt(value, "MaxDamage");
            if (min > 0 || max > 0)
            {
                section.Lines.Add($"伤害：{min}-{max}");
            }

            AddSectionLine(section, "防御", GetInt(value, "Defense") > 0 ? GetInt(value, "Defense").ToString() : string.Empty);
            AddSectionLine(section, "速度", GetInt(value, "Speed") != 0 ? GetInt(value, "Speed").ToString() : string.Empty);
        }
        else if (category == "工具")
        {
            AddSectionLine(section, "升级等级", GetInt(value, "UpgradeLevel") > 0 ? GetInt(value, "UpgradeLevel").ToString() : string.Empty);
        }
        else if (category == "大型物品")
        {
            AddSectionLine(section, "可室内放置", GetBool(value, "CanBePlacedIndoors") ? "是" : string.Empty);
            AddSectionLine(section, "可室外放置", GetBool(value, "CanBePlacedOutdoors") ? "是" : string.Empty);
        }
    }

    private static string TranslateKnownQualifiedItem(string itemId)
    {
        return itemId switch
        {
            "(O)PrizeTicket" => "兑奖券",
            _ => QualifiedItemType().Replace(itemId, "$2")
        };
    }

    private static string? FestivalAssetKeyForShop(string shopId)
    {
        return shopId switch
        {
            "Festival_EggFestival_Pierre" => "spring13",
            "Festival_FlowerDance_Pierre" => "spring24",
            "Festival_Luau_Pierre" => "summer11",
            "Festival_DanceOfTheMoonlightJellies_Pierre" => "summer28",
            "Festival_StardewValleyFair_StarTokens" => "fall16",
            "Festival_SpiritsEve_Pierre" => "fall27",
            "Festival_FestivalOfIce_TravelingMerchant" => "winter8",
            "Festival_FeastOfTheWinterStar_Pierre" => "winter25",
            "Festival_NightMarket_DecorationBoat" => "winter15",
            "Festival_NightMarket_MagicBoat_Day1" => "winter15",
            "Festival_NightMarket_MagicBoat_Day2" => "winter16",
            "Festival_NightMarket_MagicBoat_Day3" => "winter17",
            _ => null
        };
    }

    private static (string Location, string TimeText) ParseFestivalSchedule(
        IReadOnlyDictionary<string, string> festivalData,
        IReadOnlyDictionary<string, string> locationNames)
    {
        if (!festivalData.TryGetValue("conditions", out var conditions))
        {
            return (string.Empty, string.Empty);
        }

        var parts = conditions.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var location = parts.Length > 0 ? TranslateLocationName(parts[0], locationNames) : string.Empty;
        var timeText = parts.Length > 1 ? TranslateTime(parts[1]) : string.Empty;
        return (location, timeText);
    }

    private static IReadOnlyList<string> BuildFestivalHighlights(
        string assetKey,
        IReadOnlyDictionary<string, string> festivalData)
    {
        var highlights = new List<string>();
        if (FestivalHighlight(assetKey) is { } staticHighlight)
        {
            highlights.Add(staticHighlight);
        }

        var scripts = string.Join("/", festivalData
            .Where(pair => pair.Key.Contains("mainEvent", StringComparison.OrdinalIgnoreCase)
                || pair.Key.StartsWith("after", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value));
        foreach (var activity in DetectFestivalActivities(scripts))
        {
            if (!highlights.Contains(activity))
            {
                highlights.Add(activity);
            }
        }

        return highlights;
    }

    private static string? FestivalHighlight(string assetKey) => assetKey switch
    {
        "spring13" => "特殊活动：彩蛋寻宝；节日商店出售草莓种子。",
        "spring24" => "特殊活动：花舞节；可邀请高好感单身角色跳舞。",
        "summer11" => "特殊活动：夏威夷宴会；百乐汤食材会影响州长评价。",
        "summer28" => "特殊活动：月光水母起舞；夜间观赏活动。",
        "fall16" => "特殊活动：星露谷展览会；展览评分和小游戏可获得星星币。",
        "fall27" => "特殊活动：万灵节迷宫；终点可获得金南瓜。",
        "winter8" => "特殊活动：冰钓比赛；获胜可得比赛奖励。",
        "winter25" => "特殊活动：冬星盛宴；给秘密朋友送礼并收礼。",
        _ => null
    };

    private static IEnumerable<string> DetectFestivalActivities(string scripts)
    {
        if (scripts.Contains("playerControl eggFestival", StringComparison.OrdinalIgnoreCase))
        {
            yield return "活动入口：彩蛋寻宝。";
        }

        if (scripts.Contains("playerControl iceFishing", StringComparison.OrdinalIgnoreCase))
        {
            yield return "活动入口：冰钓比赛。";
        }

        if (scripts.Contains("awardFestivalPrize", StringComparison.OrdinalIgnoreCase))
        {
            yield return "奖励：完成节日比赛后发放。";
        }
    }

    private static string GetMemberString(object source, string property)
    {
        var type = source.GetType();
        return type.GetProperty(property)?.GetValue(source)?.ToString()
            ?? type.GetField(property)?.GetValue(source)?.ToString()
            ?? string.Empty;
    }

    private static int GetMemberInt(object source, string property)
    {
        var type = source.GetType();
        var value = type.GetProperty(property)?.GetValue(source)
            ?? type.GetField(property)?.GetValue(source);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private void AddObjectAliases(IReadOnlyDictionary<int, GuideSearchResult> objectResultsById)
    {
        AddObjectAlias("水仙花", "黄水仙", objectResultsById);
        AddObjectAlias("黏土", "粘土", objectResultsById);
        AddObjectAlias("辣根", "野山葵", objectResultsById);
        AddObjectAlias("所有鱼", "太阳鱼", objectResultsById);
        AddObjectAlias("任意鱼类", "太阳鱼", objectResultsById);
    }

    private void AddObjectAlias(
        string alias,
        string objectName,
        IReadOnlyDictionary<int, GuideSearchResult> objectResultsById)
    {
        var objectId = FindObjectIdByName(objectName);
        if (objectId is { } id)
        {
            _objectIdsByName[NormalizeSearchName(alias)] = id;
            if (objectResultsById.TryGetValue(id, out var result))
            {
                var section = result.Sections.FirstOrDefault(section => section.Title == "别名")
                    ?? new GuideSearchSection { Title = "别名" };
                if (!result.Sections.Contains(section))
                {
                    result.Sections.Add(section);
                }

                var line = $"别名：{alias}";
                if (!section.Lines.Contains(line))
                {
                    section.Lines.Add(line);
                }
            }
        }
    }

    private static Dictionary<string, string> MergeLocalizedText(
        params (string Asset, IReadOnlyDictionary<string, string> Values)[] sources)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (asset, values) in sources)
        {
            foreach (var (key, value) in values)
            {
                merged[key] = value;
                merged[$"{asset}:{key}"] = value;
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> LoadLocationNames(
        Type contentType,
        IDisposable content,
        Assembly gameData,
        IReadOnlyDictionary<string, string> localizedText)
    {
        var locationType = gameData.GetType("StardewValley.GameData.Locations.LocationData");
        if (locationType is null)
        {
            return new Dictionary<string, string>();
        }

        var locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, value) in LoadTypedDictionary(contentType, content, "Data\\Locations", locationType))
        {
            var name = ResolveText(GetString(value, "DisplayName"), localizedText);
            if (!string.IsNullOrWhiteSpace(name))
            {
                locations[id] = name;
            }
        }

        return locations;
    }

    private static void AddSectionLine(GuideSearchSection section, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            section.Lines.Add($"{label}：{value}");
        }
    }

    private static void AddClonedSections(GuideSearchResult destination, IEnumerable<GuideSearchSection> sections)
    {
        foreach (var source in sections)
        {
            var section = new GuideSearchSection { Title = source.Title };
            section.Lines.AddRange(source.Lines);
            section.Actions.AddRange(source.Actions.Select(action => new GuideSearchAction
            {
                Label = action.Label,
                Query = action.Query,
                Detail = action.Detail,
                ObjectId = action.ObjectId,
                IconTexture = action.IconTexture,
                IconSpriteIndex = action.IconSpriteIndex,
                IconWidth = action.IconWidth,
                IconHeight = action.IconHeight,
                IconUri = action.IconUri
            }));
            destination.Sections.Add(section);
        }
    }

    private static void AddRecipeReverseAssociations(
        GuideSearchResult recipe,
        IReadOnlyDictionary<int, GuideSearchResult> objectResultsById,
        string sectionTitle)
    {
        foreach (var ingredient in recipe.Sections.SelectMany(section => section.Actions))
        {
            if (ingredient.ObjectId is not { } objectId || !objectResultsById.TryGetValue(objectId, out var objectResult))
            {
                continue;
            }

            AddSectionAction(objectResult, sectionTitle, new GuideSearchAction
            {
                Label = recipe.Title,
                Query = recipe.Title,
                Detail = ingredient.Detail,
                ObjectId = recipe.ObjectId,
                IconTexture = recipe.IconTexture,
                IconSpriteIndex = recipe.IconSpriteIndex,
                IconWidth = recipe.IconWidth,
                IconHeight = recipe.IconHeight
            });
        }
    }

    private static void AddSectionLineUnique(GuideSearchResult result, string title, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var section = result.Sections.FirstOrDefault(item => item.Title == title);
        if (section is null)
        {
            section = new GuideSearchSection { Title = title };
            result.Sections.Add(section);
        }

        if (!section.Lines.Contains(line, StringComparer.CurrentCultureIgnoreCase))
        {
            section.Lines.Add(line);
        }
    }

    private static void AddSectionAction(GuideSearchResult result, string title, GuideSearchAction action)
    {
        var section = result.Sections.FirstOrDefault(item => item.Title == title);
        if (section is null)
        {
            section = new GuideSearchSection { Title = title };
            result.Sections.Add(section);
        }

        if (section.Actions.Any(item =>
            item.Label.Equals(action.Label, StringComparison.CurrentCultureIgnoreCase)
            && item.Query.Equals(action.Query, StringComparison.CurrentCultureIgnoreCase)
            && item.Detail.Equals(action.Detail, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        section.Actions.Add(action);
    }

    private static IEnumerable<string> FormatRelationships(
        IReadOnlyDictionary<string, string> relationships,
        IReadOnlyDictionary<string, string> npcStrings,
        IReadOnlyDictionary<string, string> localizedText)
    {
        foreach (var (key, value) in relationships)
        {
            var name = ResolveNpcName(key, npcStrings);
            var relation = TranslateRelationship(ResolveText(value, localizedText));
            yield return string.IsNullOrWhiteSpace(relation) || relation == value && relation == key
                ? name
                : $"{relation}：{name}";
        }
    }

    private static string FormatHomes(
        object character,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var homes = GetEnumerableObjects(character, "Home")
            .Select(home =>
            {
                var location = GetString(home, "Location");
                var condition = GetString(home, "Condition");
                var name = TranslateLocationName(location, locationNames);
                return string.IsNullOrWhiteSpace(condition)
                    ? name
                    : $"{name}（{FormatCondition(condition)}）";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(" / ", homes);
    }

    private static GuideSearchSection? CreateScheduleSection(
        IReadOnlyDictionary<string, string>? schedules,
        IReadOnlyDictionary<string, string> locationNames)
    {
        if (schedules is null || schedules.Count == 0)
        {
            return null;
        }

        var section = new GuideSearchSection { Title = "行程" };
        foreach (var (key, value) in schedules
            .OrderBy(item => SchedulePriority(item.Key))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8))
        {
            var route = FormatScheduleRoute(value, locationNames);
            if (!string.IsNullOrWhiteSpace(route))
            {
                section.Lines.Add($"{TranslateScheduleKey(key)}：{route}");
            }
        }

        return section.Lines.Count == 0 ? null : section;
    }

    private static IReadOnlyDictionary<string, List<string>> LoadCharacterEventSummaries(
        Type contentType,
        IDisposable content,
        string gamePath,
        IReadOnlyDictionary<string, string> npcStrings,
        IReadOnlyDictionary<string, string> localizedText,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var eventRoot = Path.Combine(gamePath, "Content", "Data", "Events");
        if (!Directory.Exists(eventRoot))
        {
            return result;
        }

        var contentRoot = Path.Combine(gamePath, "Content");
        foreach (var file in Directory.EnumerateFiles(eventRoot, "*.xnb")
            .Where(file => !LocalizedXnbFile().IsMatch(Path.GetFileName(file))))
        {
            var asset = Path.ChangeExtension(Path.GetRelativePath(contentRoot, file), null);
            if (string.IsNullOrWhiteSpace(asset))
            {
                continue;
            }

            var events = TryLoadStringDictionary(contentType, content, asset);
            if (events is null)
            {
                continue;
            }

            var location = TranslateLocationName(Path.GetFileNameWithoutExtension(file), locationNames);
            foreach (var (key, script) in events)
            {
                var searchText = $"{key} {script}";
                foreach (var npcId in npcStrings.Keys.Where(npc => ContainsNpcToken(searchText, npc)))
                {
                    if (!result.TryGetValue(npcId, out var lines))
                    {
                        lines = [];
                        result[npcId] = lines;
                    }

                    var summary = $"{location}：{FormatEventCondition(key, npcId, npcStrings, localizedText, locationNames)}";
                    if (!lines.Contains(summary))
                    {
                        lines.Add(summary);
                    }
                }
            }
        }

        return result;
    }

    private static bool ContainsNpcToken(string value, string npcId)
    {
        return Regex.IsMatch(
            value,
            $@"(^|[^A-Za-z0-9_]){Regex.Escape(npcId)}([^A-Za-z0-9_]|$)",
            RegexOptions.IgnoreCase);
    }

    private static string FormatEventCondition(
        string key,
        string npcId,
        IReadOnlyDictionary<string, string> npcStrings,
        IReadOnlyDictionary<string, string> localizedText,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var parts = new List<string>();
        foreach (Match friendship in Regex.Matches(key, @"(?:^|/)f\s+([A-Za-z0-9_ ]+?)\s+(\d+)(?=/|$)", RegexOptions.IgnoreCase))
        {
            var name = ResolveNpcName(friendship.Groups[1].Value.Trim(), npcStrings);
            if (int.TryParse(friendship.Groups[2].Value, out var points))
            {
                parts.Add($"{name} {points / 250} 心");
            }
        }

        var time = Regex.Match(key, @"(?:^|/)t\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
        if (time.Success)
        {
            parts.Add($"{FormatClock(time.Groups[1].Value)} - {FormatClock(time.Groups[2].Value)}");
        }

        var season = Regex.Match(key, @"(?:^|/)s\s+(\w+)", RegexOptions.IgnoreCase);
        if (season.Success)
        {
            parts.Add(TranslateSeason(season.Groups[1].Value));
        }

        var weather = Regex.Match(key, @"(?:^|/)w\s+(\w+)", RegexOptions.IgnoreCase);
        if (weather.Success)
        {
            parts.Add(TranslateWeather(weather.Groups[1].Value));
        }

        foreach (Match eventSeen in Regex.Matches(key, @"(?:^|/)e\s+(\d+)", RegexOptions.IgnoreCase))
        {
            parts.Add($"已看过事件 {eventSeen.Groups[1].Value}");
        }

        foreach (Match mail in Regex.Matches(key, @"(?:^|/)[mM]\s+([^/]+)", RegexOptions.IgnoreCase))
        {
            parts.Add($"已有邮件：{FormatConditionValue(mail.Groups[1].Value, localizedText, locationNames)}");
        }

        foreach (Match quest in Regex.Matches(key, @"(?:^|/)q\s+(\d+)", RegexOptions.IgnoreCase))
        {
            parts.Add($"任务进度：{quest.Groups[1].Value}");
        }

        foreach (Match location in Regex.Matches(key, @"(?:^|/)l\s+([^/]+)", RegexOptions.IgnoreCase))
        {
            parts.Add($"地点：{TranslateLocationName(location.Groups[1].Value.Trim(), locationNames)}");
        }

        var known = Regex.Matches(key, @"(?:^|/)(?:f\s+[A-Za-z0-9_ ]+?\s+\d+|t\s+\d+\s+\d+|s\s+\w+|w\s+\w+|e\s+\d+|[mM]\s+[^/]+|q\s+\d+|l\s+[^/]+)", RegexOptions.IgnoreCase).Count;
        var total = key.Split('/', StringSplitOptions.RemoveEmptyEntries).Count(token => !EventIdToken().IsMatch(token.Trim()));
        if (total > known)
        {
            parts.Add($"其他前置条件 {total - known} 项");
        }

        return parts.Count > 0 ? string.Join(" · ", parts.Distinct()) : "特殊事件";
    }

    private static string FormatCondition(string value)
    {
        return value
            .Replace("PLAYER_HAS_MAIL", "已有邮件")
            .Replace("PLAYER_HAS_FLAG", "已有标记")
            .Replace("LOCATION_CONTEXT", "地点环境")
            .Replace("SEASON", "季节")
            .Replace("YEAR", "年份")
            .Replace("DAY_OF_MONTH", "日期")
            .Replace("WEATHER", "天气")
            .Replace("NOT", "未满足")
            .Replace("ANY", "任一")
            .Replace("ALL", "全部");
    }

    private static string FormatConditionValue(
        string value,
        IReadOnlyDictionary<string, string> localizedText,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var resolved = ResolveText(value.Trim(), localizedText);
        return TranslateLocationName(resolved, locationNames);
    }

    private static int SchedulePriority(string key)
    {
        var normalized = key.ToLowerInvariant();
        if (normalized.StartsWith("rain", StringComparison.Ordinal) || normalized == "greenrain")
        {
            return 0;
        }

        if (normalized is "mon" or "tue" or "wed" or "thu" or "fri" or "sat" or "sun")
        {
            return 1;
        }

        if (normalized.Contains("marriage", StringComparison.Ordinal))
        {
            return 3;
        }

        return 2;
    }

    private static string FormatScheduleRoute(
        string value,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var steps = value
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(step => ParseScheduleStep(step, locationNames))
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Take(4)
            .ToList();
        return string.Join(" → ", steps);
    }

    private static string ParseScheduleStep(
        string step,
        IReadOnlyDictionary<string, string> locationNames)
    {
        var tokens = step.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 2 && tokens[0].Equals("GOTO", StringComparison.OrdinalIgnoreCase))
        {
            return $"沿用 {TranslateScheduleKey(tokens[1])}";
        }

        if (tokens.Length < 2 || !int.TryParse(tokens[0], out _))
        {
            return string.Empty;
        }

        return $"{FormatClock(tokens[0])} {TranslateLocationName(tokens[1], locationNames)}";
    }

    private static string TranslateScheduleKey(string key)
    {
        var normalized = key.ToLowerInvariant();
        var translated = normalized switch
        {
            "rain" => "雨天",
            "rain2" => "雨天 2",
            "greenrain" => "绿雨",
            "spring" => "春季晴天",
            "summer" => "夏季晴天",
            "fall" => "秋季晴天",
            "winter" => "冬季晴天",
            "mon" => "周一",
            "tue" => "周二",
            "wed" => "周三",
            "thu" => "周四",
            "fri" => "周五",
            "sat" => "周六",
            "sun" => "周日",
            _ => key.Replace('_', ' ')
        };
        return translated
            .Replace("spring ", "春季 ")
            .Replace("summer ", "夏季 ")
            .Replace("fall ", "秋季 ")
            .Replace("winter ", "冬季 ")
            .Replace("marriage ", "婚后 ")
            .Replace("DesertFestival", "沙漠节")
            .Replace(" Mon", " 周一")
            .Replace(" Tue", " 周二")
            .Replace(" Wed", " 周三")
            .Replace(" Thu", " 周四")
            .Replace(" Fri", " 周五")
            .Replace(" Sat", " 周六")
            .Replace(" Sun", " 周日")
            .Replace(" mon", " 周一")
            .Replace(" tue", " 周二")
            .Replace(" wed", " 周三")
            .Replace(" thu", " 周四")
            .Replace(" fri", " 周五")
            .Replace(" sat", " 周六")
            .Replace(" sun", " 周日");
    }

    private static string ResolveNpcName(string value, IReadOnlyDictionary<string, string> npcStrings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return npcStrings.TryGetValue(value, out var name) ? name : value;
    }

    private static string TranslateBundleRoom(string value) => value switch
    {
        "Pantry" => "茶水间",
        "Crafts Room" => "工艺室",
        "Fish Tank" => "鱼缸",
        "Boiler Room" => "锅炉房",
        "Vault" => "金库",
        "Bulletin Board" => "布告栏",
        "Abandoned Joja Mart" => "废弃 Joja 超市",
        _ => value
    };

    private static string TranslateHome(string value) => value switch
    {
        "SeedShop" => "皮埃尔杂货店",
        "Saloon" => "星之果实餐吧",
        "Trailer" => "拖车",
        "ManorHouse" => "镇长庄园",
        "AnimalShop" => "玛妮牧场",
        "ScienceHouse" => "木匠的家",
        "ElliottHouse" => "艾利欧特小屋",
        "JoshHouse" => "海莉与艾米丽的家",
        "SamHouse" => "山姆的家",
        "HarveyRoom" => "哈维诊所",
        "WizardHouse" => "法师塔",
        "Tent" => "莱纳斯帐篷",
        "FishShop" => "鱼店",
        "Blacksmith" => "铁匠铺",
        "Sewer" => "下水道",
        _ => value
    };

    private static string TranslateHomeRegion(string value) => value switch
    {
        "Town" => "鹈鹕镇",
        "Beach" => "海滩",
        "Forest" => "煤矿森林",
        "Mountain" => "山区",
        "Desert" => "沙漠",
        "Island" => "姜岛",
        _ => value
    };

    private static string TranslateAge(string value) => value switch
    {
        "Adult" => "成人",
        "Teen" => "青少年",
        "Child" => "儿童",
        _ => value
    };

    private static string TranslateGender(string value) => value switch
    {
        "Male" => "男性",
        "Female" => "女性",
        _ => value
    };

    private static string TranslateRelationship(string value) => value switch
    {
        "Parent" => "父母",
        "Child" => "子女",
        "Sibling" => "兄弟姐妹",
        "Spouse" => "配偶",
        "Friend" => "朋友",
        "Family" => "亲属",
        "LoveInterest" => "心仪对象",
        "mom" or "Mother" => "母亲",
        "dad" or "Father" => "父亲",
        "son" or "Son" => "儿子",
        "daughter" or "Daughter" => "女儿",
        "brother" or "Brother" => "兄弟",
        "sister" or "Sister" => "姐妹",
        "aunt" or "Aunt" => "姨母",
        "nephew" or "Nephew" => "外甥",
        "husband" or "Husband" => "丈夫",
        "wife" or "Wife" => "妻子",
        "roommate" or "Roommate" => "室友",
        _ => value
    };

    private static string TranslateLocationName(
        string value,
        IReadOnlyDictionary<string, string>? locationNames = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (locationNames is not null && locationNames.TryGetValue(value, out var localized))
        {
            return localized;
        }

        return value switch
        {
            "SeedShop" => "皮埃尔的杂货店",
            "Saloon" => "星之果实餐吧",
            "Trailer" => "拖车",
            "ManorHouse" => "镇长庄园",
            "AnimalShop" => "玛妮牧场",
            "ScienceHouse" => "木匠的家",
            "ElliottHouse" => "艾利欧特小屋",
            "HaleyHouse" => "艾米丽和海莉的家",
            "JoshHouse" => "乔治、艾芙琳和亚历克斯的家",
            "SamHouse" => "山姆的家",
            "HarveyRoom" => "哈维诊所",
            "WizardHouse" => "法师塔",
            "Tent" => "莱纳斯帐篷",
            "FishShop" => "鱼店",
            "Blacksmith" => "铁匠铺",
            "Hospital" => "诊所",
            "Beach" => "海滩",
            "Town" => "鹈鹕镇",
            "Forest" => "煤矿森林",
            "Mountain" => "山区",
            "BusStop" => "公交站",
            "CommunityCenter" => "社区中心",
            "Desert" => "卡利科沙漠",
            "bed" => "床",
            _ => value
        };
    }

    private static IEnumerable<RecipeSearchResultDefinition> LoadRecipeSearchResults(
        IReadOnlyDictionary<string, string> recipeData,
        IReadOnlyDictionary<string, string> objectNamesById,
        IReadOnlyDictionary<string, CatalogItemReference> catalogItemsByQualifiedId,
        string category,
        bool useBigCraftableOutputFlag)
    {
        foreach (var (_, raw) in recipeData)
        {
            var fields = raw.Split('/');
            if (fields.Length < 3)
            {
                continue;
            }

            var outputTokens = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var outputId = outputTokens.FirstOrDefault();
            var outputReference = ResolveRecipeOutputReference(
                outputId,
                useBigCraftableOutputFlag && IsBigCraftableRecipeOutput(fields),
                objectNamesById,
                catalogItemsByQualifiedId);
            if (outputReference is null)
            {
                continue;
            }

            var ingredients = ParseRecipeIngredients(fields[0], objectNamesById).ToList();
            if (ingredients.Count == 0)
            {
                continue;
            }

            var result = new GuideSearchResult
            {
                Category = category,
                Title = outputReference.Name,
                Detail = $"{category}：{string.Join("、", ingredients.Select(item => item.DisplayText))}",
                ObjectId = outputReference.ObjectId,
                IconTexture = outputReference.IconTexture,
                IconSpriteIndex = outputReference.IconSpriteIndex,
                IconWidth = outputReference.IconWidth,
                IconHeight = outputReference.IconHeight
            };
            var section = new GuideSearchSection { Title = category == "菜谱" ? "所需食材" : "所需材料" };
            section.Actions.AddRange(ingredients.Select(item => new GuideSearchAction
            {
                Label = item.Name,
                Query = item.SearchQuery,
                Detail = $"x{item.Stack}",
                ObjectId = item.ObjectId
            }));
            result.Sections.Add(section);
            yield return new RecipeSearchResultDefinition(outputReference, result);
        }
    }

    private static IEnumerable<CollectionCatalogItem> LoadRecipeCollectionItems(
        IReadOnlyDictionary<string, string> recipeData,
        IReadOnlyDictionary<string, string> objectNamesById,
        IReadOnlyDictionary<string, CatalogItemReference> catalogItemsByQualifiedId,
        string ingredientLabel,
        string fallbackDetail,
        bool useBigCraftableOutputFlag)
    {
        foreach (var (recipeKey, raw) in recipeData)
        {
            var fields = raw.Split('/');
            if (fields.Length < 3)
            {
                continue;
            }

            var outputTokens = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var outputId = outputTokens.FirstOrDefault();
            var outputReference = ResolveRecipeOutputReference(
                outputId,
                useBigCraftableOutputFlag && IsBigCraftableRecipeOutput(fields),
                objectNamesById,
                catalogItemsByQualifiedId);
            if (outputReference is null)
            {
                continue;
            }

            var ingredients = ParseRecipeIngredients(fields[0], objectNamesById).ToList();
            yield return new CollectionCatalogItem(
                recipeKey,
                outputReference.Name,
                ingredients.Count > 0
                    ? $"{ingredientLabel}：{string.Join("、", ingredients.Select(item => item.DisplayText))}"
                    : fallbackDetail,
                outputReference.ObjectId,
                outputReference.IconTexture,
                outputReference.IconSpriteIndex,
                outputReference.IconWidth,
                outputReference.IconHeight,
                RecipeOutputSortKey(outputReference, outputId));
        }
    }

    private static CatalogItemReference? ResolveRecipeOutputReference(
        string? rawOutputId,
        bool isBigCraftable,
        IReadOnlyDictionary<string, string> objectNamesById,
        IReadOnlyDictionary<string, CatalogItemReference> catalogItemsByQualifiedId)
    {
        var outputId = rawOutputId?.Trim();
        if (string.IsNullOrWhiteSpace(outputId))
        {
            return null;
        }

        var match = QualifiedItemType().Match(outputId);
        if (match.Success)
        {
            if (catalogItemsByQualifiedId.TryGetValue(outputId, out var qualifiedReference))
            {
                return qualifiedReference;
            }

            isBigCraftable = match.Groups[1].Value.Equals("BC", StringComparison.OrdinalIgnoreCase);
            outputId = match.Groups[2].Value;
        }

        if (isBigCraftable && catalogItemsByQualifiedId.TryGetValue($"(BC){outputId}", out var bigCraftableReference))
        {
            return bigCraftableReference;
        }

        if (catalogItemsByQualifiedId.TryGetValue($"(O){outputId}", out var objectReference))
        {
            return objectReference;
        }

        if (!isBigCraftable && catalogItemsByQualifiedId.TryGetValue(outputId, out var rawReference))
        {
            return rawReference;
        }

        if (objectNamesById.TryGetValue(outputId, out var outputName))
        {
            return new CatalogItemReference(
                $"(O){outputId}",
                outputName,
                "物品",
                string.Empty,
                int.TryParse(outputId, out var objectId) ? objectId : null,
                null,
                null,
                16,
                16);
        }

        return null;
    }

    private static bool IsBigCraftableRecipeOutput(IReadOnlyList<string> fields)
        => fields.Count > 3 && bool.TryParse(fields[3], out var isBigCraftable) && isBigCraftable;

    private static int RecipeOutputSortKey(CatalogItemReference outputReference, string? rawOutputId)
    {
        if (outputReference.ObjectId is { } objectId)
        {
            return objectId;
        }

        var match = QualifiedItemType().Match(outputReference.ItemId);
        if (match.Success && int.TryParse(match.Groups[2].Value, out var qualifiedId))
        {
            return qualifiedId;
        }

        return int.TryParse(rawOutputId, out var rawId) ? rawId : int.MaxValue;
    }

    private static IEnumerable<RecipeIngredientDefinition> ParseRecipeIngredients(
        string raw,
        IReadOnlyDictionary<string, string> objectNamesById)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < tokens.Length; index += 2)
        {
            var id = tokens[index];
            if (!int.TryParse(tokens[index + 1], out var stack))
            {
                continue;
            }

            yield return new RecipeIngredientDefinition(
                id,
                RecipeIngredientName(id, objectNamesById),
                Math.Max(1, stack),
                int.TryParse(id, out var objectId) && objectId >= 0 ? objectId : null);
        }
    }

    private static string RecipeIngredientName(string id, IReadOnlyDictionary<string, string> objectNamesById)
    {
        if (objectNamesById.TryGetValue(id, out var name))
        {
            return name;
        }

        return id switch
        {
            "-4" => "任意鱼类",
            "-5" => "任意蛋",
            "-6" => "任意奶",
            "-777" => "齐氏调味料",
            _ => id
        };
    }

    private static Dictionary<string, string> LoadStringDictionary(Type contentType, IDisposable content, string asset)
    {
        var dictionaryType = typeof(Dictionary<string, string>);
        return (Dictionary<string, string>)LoadAsset(contentType, content, asset, dictionaryType);
    }

    private static Dictionary<string, string>? TryLoadStringDictionary(Type contentType, IDisposable content, string asset)
    {
        try
        {
            return LoadStringDictionary(contentType, content, asset);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<KeyValuePair<string, object>> LoadTypedDictionary(
        Type contentType,
        IDisposable content,
        string asset,
        Type valueType)
    {
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dictionary = (System.Collections.IEnumerable)LoadAsset(contentType, content, asset, dictionaryType);
        foreach (var item in dictionary)
        {
            var itemType = item!.GetType();
            yield return new KeyValuePair<string, object>(
                (string)itemType.GetProperty("Key")!.GetValue(item)!,
                itemType.GetProperty("Value")!.GetValue(item)!);
        }
    }

    private static object LoadAsset(Type contentType, IDisposable content, string asset, Type assetType)
    {
        var method = contentType.GetMethods()
            .First(info => info.Name == "Load" && info.IsGenericMethodDefinition);
        return method.MakeGenericMethod(assetType).Invoke(content, [asset])
            ?? throw new InvalidDataException($"无法读取游戏内容：{asset}");
    }

    private static string GetString(object source, string property)
        => source.GetType().GetField(property)?.GetValue(source)?.ToString() ?? string.Empty;

    private static int GetInt(object source, string property)
        => source.GetType().GetField(property)?.GetValue(source) is int value ? value : 0;

    private static IReadOnlyList<int> GetIntList(object source, string property)
    {
        return source.GetType().GetField(property)?.GetValue(source) is System.Collections.IEnumerable values
            ? values.Cast<object>().Select(value => Convert.ToInt32(value)).ToList()
            : [];
    }

    private static IReadOnlyList<string> GetStringList(object source, string property)
    {
        return source.GetType().GetField(property)?.GetValue(source) is System.Collections.IEnumerable values
            ? values.Cast<object>().Select(value => value.ToString() ?? string.Empty).ToList()
            : [];
    }

    private static IReadOnlyList<object> GetEnumerableObjects(object source, string property)
    {
        return source.GetType().GetField(property)?.GetValue(source) is System.Collections.IEnumerable values
            ? values.Cast<object>().ToList()
            : [];
    }

    private static IReadOnlyDictionary<string, string> GetStringDictionary(object source, string property)
    {
        if (source.GetType().GetField(property)?.GetValue(source) is not System.Collections.IEnumerable values)
        {
            return new Dictionary<string, string>();
        }

        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            var itemType = item!.GetType();
            var key = itemType.GetProperty("Key")?.GetValue(item)?.ToString();
            var value = itemType.GetProperty("Value")?.GetValue(item)?.ToString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                dictionary[key] = value ?? string.Empty;
            }
        }

        return dictionary;
    }

    private static bool GetBool(object source, string property)
        => source.GetType().GetField(property)?.GetValue(source) is bool value && value;

    private static string ResolveText(string token, IReadOnlyDictionary<string, string> strings)
    {
        var match = LocalizedToken().Match(token);
        if (!match.Success)
        {
            return token;
        }

        var fullKey = $"{match.Groups[1].Value}:{match.Groups[2].Value}";
        if (strings.TryGetValue(fullKey, out var fullValue))
        {
            return fullValue;
        }

        return strings.TryGetValue(match.Groups[2].Value, out var value) ? value : token;
    }

    private static string TranslateSeasons(string value)
        => string.Join(" / ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(TranslateSeason));

    private static string TranslateSeason(string value) => value.ToLowerInvariant() switch
    {
        "spring" => "春季",
        "summer" => "夏季",
        "fall" => "秋季",
        "winter" => "冬季",
        _ => value
    };

    private static string TranslateWeather(string value) => value.ToLowerInvariant() switch
    {
        "sunny" => "晴朗",
        "rainy" => "雨天",
        "both" => "任意",
        _ => value
    };

    private static bool IsWeatherMatch(FishRecord fish, string? weather)
    {
        if (fish.Weather == "任意")
        {
            return true;
        }

        return weather is not null
            && (fish.Weather.Equals(weather, StringComparison.Ordinal)
                || (fish.Weather == "雨天" && weather == "雷雨"));
    }

    private static string TranslateLocations(string value)
    {
        var locations = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TranslateLocation)
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(" / ", locations);
    }

    private static string TranslateLocation(string value) => value.ToLowerInvariant() switch
    {
        "ocean" => "海洋",
        "town" => "小镇河流",
        "forest" => "森林河流",
        "mountain" => "山区湖泊",
        "lake" => "湖泊",
        "river" => "河流",
        "mine" or "undergroundmine" => "矿井",
        "sewer" or "sewers" => "下水道",
        "desert" => "沙漠",
        "woods" => "秘密森林",
        "island" or "islandsouth" or "islandwest" or "islandnorth" => "姜岛",
        "beachnightmarket" => "夜市潜艇",
        "submarine" => "潜艇",
        _ => value
    };

    private static string TranslateTime(string value)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return values.Length >= 2 ? $"{FormatClock(values[0])} - {FormatClock(values[1])}" : value;
    }

    private static int ParseStartMinutes(string value)
    {
        var first = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!int.TryParse(first, out var time))
        {
            return 0;
        }

        return time / 100 % 24 * 60 + time % 100;
    }

    private static string FormatClock(string raw)
    {
        if (!int.TryParse(raw, out var time))
        {
            return raw;
        }

        var hours = time / 100 % 24;
        return $"{hours}:{time % 100:00}";
    }

    private static int ToYearDay(string season, int day) => season switch
    {
        "春季" => day - 1,
        "夏季" => 28 + day - 1,
        "秋季" => 56 + day - 1,
        "冬季" => 84 + day - 1,
        _ => 0
    };

    private static int SeasonIconObjectId(string season) => season switch
    {
        "春季" => 24,
        "夏季" => 258,
        "秋季" => 276,
        "冬季" => 414,
        _ => 24
    };

    [GeneratedRegex(@"^\[LocalizedText ([^:]+):([^\]]+)\]$")]
    private static partial Regex LocalizedToken();

    [GeneratedRegex(@"^(spring|summer|fall|winter)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FestivalKey();

    [GeneratedRegex(@"\.[a-z]{2}(?:-[A-Z]{2})?\.xnb$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalizedXnbFile();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex EventIdToken();

    [GeneratedRegex(@"^\(([^)]+)\)(.+)$")]
    private static partial Regex QualifiedItemType();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed record FestivalDefinition(
        string Name,
        string Season,
        int Day,
        string Location,
        string TimeText,
        IReadOnlyList<string> Highlights,
        IReadOnlyList<FestivalShopItemDefinition> ShopItems)
    {
        public int YearDay => ToYearDay(Season, Day);
        public string DateText => $"{Season} {Day} 日";
        public string CardDetail
        {
            get
            {
                var shopText = ShopItems.Count > 0
                    ? $"商店：{string.Join("、", ShopItems.Take(3).Select(item => item.DisplayText))}"
                    : string.Empty;
                var highlight = Highlights.FirstOrDefault() ?? string.Empty;
                return string.Join("；", new[] { highlight, shopText }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }

        public string SearchDetail
        {
            get
            {
                var values = new List<string> { DateText };
                if (!string.IsNullOrWhiteSpace(Location))
                {
                    values.Add(Location);
                }

                if (!string.IsNullOrWhiteSpace(TimeText))
                {
                    values.Add(TimeText);
                }

                if (!string.IsNullOrWhiteSpace(CardDetail))
                {
                    values.Add(CardDetail);
                }

                return string.Join(" · ", values);
            }
        }
    }

    private sealed record FestivalShopItemDefinition(
        string ItemId,
        string Name,
        string SearchQuery,
        int? ObjectId,
        string? IconTexture,
        int? IconSpriteIndex,
        int IconWidth,
        int IconHeight,
        string Detail)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(Detail) ? Name : $"{Name} {Detail}";
    }

    private sealed record CatalogItemReference(
        string ItemId,
        string Name,
        string Category,
        string Detail,
        int? ObjectId,
        string? IconTexture,
        int? IconSpriteIndex,
        int IconWidth,
        int IconHeight);

    private sealed record RecipeSearchResultDefinition(
        CatalogItemReference Output,
        GuideSearchResult Result);

    private sealed record CommunityBundleDefinition(
        int Id,
        string RoomName,
        string Name,
        int RequiredCount,
        IReadOnlyList<CommunityBundleItemDefinition> Items);

    private sealed record CommunityBundleItemDefinition(
        int ObjectId,
        string Name,
        int Stack,
        int Quality)
    {
        public string DisplayText => Quality > 0
            ? $"{Name} x{Stack}（{QualityName(Quality)}）"
            : $"{Name} x{Stack}";

        private static string QualityName(int quality) => quality switch
        {
            1 => "银星",
            2 => "金星",
            4 => "铱星",
            _ => $"{quality} 星"
        };
    }

    private sealed record CollectionCatalogItem(
        string ItemId,
        string Name,
        string Detail,
        int? ObjectId,
        string? IconTexture,
        int? IconSpriteIndex,
        int IconWidth,
        int IconHeight,
        int SortKey);

    private sealed record SearchTextCacheEntry(string Text, string LooseText);

    private sealed record RecipeIngredientDefinition(string Id, string Name, int Stack, int? ObjectId)
    {
        public string SearchQuery => Name;
        public string DisplayText => $"{Name} x{Stack}";
    }
}
