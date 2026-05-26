using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModScannerService(AppStateService state, LoggingService logging)
{
    public async Task<List<ModInfo>> ScanAsync()
    {
        var results = new List<ModInfo>();
        if (state.GameDirectory is not { } game)
        {
            return results;
        }

        var enabledPath = Path.Combine(game.Path, "Mods");
        var disabledPath = Path.Combine(game.Path, "Mods.Disabled");
        try
        {
            Directory.CreateDirectory(enabledPath);
            foreach (var directory in Directory.EnumerateDirectories(enabledPath))
            {
                await AddModSafelyAsync(results, directory, true);
            }

            if (Directory.Exists(disabledPath))
            {
                foreach (var directory in Directory.EnumerateDirectories(disabledPath))
                {
                    await AddModSafelyAsync(results, directory, false);
                }
            }

            if (Directory.Exists(AppPaths.ArchivedMods))
            {
                foreach (var batch in Directory.EnumerateDirectories(AppPaths.ArchivedMods))
                {
                    foreach (var directory in Directory.EnumerateDirectories(batch))
                    {
                        var archived = await ReadModAsync(directory, false);
                        archived.IsArchived = true;
                        results.Add(archived);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("扫描模组目录失败", exception);
        }

        return results.OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Task<ModInfo> ScanDirectoryAsync(string directory, bool enabled)
    {
        return ReadModAsync(directory, enabled);
    }

    private async Task AddModSafelyAsync(List<ModInfo> results, string directory, bool enabled)
    {
        try
        {
            results.Add(await ReadModAsync(directory, enabled));
        }
        catch (Exception exception)
        {
            var info = new ModInfo
            {
                FolderPath = directory,
                FolderName = Path.GetFileName(directory),
                IsEnabled = enabled
            };
            info.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = "无法读取模组目录" });
            results.Add(info);
            await logging.ErrorAsync($"读取模组目录失败：{directory}", exception);
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
}
