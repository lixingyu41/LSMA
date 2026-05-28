using System.Reflection;
using System.Text.RegularExpressions;

namespace LSMA.Services;

public sealed partial class NpcLocalizationService(LoggingService logging)
{
    private static readonly IReadOnlyDictionary<string, string> FallbackNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Abigail"] = "阿比盖尔",
            ["Alex"] = "亚历克斯",
            ["Bear"] = "熊",
            ["Birdie"] = "贝啼",
            ["Bouncer"] = "门卫",
            ["Caroline"] = "卡洛琳",
            ["Clint"] = "克林特",
            ["Demetrius"] = "德米特里厄斯",
            ["Dwarf"] = "矮人",
            ["Elliott"] = "艾利欧特",
            ["Emily"] = "艾米丽",
            ["Evelyn"] = "艾芙琳",
            ["George"] = "乔治",
            ["Gil"] = "吉尔",
            ["Governor"] = "州长",
            ["Grandpa"] = "爷爷",
            ["Gunther"] = "冈瑟",
            ["Gus"] = "格斯",
            ["Haley"] = "海莉",
            ["Harvey"] = "哈维",
            ["Henchman"] = "仆从",
            ["Jas"] = "贾斯",
            ["Jodi"] = "乔迪",
            ["Kent"] = "肯特",
            ["Krobus"] = "科罗布斯",
            ["Leah"] = "莉亚",
            ["Leo"] = "雷欧",
            ["Lewis"] = "刘易斯",
            ["Linus"] = "莱纳斯",
            ["Marlon"] = "马龙",
            ["Marnie"] = "玛妮",
            ["Maru"] = "玛鲁",
            ["Mister Qi"] = "齐先生",
            ["Morris"] = "莫里斯",
            ["Old Mariner"] = "老水手",
            ["Pam"] = "潘姆",
            ["Penny"] = "潘妮",
            ["Pierre"] = "皮埃尔",
            ["Robin"] = "罗宾",
            ["Sam"] = "山姆",
            ["Sandy"] = "桑迪",
            ["Sebastian"] = "塞巴斯蒂安",
            ["Shane"] = "谢恩",
            ["Vincent"] = "文森特",
            ["Welwick"] = "维尔维克",
            ["Willy"] = "威利",
            ["Wizard"] = "法师"
        };
    private static readonly IReadOnlyDictionary<string, string> FallbackTextureNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Leo"] = "ParrotBoy"
        };
    private static readonly IReadOnlySet<string> FallbackRomanceable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Abigail", "Alex", "Elliott", "Emily", "Haley", "Harvey",
        "Leah", "Maru", "Penny", "Sam", "Sebastian", "Shane"
    };

    private readonly Dictionary<string, string> _names = FallbackNames.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _textureNames = FallbackTextureNames.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _romanceable = new(FallbackRomanceable, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> NpcIds => _names.Keys;

    public async Task PrepareAsync(string? gameDirectory)
    {
        ResetFallback();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return;
        }

        try
        {
            await Task.Run(() => Load(gameDirectory));
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取游戏角色中文数据失败", exception);
        }
    }

    public string Translate(string npcId)
        => _names.TryGetValue(npcId, out var localized) ? localized : npcId;

    public string TextureName(string npcId)
        => _textureNames.TryGetValue(npcId, out var texture) ? texture : npcId;

    public bool IsRomanceable(string npcId) => _romanceable.Contains(npcId);

    private void ResetFallback()
    {
        _names.Clear();
        foreach (var (key, value) in FallbackNames)
        {
            _names[key] = value;
        }

        _textureNames.Clear();
        foreach (var (key, value) in FallbackTextureNames)
        {
            _textureNames[key] = value;
        }

        _romanceable.Clear();
        _romanceable.UnionWith(FallbackRomanceable);
    }

    private void Load(string gameDirectory)
    {
        var monoGame = Assembly.LoadFrom(Path.Combine(gameDirectory, "MonoGame.Framework.dll"));
        var gameData = Assembly.LoadFrom(Path.Combine(gameDirectory, "StardewValley.GameData.dll"));
        var contentType = monoGame.GetType("Microsoft.Xna.Framework.Content.ContentManager")
            ?? throw new InvalidOperationException("游戏运行库中缺少内容读取组件。");
        using var content = Activator.CreateInstance(
            contentType,
            new EmptyServiceProvider(),
            Path.Combine(gameDirectory, "Content")) as IDisposable
            ?? throw new InvalidOperationException("无法打开游戏内容目录。");

        var names = LoadStringDictionary(contentType, content, "Strings\\NPCNames.zh-CN");
        var characterType = gameData.GetType("StardewValley.GameData.Characters.CharacterData")
            ?? throw new InvalidOperationException("游戏角色数据类型不可用。");
        foreach (var (id, character) in LoadTypedDictionary(contentType, content, "Data\\Characters", characterType))
        {
            var displayName = ResolveText(GetString(character, "DisplayName"), names);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                _names[id] = displayName;
            }

            var textureName = GetString(character, "TextureName");
            if (!string.IsNullOrWhiteSpace(textureName))
            {
                _textureNames[id] = textureName;
            }

            if (GetBool(character, "CanBeRomanced"))
            {
                _romanceable.Add(id);
            }
            else
            {
                _romanceable.Remove(id);
            }
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

    private static bool GetBool(object source, string property)
        => source.GetType().GetField(property)?.GetValue(source) is bool value && value;

    private static string ResolveText(string token, IReadOnlyDictionary<string, string> strings)
    {
        var match = LocalizedToken().Match(token);
        return match.Success && strings.TryGetValue(match.Groups[1].Value, out var value) ? value : token;
    }

    [GeneratedRegex(@"^\[LocalizedText [^:]+:([^\]]+)\]$")]
    private static partial Regex LocalizedToken();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
