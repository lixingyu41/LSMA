using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly int[] ObjectIds =
    [
        24, 143, 150, 258, 270, 276, 282, 400, 414, 498, 698
    ];

    private static readonly IReadOnlyDictionary<string, MonsterIconDefinition> MonsterIconDefinitions =
        new Dictionary<string, MonsterIconDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bat"] = new("Bat", 16, 16),
            ["Big Slime"] = new("Big Slime", 32, 32),
            ["Bug"] = new("Bug", 16, 16),
            ["Duggy"] = new("Duggy", 16, 16),
            ["Dust Spirit"] = new("Dust Spirit", 16, 16),
            ["Fly"] = new("Fly", 16, 16),
            ["Frost Bat"] = new("Frost Bat", 16, 16),
            ["Frost Jelly"] = new("Green Slime", 16, 16),
            ["Green Slime"] = new("Green Slime", 16, 16),
            ["Grub"] = new("Grub", 16, 16),
            ["Sludge"] = new("Green Slime", 16, 16),
            ["Iridium Golem"] = new("Iridium Golem", 16, 32),
            ["Lava Crab"] = new("Lava Crab", 16, 16),
            ["Magma Sparker"] = new("Magma Sparker", 16, 16),
            ["Magma Sprite"] = new("Magma Sprite", 16, 16),
            ["Metal Head"] = new("Metal Head", 16, 32),
            ["Mummy"] = new("Mummy", 16, 32),
            ["Pepper Rex"] = new("Pepper Rex", 32, 32),
            ["Rock Crab"] = new("Rock Crab", 16, 16),
            ["Serpent"] = new("Serpent", 32, 32),
            ["Shadow Brute"] = new("Shadow Brute", 16, 32),
            ["Shadow Shaman"] = new("Shadow Shaman", 16, 32),
            ["Skeleton"] = new("Skeleton", 16, 32),
            ["Skeleton Mage"] = new("Skeleton Mage", 16, 32),
            ["Squid Kid"] = new("Squid Kid", 16, 32),
            ["Stone Golem"] = new("Stone Golem", 16, 32)
        };

    private readonly Dictionary<int, string> _objectIcons = [];
    private readonly Dictionary<string, string> _textureIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XnbTextureSize> _textureSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _portraitIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _skillIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _monsterIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _farmerPortraits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _seasonBackgrounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _blurredSeasonBackgrounds = new(StringComparer.OrdinalIgnoreCase);
    private string? _fullHeartIconUri;
    private string? _emptyHeartIconUri;

    public async Task PrepareAsync()
    {
        _objectIcons.Clear();
        _textureIcons.Clear();
        _textureSizes.Clear();
        _portraitIcons.Clear();
        _skillIcons.Clear();
        _monsterIcons.Clear();
        _farmerPortraits.Clear();
        _seasonBackgrounds.Clear();
        _blurredSeasonBackgrounds.Clear();
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
        await PrepareSaveArtworkAsync(game.Path, outputRoot);
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

    public string? GetTextureIconUri(string texture, int spriteIndex, int width = 16, int height = 16)
        => _textureIcons.GetValueOrDefault(TextureCacheKey(texture, spriteIndex, width, height));

    public async Task<string?> GetTextureIconAsync(string texture, int spriteIndex, int width = 16, int height = 16)
    {
        var cacheKey = TextureCacheKey(texture, spriteIndex, width, height);
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
            $"{Path.GetFileName(texture)}-{spriteIndex}-{width}x{height}.png");
        try
        {
            var size = await GetTextureSizeAsync(source, game.Path);
            var columns = Math.Max(1, size.Width / Math.Max(1, width));
            var x = spriteIndex % columns * width;
            var y = spriteIndex / columns * height;
            if (x + width > size.Width || y + height > size.Height)
            {
                return null;
            }

            await textures.ExportPngRegionAsync(
                source,
                output,
                game.Path,
                x,
                y,
                width,
                height);
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

    private async Task<XnbTextureSize> GetTextureSizeAsync(string source, string gamePath)
    {
        if (_textureSizes.TryGetValue(source, out var size))
        {
            return size;
        }

        size = await textures.GetTextureSizeAsync(source, gamePath);
        _textureSizes[source] = size;
        return size;
    }

    private static string TextureCacheKey(string texture, int spriteIndex, int width, int height)
        => $"{texture}|{spriteIndex}|{width}x{height}";

    public string? GetBlurredSeasonBackgroundUri(string? season)
    {
        var key = SeasonAssetKey(season ?? string.Empty);
        return _blurredSeasonBackgrounds.GetValueOrDefault(key)
            ?? _blurredSeasonBackgrounds.GetValueOrDefault("Spring");
    }

    public async Task ApplySaveIconsAsync(SaveInfo save)
    {
        save.SeasonIconUri = GetObjectIconUri(save.Season switch
        {
            "春季" => 24,
            "夏季" => 258,
            "秋季" => 276,
            "冬季" => 414,
            _ => 24
        });
        save.BackgroundImageUri = _seasonBackgrounds.GetValueOrDefault(SeasonAssetKey(save.Season));

        if (save.Players.Count == 0)
        {
            await ApplySaveInfoIconsAsync(save, save);
            return;
        }

        foreach (var player in save.Players)
        {
            await ApplySaveInfoIconsAsync(save, player);
        }

        var primary = save.Players[0];
        save.PortraitImageUri = primary.PortraitImageUri;
    }

    private async Task ApplySaveInfoIconsAsync(SaveInfo save, SaveInfo player)
    {
        player.SeasonIconUri = save.SeasonIconUri;
        player.BackgroundImageUri = save.BackgroundImageUri;
        player.PortraitImageUri = await PrepareCustomFarmerPortraitAsync(save, player)
            ?? _farmerPortraits.GetValueOrDefault(FarmerGenderKey(player.Gender))
            ?? save.SeasonIconUri;

        foreach (var skill in player.Skills)
        {
            skill.IconUri = GetSkillIconUri(skill.Key) ?? (skill.Key == "Mastery" ? save.SeasonIconUri : null);
        }

        foreach (var fish in player.FishCatchStats)
        {
            if (fish.ObjectId is { } objectId)
            {
                fish.IconUri = await GetObjectIconAsync(objectId);
            }
            else if (fish.IconTexture is { Length: > 0 } texture && fish.IconSpriteIndex is { } spriteIndex)
            {
                fish.IconUri = await GetTextureIconAsync(texture, spriteIndex, fish.IconWidth, fish.IconHeight);
            }
        }

        foreach (var monster in player.MonsterKillStats)
        {
            monster.IconUri = await GetMonsterIconAsync(monster.IconKey ?? monster.Detail);
        }

        foreach (var item in player.CollectionItems.Values.SelectMany(items => items))
        {
            await ApplyCollectionItemIconAsync(item);
        }

        foreach (var item in player.ProgressDetailItems.Values.SelectMany(items => items))
        {
            await ApplyCollectionItemIconAsync(item);
        }

        foreach (var friendship in player.Friendships)
        {
            friendship.IconUri = GetPortraitUri(friendship.NpcId);
            friendship.HeartSlots.Clear();

            var slotCount = friendship.IsSpouse ? 14 : 10;
            var accessibleHearts = friendship.IsDatable && !friendship.IsPartner ? 8 : slotCount;
            var fullHearts = friendship.Points / 250;
            var partialProgress = (friendship.Points % 250) / 250.0;

            for (var index = 0; index < slotCount; index++)
            {
                var isLocked = index >= accessibleHearts;
                var isFull = !isLocked && index < fullHearts;
                var isPartial = !isLocked && !isFull && index == fullHearts && partialProgress > 0;

                friendship.HeartSlots.Add(new SaveFriendshipHeart
                {
                    IconUri = isFull && !isPartial ? _fullHeartIconUri : _emptyHeartIconUri,
                    FullIconUri = _fullHeartIconUri,
                    IsLocked = isLocked,
                    IsPartial = isPartial,
                    FillPercent = isPartial ? partialProgress : (isFull ? 1.0 : 0.0)
                });
            }
        }
    }

    private async Task ApplyCollectionItemIconAsync(SaveCollectionItemInfo item)
    {
        if (item.ObjectId is { } objectId)
        {
            item.IconUri = await GetObjectIconAsync(objectId);
        }
        else if (item.NpcId is { Length: > 0 } npcId)
        {
            item.IconUri = GetPortraitUri(npcId);
        }
        else if (item.IconKey is { Length: > 0 } iconKey)
        {
            item.IconUri = GetSkillIconUri(iconKey);
        }
        else if (item.IconTexture is { Length: > 0 } texture && item.IconSpriteIndex is { } spriteIndex)
        {
            item.IconUri = await GetTextureIconAsync(texture, spriteIndex, item.IconWidth, item.IconHeight);
        }
    }

    private async Task<string?> GetMonsterIconAsync(string monsterId)
    {
        if (string.IsNullOrWhiteSpace(monsterId))
        {
            return null;
        }

        var normalizedId = monsterId.Replace("_dangerous", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (_monsterIcons.TryGetValue(normalizedId, out var cached))
        {
            return cached;
        }

        if (state.GameDirectory is not { } game)
        {
            return null;
        }

        var definition = MonsterIconDefinitions.TryGetValue(normalizedId, out var mapped)
            ? mapped
            : new MonsterIconDefinition(normalizedId, 16, 16);
        var output = Path.Combine(
            AppPaths.AssetCache,
            "InterfaceIcons",
            "Monsters",
            $"{SafeFileName(normalizedId)}.png");
        var sources = new[]
        {
            Path.Combine(game.Path, "Content", "Characters", "Monsters", $"{definition.TextureName}.xnb"),
            Path.Combine(game.Path, "Content", "Characters", $"{definition.TextureName}.xnb")
        };

        foreach (var source in sources.Where(File.Exists))
        {
            try
            {
                await textures.ExportPngRegionAsync(
                    source,
                    output,
                    game.Path,
                    definition.X,
                    definition.Y,
                    definition.Width,
                    definition.Height);
                return _monsterIcons[normalizedId] = ToUri(output);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync($"生成怪物图标失败：{normalizedId}", exception);
                return null;
            }
        }

        return null;
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

    private async Task PrepareSaveArtworkAsync(string gamePath, string outputRoot)
    {
        var output = Path.Combine(outputRoot, "Saves");
        await PrepareBaseFarmerPortraitAsync(gamePath, output, "Male", "farmer_base.xnb");
        await PrepareBaseFarmerPortraitAsync(gamePath, output, "Female", "farmer_girl_base.xnb");

        var backgrounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Spring"] = "map.xnb",
            ["Summer"] = "map_summer.xnb",
            ["Fall"] = "map_fall.xnb",
            ["Winter"] = "map_winter.xnb"
        };

        foreach (var (season, fileName) in backgrounds)
        {
            var source = Path.Combine(gamePath, "Content", "LooseSprites", fileName);
            if (!File.Exists(source))
            {
                continue;
            }

            var outputPath = Path.Combine(output, "Backgrounds", $"{season}.png");
            var blurredPath = Path.Combine(output, "Backgrounds", "Blurred", $"{season}.png");
            try
            {
                await textures.ExportPngAsync(source, outputPath, gamePath);
                await textures.ExportBlurredPngAsync(source, blurredPath, gamePath, 7);
                _seasonBackgrounds[season] = ToUri(outputPath);
                _blurredSeasonBackgrounds[season] = ToUri(blurredPath);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync($"生成存档季节背景失败：{season}", exception);
            }
        }
    }

    private async Task PrepareBaseFarmerPortraitAsync(string gamePath, string outputRoot, string gender, string fileName)
    {
        var source = Path.Combine(gamePath, "Content", "Characters", "Farmer", fileName);
        if (!File.Exists(source))
        {
            return;
        }

        var output = Path.Combine(outputRoot, "Farmers", $"{gender}.png");
        try
        {
            await textures.ExportPngRegionAsync(source, output, gamePath, 0, 0, 16, 32);
            _farmerPortraits[gender] = ToUri(output);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"生成玩家角色图失败：{gender}", exception);
        }
    }

    private async Task<string?> PrepareCustomFarmerPortraitAsync(SaveInfo save, SaveInfo player)
    {
        if (state.GameDirectory is not { } game)
        {
            return null;
        }

        var gender = FarmerGenderKey(player.Gender);
        var output = Path.Combine(
            AppPaths.AssetCache,
            "InterfaceIcons",
            "Saves",
            "Farmers",
            $"v4-{SaveArtworkKey(save)}-{PlayerArtworkKey(player)}-{gender}-h{player.Hair}-{player.HairColorR:X2}{player.HairColorG:X2}{player.HairColorB:X2}-s{player.ShirtIndex}.png");
        if (File.Exists(output))
        {
            return ToUri(output);
        }

        var baseSource = Path.Combine(
            game.Path,
            "Content",
            "Characters",
            "Farmer",
            gender == "Female" ? "farmer_girl_base.xnb" : "farmer_base.xnb");
        if (!File.Exists(baseSource))
        {
            return null;
        }

        try
        {
            var portrait = await textures.LoadTextureRegionAsync(baseSource, game.Path, 0, 0, 16, 32);
            await OverlayPantsAsync(baseSource, game.Path, portrait);
            await OverlayShirtAsync(game.Path, portrait, player.ShirtIndex);
            await OverlayHairAsync(game.Path, portrait, player);
            await OverlayArmsAsync(baseSource, game.Path, portrait);
            await textures.WritePngAsync(output, portrait);
            return ToUri(output);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"生成存档玩家角色图失败：{save.FolderName}", exception);
            return null;
        }
    }

    private async Task OverlayPantsAsync(string baseSource, string gamePath, XnbTexturePixels portrait)
    {
        var layer = await textures.LoadTextureRegionAsync(baseSource, gamePath, 12 * 16, 0, 16, 32);
        Overlay(portrait, layer);
    }

    private async Task OverlayShirtAsync(string gamePath, XnbTexturePixels portrait, int shirtIndex)
    {
        if (shirtIndex < 0)
        {
            return;
        }

        var source = Path.Combine(gamePath, "Content", "Characters", "Farmer", "shirts.xnb");
        if (!File.Exists(source))
        {
            return;
        }

        var layer = await textures.LoadTextureRegionAsync(
            source,
            gamePath,
            shirtIndex % 16 * 8,
            shirtIndex / 16 * 8,
            8,
            8);
        Overlay(portrait, layer, 4, 9);
    }

    private async Task OverlayHairAsync(string gamePath, XnbTexturePixels portrait, SaveInfo save)
    {
        if (save.Hair < 0)
        {
            return;
        }

        var hairIndex = save.Hair;
        var fileName = "hairstyles.xnb";
        if (hairIndex >= 168)
        {
            hairIndex -= 168;
            fileName = "hairstyles2.xnb";
        }

        var source = Path.Combine(gamePath, "Content", "Characters", "Farmer", fileName);
        if (!File.Exists(source))
        {
            return;
        }

        var layer = await textures.LoadTextureRegionAsync(
            source,
            gamePath,
            hairIndex % 8 * 16,
            hairIndex / 8 * 16,
            16,
            16);
        Tint(layer, save.HairColorR, save.HairColorG, save.HairColorB);
        Overlay(portrait, layer);
    }

    private async Task OverlayArmsAsync(string baseSource, string gamePath, XnbTexturePixels portrait)
    {
        var layer = await textures.LoadTextureRegionAsync(baseSource, gamePath, 6 * 16, 0, 16, 32);
        Overlay(portrait, layer);
    }

    private static void Tint(XnbTexturePixels layer, int red, int green, int blue)
    {
        for (var index = 0; index < layer.Pixels.Length; index += 4)
        {
            if (layer.Pixels[index + 3] == 0)
            {
                continue;
            }

            var shade = (layer.Pixels[index] + layer.Pixels[index + 1] + layer.Pixels[index + 2]) / (3d * 255d);
            layer.Pixels[index] = Scale(red, shade);
            layer.Pixels[index + 1] = Scale(green, shade);
            layer.Pixels[index + 2] = Scale(blue, shade);
        }
    }

    private static byte Scale(int value, double shade)
        => (byte)Math.Clamp((int)Math.Round(value * Math.Clamp(shade, 0.35, 1.0)), 0, 255);

    private static void Overlay(XnbTexturePixels target, XnbTexturePixels layer, int offsetX = 0, int offsetY = 0)
    {
        for (var y = 0; y < layer.Height; y++)
        {
            var targetY = y + offsetY;
            if (targetY < 0 || targetY >= target.Height)
            {
                continue;
            }

            for (var x = 0; x < layer.Width; x++)
            {
                var targetX = x + offsetX;
                if (targetX < 0 || targetX >= target.Width)
                {
                    continue;
                }

                var targetIndex = (targetY * target.Width + targetX) * 4;
                var layerIndex = (y * layer.Width + x) * 4;
                var alpha = layer.Pixels[layerIndex + 3] / 255d;
                if (alpha <= 0)
                {
                    continue;
                }

                var inverse = 1 - alpha;
                target.Pixels[targetIndex] = Blend(layer.Pixels[layerIndex], target.Pixels[targetIndex], alpha, inverse);
                target.Pixels[targetIndex + 1] = Blend(layer.Pixels[layerIndex + 1], target.Pixels[targetIndex + 1], alpha, inverse);
                target.Pixels[targetIndex + 2] = Blend(layer.Pixels[layerIndex + 2], target.Pixels[targetIndex + 2], alpha, inverse);
                target.Pixels[targetIndex + 3] = (byte)Math.Clamp(
                    layer.Pixels[layerIndex + 3] + target.Pixels[targetIndex + 3] * inverse,
                    0,
                    255);
            }
        }
    }

    private static byte Blend(byte source, byte target, double alpha, double inverse)
        => (byte)Math.Clamp((int)Math.Round(source * alpha + target * inverse), 0, 255);

    private static string FarmerGenderKey(string? gender)
        => string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male";

    private static string SaveArtworkKey(SaveInfo save)
        => save.UniqueGameId > 0
            ? save.UniqueGameId.ToString(CultureInfo.InvariantCulture)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(save.FolderName))).Substring(0, 16);

    private static string PlayerArtworkKey(SaveInfo player)
    {
        var key = player is SavePlayerInfo savePlayer && !string.IsNullOrWhiteSpace(savePlayer.PlayerKey)
            ? savePlayer.PlayerKey
            : player.FarmerName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
    }

    private static string SeasonAssetKey(string season)
        => season switch
        {
            "夏季" => "Summer",
            "秋季" => "Fall",
            "冬季" => "Winter",
            _ => "Spring"
        };

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private sealed record MonsterIconDefinition(string TextureName, int Width, int Height, int X = 0, int Y = 0);
}
