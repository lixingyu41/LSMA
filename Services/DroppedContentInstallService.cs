using System.IO.Compression;

namespace LSMA.Services;

public sealed class DroppedContentInstallService(DialogService dialogs, LoggingService logging)
{
    private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };
    private static readonly HashSet<string> ModArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".xz"
    };

    public async Task HandleAsync(IReadOnlyList<string> paths)
    {
        var cleanPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cleanPaths.Count == 0)
        {
            return;
        }

        var groups = new DroppedContentGroups();
        foreach (var path in cleanPaths)
        {
            try
            {
                switch (Classify(path))
                {
                    case DroppedContentKind.Mod:
                        groups.Mods.Add(path);
                        break;
                    case DroppedContentKind.Save:
                        groups.Saves.Add(path);
                        break;
                    case DroppedContentKind.ModPack:
                        groups.ModPacks.Add(path);
                        break;
                    default:
                        groups.Unknown.Add(path);
                        break;
                }
            }
            catch (Exception exception)
            {
                groups.Unknown.Add(path);
                await logging.ErrorAsync($"识别拖入内容失败：{path}", exception);
            }
        }

        if (groups.RecognizedCount == 0)
        {
            await dialogs.ShowMessageAsync("未识别拖入内容", "拖入内容不是可识别的模组、存档或整合包。");
            return;
        }

        if (!await dialogs.ConfirmAsync("拖放安装", CreateConfirmation(groups), "开始处理"))
        {
            return;
        }

        var completed = new List<string>();
        var failed = new List<string>();
        if (groups.Mods.Count > 0)
        {
            if (await App.Current.Services.Mods.InstallDroppedPackagesAsync(groups.Mods, requireConfirmation: false))
            {
                completed.Add($"模组 {groups.Mods.Count} 项");
            }
            else
            {
                failed.Add("模组安装未完成，安装计划已停在模组页。");
            }
        }

        foreach (var savePath in groups.Saves)
        {
            if (await App.Current.Services.Saves.ImportDroppedSaveAsync(savePath, requireConfirmation: false))
            {
                completed.Add($"存档 {DisplayName(savePath)}");
            }
            else
            {
                failed.Add($"存档 {DisplayName(savePath)} 未导入。");
            }
        }

        foreach (var modPackPath in groups.ModPacks)
        {
            if (await App.Current.Services.Mods.ImportDroppedModPackAsync(modPackPath, requireConfirmation: false))
            {
                completed.Add($"整合包 {DisplayName(modPackPath)}");
            }
            else
            {
                failed.Add($"整合包 {DisplayName(modPackPath)} 未导入。");
            }
        }

        if (groups.Unknown.Count > 0)
        {
            failed.Add($"跳过未识别内容 {groups.Unknown.Count} 项。");
        }

        if (failed.Count > 0)
        {
            var message = completed.Count == 0
                ? string.Join(Environment.NewLine, failed)
                : $"已完成：{string.Join("、", completed)}{Environment.NewLine}{string.Join(Environment.NewLine, failed)}";
            await dialogs.ShowMessageAsync("拖放安装结果", message);
        }
    }

    private static string CreateConfirmation(DroppedContentGroups groups)
    {
        var parts = new List<string>();
        if (groups.Mods.Count > 0)
        {
            parts.Add($"模组 {groups.Mods.Count} 项");
        }

        if (groups.Saves.Count > 0)
        {
            parts.Add($"存档 {groups.Saves.Count} 项");
        }

        if (groups.ModPacks.Count > 0)
        {
            parts.Add($"整合包 {groups.ModPacks.Count} 项");
        }

        var message = $"将处理 {string.Join("、", parts)}。同名模组或存档会沿用现有自动备份与回滚流程。";
        return groups.Unknown.Count == 0
            ? message
            : $"{message}{Environment.NewLine}另有 {groups.Unknown.Count} 项无法识别，将跳过。";
    }

    private static DroppedContentKind Classify(string path)
    {
        if (Directory.Exists(path))
        {
            return ClassifyDirectory(path);
        }

        if (File.Exists(path))
        {
            return ClassifyFile(path);
        }

        return DroppedContentKind.Unknown;
    }

    private static DroppedContentKind ClassifyDirectory(string path)
    {
        if (File.Exists(Path.Combine(path, "lsma-modpack.json")))
        {
            return DroppedContentKind.ModPack;
        }

        if (ContainsSaveDirectory(path))
        {
            return DroppedContentKind.Save;
        }

        return ContainsFileNamed(path, "manifest.json")
            ? DroppedContentKind.Mod
            : DroppedContentKind.Unknown;
    }

    private static DroppedContentKind ClassifyFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".lsmapack", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedContentKind.ModPack;
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ModArchiveExtensions.Contains(extension)
                ? DroppedContentKind.Mod
                : DroppedContentKind.Unknown;
        }

        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToList();
        if (entries.Any(entry => Path.GetFileName(entry).Equals("lsma-modpack.json", StringComparison.OrdinalIgnoreCase)))
        {
            return DroppedContentKind.ModPack;
        }

        if (ArchiveContainsSaveDirectory(entries))
        {
            return DroppedContentKind.Save;
        }

        return entries.Any(entry => Path.GetFileName(entry).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            ? DroppedContentKind.Mod
            : DroppedContentKind.Unknown;
    }

    private static bool ContainsFileNamed(string path, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(path, fileName, RecursiveEnumerationOptions).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsSaveDirectory(string path)
    {
        if (IsSaveDirectory(path))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateDirectories(path, "*", RecursiveEnumerationOptions).Any(IsSaveDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSaveDirectory(string path)
    {
        try
        {
            var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var files = Directory.GetFiles(path);
            return files.Any(file => Path.GetFileName(file).Equals("SaveGameInfo", StringComparison.OrdinalIgnoreCase))
                && files.Any(file => IsPrimarySaveFile(Path.GetFileName(file), directoryName));
        }
        catch
        {
            return false;
        }
    }

    private static bool ArchiveContainsSaveDirectory(IEnumerable<string> entries)
    {
        return entries
            .GroupBy(entry => Path.GetDirectoryName(entry.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? string.Empty)
            .Any(group =>
            {
                var directoryName = group.Key.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
                var fileNames = group
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToList();
                return fileNames.Any(name => name.Equals("SaveGameInfo", StringComparison.OrdinalIgnoreCase))
                    && fileNames.Any(name => IsPrimarySaveFile(name, directoryName));
            });
    }

    private static bool IsPrimarySaveFile(string fileName, string directoryName)
    {
        return !fileName.Equals("SaveGameInfo", StringComparison.OrdinalIgnoreCase)
            && (fileName.Equals(directoryName, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Path.GetExtension(fileName)));
    }

    private static string DisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private enum DroppedContentKind
    {
        Unknown,
        Mod,
        Save,
        ModPack
    }

    private sealed class DroppedContentGroups
    {
        public List<string> Mods { get; } = [];
        public List<string> Saves { get; } = [];
        public List<string> ModPacks { get; } = [];
        public List<string> Unknown { get; } = [];
        public int RecognizedCount => Mods.Count + Saves.Count + ModPacks.Count;
    }
}
