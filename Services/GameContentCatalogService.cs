using System.Reflection;
using System.Text.RegularExpressions;
using LSMA.Models;

namespace LSMA.Services;

public sealed partial class GameContentCatalogService(AppStateService state, LoggingService logging)
{
    private readonly List<GuideSearchResult> _searchIndex = [];
    private readonly List<FishRecord> _fish = [];
    private readonly List<FestivalDefinition> _festivals = [];

    public async Task PrepareAsync()
    {
        _searchIndex.Clear();
        _fish.Clear();
        _festivals.Clear();
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

        return _searchIndex
            .Where(item => $"{item.Title} {item.Detail} {item.Category}".Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => item.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Title)
            .Take(60)
            .ToList();
    }

    public IReadOnlyList<FestivalRecord> GetUpcomingFestivals(SaveInfo? save)
    {
        var currentDay = save is null ? 0 : ToYearDay(save.Season, save.Day);
        return _festivals
            .Select(festival => (Festival: festival, Remaining: (festival.YearDay - currentDay + 112) % 112))
            .OrderBy(value => value.Remaining)
            .Take(3)
            .Select(value => new FestivalRecord
            {
                Name = value.Festival.Name,
                Season = value.Festival.Season,
                Day = value.Festival.Day,
                CountdownText = save is null
                    ? value.Festival.DateText
                    : value.Remaining == 0 ? "今天" : $"{value.Remaining} 天后"
            })
            .ToList();
    }

    public IReadOnlyList<FishRecord> GetFishToday(SaveInfo? save)
    {
        if (save is null)
        {
            return _fish.Take(8).ToList();
        }

        return _fish
            .Where(fish => fish.Season.Contains(save.Season, StringComparison.Ordinal)
                && (fish.Weather == "任意" || fish.Weather == save.Weather))
            .OrderBy(fish => fish.Name)
            .ToList();
    }

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
        var festivalStrings = LoadStringDictionary(contentType, content, "Data\\Festivals\\FestivalDates.zh-CN");
        var fishData = LoadStringDictionary(contentType, content, "Data\\Fish");

        var objectType = gameData.GetType("StardewValley.GameData.Objects.ObjectData")
            ?? throw new InvalidOperationException("游戏对象数据类型不可用。");
        var objects = LoadTypedDictionary(contentType, content, "Data\\Objects", objectType);
        var localizedObjects = new Dictionary<int, string>();
        foreach (var (id, value) in objects)
        {
            var name = ResolveText(GetString(value, "DisplayName"), objectsStrings);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var detail = ResolveText(GetString(value, "Description"), objectsStrings);
            var price = GetInt(value, "Price");
            var texture = GetString(value, "Texture");
            var spriteIndex = GetInt(value, "SpriteIndex");
            var hasAlternateTexture = !string.IsNullOrWhiteSpace(texture);
            if (int.TryParse(id, out var objectId))
            {
                localizedObjects[objectId] = name;
            }

            _searchIndex.Add(new GuideSearchResult
            {
                Category = "物品",
                Title = name,
                Detail = price > 0 ? $"{detail} · 售价 {price}g" : detail,
                ObjectId = !hasAlternateTexture && int.TryParse(id, out objectId) ? objectId : null,
                IconTexture = hasAlternateTexture ? texture : null,
                IconSpriteIndex = hasAlternateTexture ? spriteIndex : null
            });
        }

        var characterType = gameData.GetType("StardewValley.GameData.Characters.CharacterData")
            ?? throw new InvalidOperationException("游戏角色数据类型不可用。");
        foreach (var (id, value) in LoadTypedDictionary(contentType, content, "Data\\Characters", characterType))
        {
            var name = ResolveText(GetString(value, "DisplayName"), npcStrings);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            var season = TranslateSeason(GetString(value, "BirthSeason"));
            var birthday = GetInt(value, "BirthDay");
            var datable = GetBool(value, "CanBeRomanced");
            _searchIndex.Add(new GuideSearchResult
            {
                Category = "人物",
                Title = name,
                NpcId = id,
                Detail = birthday > 0
                    ? $"{season} {birthday} 日生日{(datable ? " · 可恋爱" : string.Empty)}"
                    : datable ? "可恋爱角色" : "角色"
            });
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
            var definition = new FestivalDefinition(name, season, day);
            _festivals.Add(definition);
            _searchIndex.Add(new GuideSearchResult
            {
                Category = "节日",
                Title = name,
                Detail = definition.DateText
            });
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
                Time = TranslateTime(fields[5]),
                CommunityCenterNeeded = false
            };
            _fish.Add(record);
            _searchIndex.Add(new GuideSearchResult
            {
                Category = "鱼类",
                Title = record.Name,
                Detail = $"{record.Season} · {record.Detail}",
                ObjectId = id
            });
        }
    }

    private static Dictionary<string, string> LoadStringDictionary(Type contentType, IDisposable content, string asset)
    {
        var dictionaryType = typeof(Dictionary<string, string>);
        return (Dictionary<string, string>)LoadAsset(contentType, content, asset, dictionaryType);
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

    private static bool GetBool(object source, string property)
        => source.GetType().GetField(property)?.GetValue(source) is bool value && value;

    private static string ResolveText(string token, IReadOnlyDictionary<string, string> strings)
    {
        var match = LocalizedToken().Match(token);
        return match.Success && strings.TryGetValue(match.Groups[1].Value, out var value) ? value : token;
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

    private static string TranslateTime(string value)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return values.Length >= 2 ? $"{FormatClock(values[0])} - {FormatClock(values[1])}" : value;
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

    [GeneratedRegex(@"^\[LocalizedText [^:]+:([^\]]+)\]$")]
    private static partial Regex LocalizedToken();

    [GeneratedRegex(@"^(spring|summer|fall|winter)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FestivalKey();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed record FestivalDefinition(string Name, string Season, int Day)
    {
        public int YearDay => ToYearDay(Season, Day);
        public string DateText => $"{Season} {Day} 日";
    }
}
