using LSMA.Utilities;

namespace LSMA.Services;

public sealed class AssetCacheService(
    AppStateService state,
    SettingsService settings,
    FileSystemSafeService files,
    XnbTextureService textures,
    LoggingService logging)
{
    public async Task<int> BuildAsync()
    {
        if (!settings.Current.LocalAssetCacheEnabled)
        {
            throw new InvalidOperationException("请先启用本地素材缓存。");
        }

        if (state.GameDirectory is not { } game)
        {
            throw new InvalidOperationException("请先连接游戏目录。");
        }

        var sources = new[]
        {
            Path.Combine(game.Path, "Content", "Portraits"),
            Path.Combine(game.Path, "Content", "Characters")
        }.Where(Directory.Exists).ToList();
        if (sources.Count == 0)
        {
            throw new DirectoryNotFoundException("游戏目录中未找到可读取的本地素材。");
        }

        await files.ClearDirectoryAsync(AppPaths.AssetCache, AppPaths.AssetCache);
        var count = 0;
        var failures = 0;
        foreach (var sourceRoot in sources)
        {
            var category = Path.GetFileName(sourceRoot);
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.xnb").Take(300))
            {
                var output = Path.Combine(AppPaths.AssetCache, category, $"{Path.GetFileNameWithoutExtension(source)}.png");
                try
                {
                    await textures.ExportPngAsync(source, output, game.Path);
                    count++;
                }
                catch (Exception exception)
                {
                    failures++;
                    await logging.ErrorAsync($"读取本地素材失败：{source}", exception);
                }
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException("未能读取游戏素材，请确认游戏文件完整。");
        }

        await logging.InfoAsync($"已生成本地素材缓存：成功 {count} 项，失败 {failures} 项");
        return count;
    }

    public async Task ClearAsync()
    {
        await files.ClearDirectoryAsync(AppPaths.AssetCache, AppPaths.AssetCache);
        await logging.InfoAsync("已清空本地素材缓存");
    }
}
