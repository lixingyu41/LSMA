using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class GameIconService(
    AppStateService state,
    XnbTextureService textures,
    NpcLocalizationService npcNames,
    LoggingService logging)
{
    private const int ObjectSheetColumns = 24;
    private const int Objects2SheetColumns = 8;
    private static readonly int[] ObjectIds =
    [
        24, 143, 150, 258, 270, 276, 282, 400, 414, 498, 698
    ];

    private readonly Dictionary<int, string> _objectIcons = [];
    private readonly Dictionary<string, string> _textureIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _portraitIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _skillIcons = new(StringComparer.OrdinalIgnoreCase);
    private string? _fullHeartIconUri;
    private string? _emptyHeartIconUri;

    public async Task PrepareAsync()
    {
        _objectIcons.Clear();
        _textureIcons.Clear();
        _portraitIcons.Clear();
        _skillIcons.Clear();
        _fullHeartIconUri = null;
        _emptyHeartIconUri = null;
        if (state.GameDirectory is not { } game)
        {
            return;
        }

        var outputRoot = Path.Combine(AppPaths.AssetCache, "InterfaceIcons");
        Directory.CreateDirectory(outputRoot);
        await PrepareObjectIconsAsync(game.Path, outputRoot);
        await PrepareInterfaceIconsAsync(game.Path, outputRoot);
        await PreparePortraitIconsAsync(game.Path, outputRoot);
    }

    public string? GetObjectIconUri(int objectId)
        => _objectIcons.GetValueOrDefault(objectId);

    public async Task<string?> GetObjectIconAsync(int objectId)
    {
        if (_objectIcons.TryGetValue(objectId, out var cached))
        {
            return cached;
        }

        if (state.GameDirectory is not { } game)
        {
            return null;
        }

        return await PrepareObjectIconAsync(game.Path, Path.Combine(AppPaths.AssetCache, "InterfaceIcons"), objectId);
    }

    public string? GetPortraitUri(string npcId)
        => _portraitIcons.GetValueOrDefault(npcId);

    public string? GetTextureIconUri(string texture, int spriteIndex)
        => _textureIcons.GetValueOrDefault($"{texture}|{spriteIndex}");

    public async Task<string?> GetTextureIconAsync(string texture, int spriteIndex)
    {
        var cacheKey = $"{texture}|{spriteIndex}";
        if (_textureIcons.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (state.GameDirectory is not { } game)
        {
            return null;
        }

        var source = Path.Combine(game.Path, "Content", $"{texture}.xnb");
        if (!File.Exists(source))
        {
            return null;
        }

        var output = Path.Combine(
            AppPaths.AssetCache,
            "InterfaceIcons",
            "Objects",
            $"{Path.GetFileName(texture)}-{spriteIndex}.png");
        var columns = texture.Equals(@"TileSheets\Objects_2", StringComparison.OrdinalIgnoreCase)
            ? Objects2SheetColumns
            : ObjectSheetColumns;
        try
        {
            await textures.ExportPngRegionAsync(
                source,
                output,
                game.Path,
                spriteIndex % columns * 16,
                spriteIndex / columns * 16,
                16,
                16);
            return _textureIcons[cacheKey] = ToUri(output);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"生成界面物品图标失败：{texture}/{spriteIndex}", exception);
            return null;
        }
    }

    public string? GetSkillIconUri(string key)
        => _skillIcons.GetValueOrDefault(key);

    public void ApplySaveIcons(SaveInfo save)
    {
        save.SeasonIconUri = GetObjectIconUri(save.Season switch
        {
            "春季" => 24,
            "夏季" => 258,
            "秋季" => 276,
            "冬季" => 414,
            _ => 24
        });

        foreach (var skill in save.Skills)
        {
            skill.IconUri = GetSkillIconUri(skill.Key);
        }

        foreach (var friendship in save.Friendships)
        {
            friendship.IconUri = GetPortraitUri(friendship.NpcId);
            friendship.HeartSlots.Clear();
            var slotCount = friendship.IsSpouse ? 14 : 10;
            for (var index = 0; index < slotCount; index++)
            {
                var isLocked = friendship.IsDatable && !friendship.IsPartner && index >= 8;
                friendship.HeartSlots.Add(new SaveFriendshipHeart
                {
                    IconUri = !isLocked && index < friendship.Hearts ? _fullHeartIconUri : _emptyHeartIconUri,
                    IsLocked = isLocked
                });
            }
        }
    }

    private async Task PrepareObjectIconsAsync(string gamePath, string outputRoot)
    {
        if (!File.Exists(Path.Combine(gamePath, "Content", "Maps", "springobjects.xnb")))
        {
            return;
        }

        foreach (var objectId in ObjectIds)
        {
            await PrepareObjectIconAsync(gamePath, outputRoot, objectId);
        }
    }

    private async Task<string?> PrepareObjectIconAsync(string gamePath, string outputRoot, int objectId)
    {
        var source = Path.Combine(gamePath, "Content", "Maps", "springobjects.xnb");
        if (!File.Exists(source))
        {
            return null;
        }

        var output = Path.Combine(outputRoot, "Objects", $"{objectId}.png");
        try
        {
            await textures.ExportPngRegionAsync(
                source,
                output,
                gamePath,
                objectId % ObjectSheetColumns * 16,
                objectId / ObjectSheetColumns * 16,
                16,
                16);
            return _objectIcons[objectId] = ToUri(output);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"生成界面物品图标失败：{objectId}", exception);
            return null;
        }
    }

    private async Task PrepareInterfaceIconsAsync(string gamePath, string outputRoot)
    {
        var cursors = Path.Combine(gamePath, "Content", "LooseSprites", "Cursors.xnb");
        if (!File.Exists(cursors))
        {
            return;
        }

        var skills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Farming"] = 10,
            ["Fishing"] = 20,
            ["Mining"] = 30,
            ["Combat"] = 40,
            ["Foraging"] = 60
        };

        foreach (var (name, x) in skills)
        {
            var output = Path.Combine(outputRoot, "Skills", $"{name}.png");
            await textures.ExportPngRegionAsync(cursors, output, gamePath, x, 428, 10, 10);
            _skillIcons[name] = ToUri(output);
        }

        var fullHeart = Path.Combine(outputRoot, "Social", "HeartFull.png");
        var emptyHeart = Path.Combine(outputRoot, "Social", "HeartEmpty.png");
        await textures.ExportPngRegionAsync(cursors, fullHeart, gamePath, 211, 428, 7, 6);
        await textures.ExportPngRegionAsync(cursors, emptyHeart, gamePath, 218, 428, 7, 6);
        _fullHeartIconUri = ToUri(fullHeart);
        _emptyHeartIconUri = ToUri(emptyHeart);
    }

    private async Task PreparePortraitIconsAsync(string gamePath, string outputRoot)
    {
        foreach (var npcId in npcNames.NpcIds)
        {
            var textureName = npcNames.TextureName(npcId);
            var source = Path.Combine(gamePath, "Content", "Portraits", $"{textureName}.xnb");
            var width = 64;
            var height = 64;
            if (!File.Exists(source))
            {
                source = Path.Combine(gamePath, "Content", "Characters", $"{textureName}.xnb");
                width = 16;
                height = 32;
                if (!File.Exists(source))
                {
                    continue;
                }
            }

            var output = Path.Combine(outputRoot, "Portraits", $"{npcId}.png");
            try
            {
                await textures.ExportPngRegionAsync(source, output, gamePath, 0, 0, width, height);
                _portraitIcons[npcId] = ToUri(output);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync($"生成人物头像图标失败：{npcId}", exception);
            }
        }
    }

    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
}
