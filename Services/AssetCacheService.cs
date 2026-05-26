using System.Diagnostics;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class AssetCacheService(
    AppStateService state,
    SettingsService settings,
    FileSystemSafeService files,
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

        if (string.IsNullOrWhiteSpace(settings.Current.XnbToolPath) || !File.Exists(settings.Current.XnbToolPath))
        {
            throw new InvalidOperationException("需要配置可用的 XNB 解包工具路径。");
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
        foreach (var sourceRoot in sources)
        {
            var category = Path.GetFileName(sourceRoot);
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.xnb").Take(300))
            {
                var temporary = Path.Combine(AppPaths.Temp, "assets", category, Path.GetFileName(source));
                var output = Path.Combine(AppPaths.AssetCache, category, Path.GetFileNameWithoutExtension(source));
                Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                try
                {
                    File.Copy(source, temporary, true);
                    var arguments = settings.Current.XnbArgumentsTemplate
                        .Replace("{input}", temporary, StringComparison.Ordinal)
                        .Replace("{output}", output, StringComparison.Ordinal);
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = settings.Current.XnbToolPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }) ?? throw new InvalidOperationException("无法启动 XNB 工具。");
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        count++;
                    }
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }

        await logging.InfoAsync($"已生成本地素材缓存：{count} 项");
        return count;
    }

    public async Task ClearAsync()
    {
        await files.ClearDirectoryAsync(AppPaths.AssetCache, AppPaths.AssetCache);
        await logging.InfoAsync("已清空本地素材缓存");
    }
}
