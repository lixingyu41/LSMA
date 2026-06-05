using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModScannerService(AppStateService state, LoggingService logging)
{
    private const int MaxConcurrentReads = 8;

    public async Task<List<ModInfo>> ScanAsync()
    {
        var targets = new List<ModScanTarget>();
        if (state.GameDirectory is not { } game)
        {
            return [];
        }

        var enabledPath = Path.Combine(game.Path, "Mods");
        var disabledPath = Path.Combine(game.Path, "Mods.Disabled");
        try
        {
            Directory.CreateDirectory(enabledPath);
            foreach (var directory in Directory.EnumerateDirectories(enabledPath))
            {
                targets.Add(new ModScanTarget(directory, true, false));
            }

            if (Directory.Exists(disabledPath))
            {
                foreach (var directory in Directory.EnumerateDirectories(disabledPath))
                {
                    targets.Add(new ModScanTarget(directory, false, false));
                }
            }

            if (Directory.Exists(AppPaths.ArchivedMods))
            {
                foreach (var batch in Directory.EnumerateDirectories(AppPaths.ArchivedMods))
                {
                    foreach (var directory in Directory.EnumerateDirectories(batch))
                    {
                        targets.Add(new ModScanTarget(directory, false, true));
                    }
                }
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("扫描模组目录失败", exception);
        }

        var results = await ReadModsAsync(targets);
        return results.OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Task<ModInfo> ScanDirectoryAsync(string directory, bool enabled)
    {
        return ReadModAsync(directory, enabled);
    }

    private async Task<IReadOnlyList<ModInfo>> ReadModsAsync(IReadOnlyList<ModScanTarget> targets)
    {
        using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 2, MaxConcurrentReads));
        var tasks = targets.Select(async target =>
        {
            await gate.WaitAsync();
            try
            {
                return await ReadModSafelyAsync(target);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<ModInfo> ReadModSafelyAsync(ModScanTarget target)
    {
        try
        {
            var info = await ReadModAsync(target.Directory, target.Enabled);
            info.IsArchived = target.Archived;
            return info;
        }
        catch (Exception exception)
        {
            var info = new ModInfo
            {
                FolderPath = target.Directory,
                FolderName = Path.GetFileName(target.Directory),
                IsEnabled = target.Enabled,
                IsArchived = target.Archived
            };
            info.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = "无法读取模组目录" });
            await logging.ErrorAsync($"读取模组目录失败：{target.Directory}", exception);
            return info;
        }
    }

    private async Task<ModInfo> ReadModAsync(string directory, bool enabled)
    {
        var info = new ModInfo
        {
            FolderPath = directory,
            FolderName = Path.GetFileName(directory),
            IsEnabled = enabled
        };
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            info.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = "模组信息文件缺失" });
            var nestedManifests = Directory.EnumerateFiles(directory, "manifest.json", SearchOption.AllDirectories).ToList();
            if (nestedManifests.Count == 1)
            {
                var nestedDirectory = Path.GetDirectoryName(nestedManifests[0]);
                if (nestedDirectory is not null && !nestedDirectory.Equals(directory, StringComparison.OrdinalIgnoreCase))
                {
                    info.SuggestedNestedDirectory = nestedDirectory;
                }

                info.Issues.Add(new ModIssue { Severity = IssueSeverity.Warning, Message = "目录嵌套异常，请检查安装层级" });
            }
            else if (nestedManifests.Count > 1)
            {
                info.Issues.Add(new ModIssue { Severity = IssueSeverity.Warning, Message = "目录内包含多个模组，建议重新安装" });
            }

            return info;
        }

        try
        {
            info.Manifest = await JsonHelper.ReadAsync<ModManifest>(manifestPath);
            if (info.Manifest is null)
            {
                info.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = "模组信息文件无效" });
            }
        }
        catch (Exception exception)
        {
            info.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = "模组信息文件无效" });
            await logging.ErrorAsync($"解析模组信息文件失败：{directory}", exception);
        }

        return info;
    }

    private sealed record ModScanTarget(string Directory, bool Enabled, bool Archived);
}
